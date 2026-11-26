using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Ochre.Core.Claude;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Terminals;

/// <summary>Terminals on Windows: ConPTY, with the shells found on this machine.</summary>
public sealed class ConPtyPlatform : ITerminalPlatform
{
    // Each shell is told to report its folder at every prompt with Windows Terminal's OSC 9;9, which the host
    // reads to reopen a restored tab where the user had cd'd to. The user's own prompt is kept and wrapped.

    // PowerShell: run after the user's profile, so it wraps whatever prompt the profile set. Kept free of double
    // quotes so it survives the command line as one argument; not a script file, which execution policy can block.
    private const string PowerShellFolderHook =
        @"$global:__OchrePrompt=$function:prompt;function global:prompt{$o=& $global:__OchrePrompt;if($PWD.Provider.Name -eq 'FileSystem'){-join([char]27,']9;9;',[char]34,$PWD.ProviderPath,[char]34,[char]27,'\',$o)}else{$o}}";

    // cmd: $e is ESC and $P the current folder.
    private const string CmdFolderPrompt = @"$e]9;9;$P$e\";

    // Git Bash: cygpath turns /c/work into C:\work, the form the host checks.
    private const string BashFolderCommand = @"printf '\033]9;9;%s\033\\' ""$(cygpath -w ""$PWD"")""";

    public IReadOnlyList<ShellProfile> DetectShells()
    {
        var powerShellArgs = $"-NoLogo -NoExit -Command \"{PowerShellFolderHook}\"";
        var shells = new List<ShellProfile>();
        if (ExecutableResolver.Find("pwsh") is { } pwsh) shells.Add(new ShellProfile("pwsh", "PowerShell", pwsh, powerShellArgs));
        var windowsPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(windowsPowerShell)) shells.Add(new ShellProfile("powershell", "Windows PowerShell", windowsPowerShell, powerShellArgs));
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (File.Exists(cmd))
        {
            var prompt = CmdFolderPrompt + (Environment.GetEnvironmentVariable("PROMPT") is { Length: > 0 } own ? own : "$P$G");
            shells.Add(new ShellProfile("cmd", "Command Prompt", cmd, "", new Dictionary<string, string> { ["PROMPT"] = prompt }));
        }
        var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        if (File.Exists(gitBash))
        {
            var command = BashFolderCommand + (Environment.GetEnvironmentVariable("PROMPT_COMMAND") is { Length: > 0 } own ? "; " + own : "");
            shells.Add(new ShellProfile("git-bash", "Git Bash", gitBash, "--login -i", new Dictionary<string, string> { ["PROMPT_COMMAND"] = command }));
        }
        return shells;
    }

    public IPseudoConsole Start(ShellProfile shell, string folder, int cols, int rows, Action<string> onOutput, Action<int> onExited) =>
        ConPtySession.Start(shell, folder, cols, rows, onOutput, onExited);
}

/// <summary>A shell attached to a ConPTY (lifted from the M0 spike's PseudoConsoleSession).</summary>
internal sealed class ConPtySession : IPseudoConsole
{
    private const uint StillActive = 259;

    private readonly nint _pseudoConsole;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly ManualResetEvent _processExited;
    private readonly Action<string> _onOutput;
    private readonly Action<int> _onExited;
    private readonly BlockingCollection<string> _writes = new();
    private RegisteredWaitHandle? _exitWait;
    private int _consoleClosed;

