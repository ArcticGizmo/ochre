using System.Text;
using Ochre.Core;
using Ochre.Core.Platform;
using Ochre.Modules.Terminals;
using Xunit;

namespace Ochre.Tests;

// Data-loss and security boundary (security.md threat 6): terminal tabs and their scrollback across a quit. The shell
// is an in-memory fake, so nothing is started.
public sealed class TerminalPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N"));
    private readonly AppLog _log;
    private readonly FakeProtector _protector = new();

    public TerminalPersistenceTests() => _log = new AppLog(Path.Combine(_root, "logs"));

    private string Folder => Path.Combine(_root, "terminals");

    private TerminalStore Store() => new(Folder, _protector);

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void List_round_trips()
    {
        var store = Store();
        var saved = new SavedTerminal("t-0123abcd", "pwsh", @"C:\work\app", "T-0042", "pnpm dev", "vite", KeepScrollback: false);
        var at = new DateTimeOffset(2026, 11, 26, 18, 52, 0, TimeSpan.Zero);
        store.SaveList([saved], at);

        var loaded = store.Load();
        Assert.False(loaded.ReadFailed);
        Assert.Equal(at, loaded.SavedAt);
        Assert.Equal(saved, Assert.Single(loaded.Terminals));
    }

    [Fact]
    public void Scrollback_is_never_written_in_plain_text()
    {
        var store = Store();
        store.SaveScrollback("t-0123abcd", "export API_TOKEN=secret-value");

        var raw = File.ReadAllBytes(Path.Combine(Folder, "t-0123abcd.scrollback"));
        Assert.DoesNotContain("secret-value", Encoding.UTF8.GetString(raw));
        Assert.Equal("export API_TOKEN=secret-value", store.LoadScrollback("t-0123abcd"));
    }

    [Fact]
    public void Scrollback_that_cannot_be_decrypted_reads_as_none()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "t-0123abcd.scrollback"), "not ours");
        Assert.Null(Store().LoadScrollback("t-0123abcd"));
    }

    [Fact]
    public void Ids_from_the_file_must_be_terminal_ids()
    {
        // The id names a file, so a hand-edited list mustn't reach outside the folder.
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "terminals.json"), """
            { "terminals": [
              { "id": "..\\..\\tasks\\T-0001", "profile": "pwsh", "folder": "C:\\x" },
              { "id": "t-0123abcd", "profile": "pwsh", "folder": "C:\\x" },
              { "id": "t-0123abcd", "profile": "cmd", "folder": "C:\\y" },
              { "id": "t-89abcdef", "folder": "C:\\x" }
            ] }
            """);
        var loaded = Store().Load();
        var only = Assert.Single(loaded.Terminals);
        Assert.Equal("t-0123abcd", only.Id);
        Assert.True(only.KeepScrollback);
        Assert.Throws<ArgumentException>(() => Store().SaveScrollback("../escape", "x"));
    }

    [Fact]
    public void A_damaged_list_reads_as_failed_not_empty()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "terminals.json"), "{ not json");
        Assert.True(Store().Load().ReadFailed);
    }

    [Fact]
    public async Task A_tab_comes_back_with_its_scrollback_and_its_command_offered_not_run()
    {
        string id;
        var first = new FakeTerminals();
        using (var manager = new TerminalManager(first, _log))
        using (var saver = new TerminalPersistence(manager, Store(), _log))
        {
            var t = manager.Open("fake", _root, "T-0042", "pnpm dev", 80, 24);
            id = t.Id;
            first.Last.Emit("server listening\r\n");
            await Until(() => manager.Snapshot(t).Text.Contains("server listening"));
            saver.SaveNow();
        }

        var second = new FakeTerminals();
        using var again = new TerminalManager(second, _log);
        using var restorer = new TerminalPersistence(again, Store(), _log);
        Assert.Equal(1, restorer.RestoreAll());

        var back = Assert.Single(again.All);
        Assert.Equal(id, back.Id);
        Assert.Equal("T-0042", back.TaskId);
        Assert.True(back.Running);
        Assert.True(back.OfferRerun);
        Assert.NotNull(back.RestoredFrom);
        var text = again.Snapshot(back).Text;
        Assert.Contains("server listening", text);
        Assert.Contains("restored", text);
        Assert.DoesNotContain("pnpm dev", second.Last.Written);

        again.Rerun(back);
        Assert.Equal("pnpm dev\r", second.Last.Written);
        Assert.False(back.OfferRerun);
    }

    [Fact]
    public async Task Closing_a_tab_or_turning_off_keep_deletes_its_saved_scrollback()
    {
        var shells = new FakeTerminals();
        using var manager = new TerminalManager(shells, _log);
        using var saver = new TerminalPersistence(manager, Store(), _log);
        var a = manager.Open("fake", _root, null, null, 80, 24);
        shells.Last.Emit("a-output\r\n");
        var b = manager.Open("fake", _root, null, null, 80, 24);
        shells.Last.Emit("b-output\r\n");
        await Until(() => manager.Snapshot(a).Text.Contains("a-output") && manager.Snapshot(b).Text.Contains("b-output"));
        saver.SaveNow();
        Assert.True(Store().HasScrollback(a.Id));
        Assert.True(Store().HasScrollback(b.Id));

        manager.Close(a);
        manager.SetKeepScrollback(b, false);
        saver.SaveNow();

        Assert.False(Store().HasScrollback(a.Id));
        Assert.False(Store().HasScrollback(b.Id));
        var saved = Assert.Single(Store().Load().Terminals);
        Assert.Equal(b.Id, saved.Id);
        Assert.False(saved.KeepScrollback);
    }

    [Fact]
    public void A_tab_opened_without_keep_never_writes_scrollback()
    {
        var shells = new FakeTerminals();
        using var manager = new TerminalManager(shells, _log);
        using var saver = new TerminalPersistence(manager, Store(), _log);
        var t = manager.Open("fake", _root, null, null, 80, 24, keepScrollback: false);
        shells.Last.Emit("password: hunter2\r\n");
        Thread.Sleep(100);
        saver.SaveNow();
        Assert.False(Store().HasScrollback(t.Id));
    }

    [Fact]
    public void An_unreadable_list_is_left_alone()
    {
        Directory.CreateDirectory(Folder);
        var list = Path.Combine(Folder, "terminals.json");
        File.WriteAllText(list, "{ damaged");
        var shells = new FakeTerminals();
        using var manager = new TerminalManager(shells, _log);
        using var saver = new TerminalPersistence(manager, Store(), _log);
        Assert.Equal(0, saver.RestoreAll());
        manager.Open("fake", _root, null, null, 80, 24);
        saver.SaveNow();
        Assert.Equal("{ damaged", File.ReadAllText(list));
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FakeProtector : IDataProtector
    {
        private static readonly byte[] Header = "FAKE"u8.ToArray();

        public byte[] Protect(byte[] data) => [.. Header, .. data.Select(b => (byte)(b ^ 0x5a))];

        public byte[]? Unprotect(byte[] data) =>
            data.Length >= Header.Length && data.AsSpan(0, Header.Length).SequenceEqual(Header)
                ? data.Skip(Header.Length).Select(b => (byte)(b ^ 0x5a)).ToArray()
                : null;
    }

    private sealed class FakeTerminals : ITerminalPlatform
    {
        public FakeConsole Last { get; private set; } = null!;

        public IReadOnlyList<ShellProfile> DetectShells() => [new ShellProfile("fake", "Fake shell", @"C:\fake\shell.exe", "")];

        public IPseudoConsole Start(ShellProfile shell, string folder, int cols, int rows, Action<string> onOutput, Action<int> onExited) =>
            Last = new FakeConsole(onOutput, onExited);
    }

    private sealed class FakeConsole(Action<string> onOutput, Action<int> onExited) : IPseudoConsole
    {
        public string Written { get; private set; } = "";

        public void Emit(string text) => onOutput(text);

        public void Write(string data) => Written += data;

        public void Resize(int cols, int rows)
        {
        }

        public void Dispose() => onExited(0);
    }
}