    private ConPtySession(nint pseudoConsole, FileStream input, FileStream output, nint process, Action<string> onOutput, Action<int> onExited)
    {
        _pseudoConsole = pseudoConsole;
        _input = input;
        _output = output;
        _processExited = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(process, ownsHandle: true) };
        _onOutput = onOutput;
        _onExited = onExited;
    }

    public static ConPtySession Start(ShellProfile shell, string folder, int cols, int rows, Action<string> onOutput, Action<int> onExited)
    {
        if (!Native.CreatePipe(out var ptyInputRead, out var inputWrite, 0, 0) ||
            !Native.CreatePipe(out var outputRead, out var ptyOutputWrite, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed");
        }

        var hr = Native.CreatePseudoConsole(Size(cols, rows), ptyInputRead, ptyOutputWrite, 0, out var hpc);
        Marshal.ThrowExceptionForHR(hr);

        var attrSize = (nint)0;
        Native.InitializeProcThreadAttributeList(0, 1, 0, ref attrSize);
        var attrList = Marshal.AllocHGlobal(attrSize);
        var commandLine = shell.Arguments.Length == 0 ? Quote(shell.Executable) : $"{Quote(shell.Executable)} {shell.Arguments}";
        var cmd = Marshal.StringToHGlobalUni(commandLine);
        var environment = shell.Environment is { Count: > 0 } extra ? Marshal.StringToHGlobalUni(EnvironmentBlock(extra)) : 0;
        var started = false;
        try
        {
            if (!Native.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed");
            if (!Native.UpdateProcThreadAttribute(attrList, 0, Native.ProcThreadAttributePseudoConsole, hpc, IntPtr.Size, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed");

            var si = new Native.StartupInfoEx { AttributeList = attrList };
            si.StartupInfo.Cb = Marshal.SizeOf<Native.StartupInfoEx>();

            // The executable is an absolute path (ShellProfile), so CreateProcess never searches a folder for it.
            var flags = Native.ExtendedStartupInfoPresent | (environment != 0 ? Native.CreateUnicodeEnvironment : 0);
            if (!Native.CreateProcess(null, cmd, 0, 0, false, flags, environment, folder, ref si, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"could not start {Path.GetFileName(shell.Executable)}");
            started = true;
            Native.CloseHandle(pi.Thread);

            // ConPTY holds its own duplicates; keeping ours open would stop the output pipe from ever closing.
            ptyInputRead.Dispose();
            ptyOutputWrite.Dispose();

            var session = new ConPtySession(
                hpc,
                new FileStream(inputWrite, FileAccess.Write, 0, false),
                new FileStream(outputRead, FileAccess.Read, 0, false),
                pi.Process,
                onOutput,
                onExited);
            session.Begin();
            return session;
        }
        finally
        {
            Native.DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            Marshal.FreeHGlobal(cmd);
            if (environment != 0) Marshal.FreeHGlobal(environment);
            if (!started)
            {
                Native.ClosePseudoConsole(hpc);
                ptyInputRead.Dispose();
                ptyOutputWrite.Dispose();
                inputWrite.Dispose();
                outputRead.Dispose();
            }
        }
    }

    // Ochre's environment with the profile's variables on top, as CreateProcess wants it: "name=value" entries sorted
    // by name, each ending in a NUL, then one more NUL (StringToHGlobalUni adds the last).
    private static string EnvironmentBlock(IReadOnlyDictionary<string, string> extra)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            if (e.Key is string k && e.Value is string v && k.Length > 0 && !k.Contains('=', StringComparison.Ordinal)) vars[k] = v;
        }
        foreach (var (k, v) in extra) vars[k] = v;
        var sb = new StringBuilder();
        foreach (var (k, v) in vars) sb.Append(k).Append('=').Append(v).Append('\0');
        return sb.ToString();
    }

    private static string Quote(string path) => path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static Native.Coord Size(int cols, int rows) => new() { X = (short)Math.Clamp(cols, 2, 1000), Y = (short)Math.Clamp(rows, 1, 500) };

    private void Begin()
    {
        // When the shell ends, closing the console is what lets the reader reach the end of its output. Registered
        // before the reader starts, so the reader's cleanup always finds it.
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_processExited, (_, _) => CloseConsole(), null, Timeout.Infinite, executeOnlyOnce: true);
        new Thread(ReadLoop) { IsBackground = true, Name = "pty-reader" }.Start();
        new Thread(WriteLoop) { IsBackground = true, Name = "pty-writer" }.Start();
    }

    public void Write(string data)
    {
        try
        {
            _writes.Add(data);
        }
        catch (InvalidOperationException)
        {
            // Closed: input after the shell ended goes nowhere.
        }
    }

    public void Resize(int cols, int rows)
    {
        if (Volatile.Read(ref _consoleClosed) == 0) Native.ResizePseudoConsole(_pseudoConsole, Size(cols, rows));
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var data in _writes.GetConsumingEnumerable())
            {
                _input.Write(Encoding.UTF8.GetBytes(data));
                _input.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The console closed under a pending write.
        }
    }

    private void ReadLoop()
    {
        // A stateful decoder, because a UTF-8 sequence can straddle two reads.
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[64 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        try
        {
            int read;
            while ((read = _output.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                if (count > 0) _onOutput(new string(chars, 0, count));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Pipe broken on close.
        }

        _exitWait?.Unregister(null);
        _writes.CompleteAdding();
        _output.Dispose();
        int code = -1;
        if (Native.GetExitCodeProcess(_processExited.SafeWaitHandle, out var exit) && exit != StillActive) code = unchecked((int)exit);
        _processExited.Dispose();
        _onExited(code);
    }

    private void CloseConsole()
    {
        if (Interlocked.Exchange(ref _consoleClosed, 1) == 0) Native.ClosePseudoConsole(_pseudoConsole);
    }

    public void Dispose()
    {
        _writes.CompleteAdding();
        // Closing the pseudoconsole ends the shell and everything attached to its console, and lets the reader drain to the end.
        CloseConsole();
        _input.Dispose();
    }
}

internal static partial class Native
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const nint ProcThreadAttributePseudoConsole = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        public int Cb;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, nint attributes, int size);

    [LibraryImport("kernel32.dll")]
    internal static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint pseudoConsole);

    [LibraryImport("kernel32.dll")]
    internal static partial int ResizePseudoConsole(nint pseudoConsole, Coord size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(nint pseudoConsole);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(nint attributeList, int attributeCount, int flags, ref nint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(nint attributeList, uint flags, nint attribute, nint value, nint size, nint previousValue, nint returnSize);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(nint attributeList);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(
        string? applicationName,
        nint commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(SafeWaitHandle process, out uint exitCode);
}
