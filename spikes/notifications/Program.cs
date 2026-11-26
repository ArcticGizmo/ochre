using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NotifySpike;

// M5 notifications spike. Answers: can an unpackaged .NET app show rich notifications (header per task, buttons,
// text and choice boxes, logo per status, progress, reminder/urgent scenarios), receive the clicks and typed text
// in-process, replace and remove them by tag and group, and what happens to a click once the process has quit
// (protocol activation). Findings go to docs/spike-notifications-results.md.
internal static class Program
{
    private const string Aumid = "Ochre.Spike.Notifications";
    private const string Protocol = "ochre-spike";
    private const string TaskId = "T-0042";
    private const string TaskTitle = "Fix the login redirect";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ochre-spike");
    private static readonly string LogPath = Path.Combine(DataDir, "activations.log");

    // Toast objects must stay referenced or their Activated/Dismissed events are lost to the GC.
    private static readonly List<ToastNotification> Shown = [];
    private static bool _protocolMode;
    private static uint _sequence;

    private static int Main(string[] args)
    {
        Directory.CreateDirectory(DataDir);

        if (args.Length > 0 && args[0].StartsWith(Protocol + ":", StringComparison.OrdinalIgnoreCase))
            return ProtocolLaunch(args[0]);

        if (args is ["unregister"])
        {
            Unregister();
            return 0;
        }

        Register();
        WriteLogos();
        Console.WriteLine($"""
            Notify spike. AUMID {Aumid}; activations also go to {LogPath}
            Commands:
              p    permission (reminder scenario, Allow/Deny/Open)
              pu   permission with the urgent scenario
              q    question with a choice box
              pl   plan approval with a feedback box
              f    finished turn with a reply box
              x    failed turn
              w    observed session waiting (Open only)
              pr   progress: a working agent, updated in place for ~10 s
              rep  replace the permission in place (same tag) with a new command
              rm   remove the permission notification (answered elsewhere)
              cg   clear the task's group
              h    list what Windows still holds for this AUMID
              mode toggle button activation: foreground (in-process) / protocol (launches {Protocol}:)
              quit exit (notifications stay in the notification centre)
            Run `notify-spike unregister` afterwards to remove the registry keys.
            """);

        while (true)
        {
            Console.Write($"[{(_protocolMode ? "protocol" : "foreground")}]> ");
            var line = Console.ReadLine()?.Trim();
            if (line is null or "quit") break;
            try
            {
                Run(line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return 0;
    }

    private static void Run(string command)
    {
        switch (command)
        {
            case "p": Permission("reminder", "git push --force-with-lease origin fix/login-redirect"); break;
            case "pu": Permission("urgent", "rm -rf ./build && dotnet build"); break;
            case "rep": Permission("reminder", "dotnet test tests/Ochre.Tests/Ochre.Tests.csproj"); break;
            case "q": Question(); break;
            case "pl": Plan(); break;
            case "f": Finished(); break;
            case "x": Failed(); break;
            case "w": Waiting(); break;
            case "pr": _ = Progress(); break;
            case "rm":
                ToastNotificationManager.History.Remove("s-builder", TaskId, Aumid);
                Console.WriteLine("  removed tag s-builder");
                break;
            case "cg":
                ToastNotificationManager.History.RemoveGroup(TaskId, Aumid);
                Console.WriteLine($"  cleared group {TaskId}");
                break;
            case "h":
                foreach (var t in ToastNotificationManager.History.GetHistory(Aumid))
                    Console.WriteLine($"  tag={t.Tag} group={t.Group} expires={t.ExpirationTime}");
                break;
            case "mode":
                _protocolMode = !_protocolMode;
                if (_protocolMode) RegisterProtocol();
                break;
            case "": break;
            default: Console.WriteLine("  unknown command"); break;
        }
    }

    // --- Content -------------------------------------------------------------------------------------------------

    private static void Permission(string scenario, string commandLine) => Show("s-builder", $"""
        <toast scenario="{scenario}" useButtonStyle="true" {Launch("open", "s-builder")}>
          {Header()}
          <visual><binding template="ToastGeneric">
            <text>builder wants to run Bash</text>
            <text>{X(commandLine)}</text>
            <text placement="attribution">ochre · fix/login-redirect</text>
            {Logo("waiting")}
          </binding></visual>
          <actions>
            <action content="Allow" hint-buttonStyle="Success" {Act("allow", "s-builder")}/>
            <action content="Deny" hint-buttonStyle="Critical" {Act("deny", "s-builder")}/>
            <action content="Open" {Act("open", "s-builder")}/>
          </actions>
        </toast>
        """);

    private static void Question() => Show("s-planner", $"""
        <toast scenario="reminder" {Launch("open", "s-planner")}>
          {Header()}
          <visual><binding template="ToastGeneric">
            <text>planner asks</text>
            <text>Which session store should the login fix use?</text>
            {Logo("waiting")}
          </binding></visual>
          <actions>
            <input id="answer" type="selection" defaultInput="cookie">
              <selection id="cookie" content="Cookie session (current)"/>
              <selection id="redis" content="Redis-backed session"/>
              <selection id="jwt" content="Stateless JWT"/>
            </input>
            <action content="Answer" {Act("answer", "s-planner")}/>
            <action content="Open" {Act("open", "s-planner")}/>
          </actions>
        </toast>
        """);

    private static void Plan() => Show("s-planner", $"""
        <toast scenario="reminder" {Launch("open", "s-planner")}>
          {Header()}
          <visual><binding template="ToastGeneric">
            <text>planner has a plan for you</text>
            <text>1. Reproduce the redirect loop  2. Fix ReturnUrl validation  3. Add a regression test</text>
            {Logo("waiting")}
          </binding></visual>
          <actions>
            <input id="feedback" type="text" placeHolderContent="Feedback (to reject)"/>
            <action content="Reject" hint-inputId="feedback" {Act("reject", "s-planner")}/>
            <action content="Approve" {Act("approve", "s-planner")}/>
            <action content="Open" {Act("open", "s-planner")}/>
          </actions>
        </toast>
        """);

    private static void Finished() => Show("s-tester", $"""
        <toast {Launch("open", "s-tester")}>
          {Header()}
          <visual><binding template="ToastGeneric">
            <text>tester finished</text>
            <text>All 586 tests pass. The redirect now keeps the ReturnUrl only when it is local.</text>
            {Logo("finished")}
          </binding></visual>
          <actions>
            <input id="reply" type="text" placeHolderContent="Reply to tester"/>
            <action content="Send" hint-inputId="reply" {Act("send", "s-tester")}/>
            <action content="Open" {Act("open", "s-tester")}/>
          </actions>
        </toast>
        """);

    private static void Failed() => Show("s-tester", $"""
        <toast {Launch("open", "s-tester")}>
          {Header()}
          <visual><binding template="ToastGeneric">
            <text>tester stopped with an error</text>
            <text>API error: overloaded (529). The turn didn't finish.</text>
            {Logo("failed")}
          </binding></visual>
          <actions>
            <action content="Open" {Act("open", "s-tester")}/>
          </actions>
        </toast>
        """);

    private static void Waiting() => Show("s-outside", $"""
        <toast {Launch("open", "s-outside")}>
          {Header()}
          <visual><binding template="ToastGeneric">
            <text>A session outside Ochre is waiting</text>
            <text>In a terminal at ochre (read-only here)</text>
            {Logo("observed")}
          </binding></visual>
          <actions>
            <action content="Open" {Act("open", "s-outside")}/>
          </actions>
        </toast>
        """);

    private static async Task Progress()
    {
        Show("s-worker", $$"""
            <toast {{Launch("open", "s-worker")}}>
              {{Header()}}
              <visual><binding template="ToastGeneric">
                <text>worker is working</text>
                <progress title="{progressTitle}" value="{progressValue}" valueStringOverride="{progressText}" status="{progressStatus}"/>
                {{Logo("busy")}}
              </binding></visual>
            </toast>
            """, ProgressData(0, "Reading the auth middleware"));
        string[] steps = ["Running Grep", "Editing LoginController.cs", "Running dotnet test", "Writing the summary"];
        for (var i = 0; i < steps.Length; i++)
        {
            await Task.Delay(2500);
            var result = ToastNotificationManager.CreateToastNotifier(Aumid)
                .Update(ProgressData((i + 1.0) / steps.Length, steps[i]), "s-worker", TaskId);
            Console.WriteLine($"  progress update {i + 1}: {result}");
        }
    }

    private static NotificationData ProgressData(double value, string status) => new(
        new Dictionary<string, string>
        {
            ["progressTitle"] = "Turn 3",
            ["progressValue"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["progressText"] = $"{(int)(value * 100)}%",
            ["progressStatus"] = status,
        },
        ++_sequence);

    // --- Building blocks -------------------------------------------------------------------------------------------

    private static string Header() =>
        $"""<header id="{TaskId}" title="{X($"{TaskId} {TaskTitle}")}" arguments="{X(Args("task", TaskId))}"/>""";

    private static string Logo(string status) =>
        $"""<image placement="appLogoOverride" hint-crop="circle" src="{X(new Uri(LogoPath(status)).AbsoluteUri)}"/>""";

    private static string Launch(string action, string item) => _protocolMode
        ? $"""activationType="protocol" launch="{X(Url(action, item))}" """
        : $"""launch="{X(Args(action, item))}" """;

    private static string Act(string action, string item) => _protocolMode
        ? $"""activationType="protocol" arguments="{X(Url(action, item))}" """
        : $"""activationType="foreground" arguments="{X(Args(action, item))}" """;

    private static string Args(string action, string item) => $"action={action}&item={item}";
    private static string Url(string action, string item) => $"{Protocol}:{action}?item={item}";
    private static string X(string s) => SecurityElement.Escape(s);

    private static void Show(string tag, string xml, NotificationData? data = null)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var toast = new ToastNotification(doc) { Tag = tag, Group = TaskId };
        if (data is not null) toast.Data = data;
        toast.Activated += (_, e) => OnActivated(tag, e);
        toast.Dismissed += (_, e) => Log($"dismissed tag={tag} reason={e.Reason}");
        toast.Failed += (_, e) => Log($"failed tag={tag} error=0x{e.ErrorCode.HResult:X8}");
        Shown.Add(toast);
        var sw = Stopwatch.StartNew();
        ToastNotificationManager.CreateToastNotifier(Aumid).Show(toast);
        Console.WriteLine($"  shown tag={tag} in {sw.ElapsedMilliseconds} ms");
    }

    private static void OnActivated(string tag, object e)
    {
        if (e is not ToastActivatedEventArgs a)
        {
            Log($"activated tag={tag} with {e.GetType().Name}");
            return;
        }
        var inputs = string.Join(", ", a.UserInput.Select(kv => $"{kv.Key}={kv.Value}"));
        Log($"activated tag={tag} args=\"{a.Arguments}\" inputs=[{inputs}]");
    }

    private static int ProtocolLaunch(string url)
    {
        Log($"protocol launch pid={Environment.ProcessId} url={url}");
        Console.WriteLine("Launched by a notification after the spike quit (or in protocol mode). Enter to close.");
        Console.ReadLine();
        return 0;
    }

    private static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Console.WriteLine($"  {line}");
        lock (LogPath) File.AppendAllText(LogPath, line + Environment.NewLine);
    }

    // --- Registration --------------------------------------------------------------------------------------------

    // An unpackaged app gets a notification identity from this key alone; no Start menu shortcut is needed.
    private static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}");
        key.SetValue("DisplayName", "Ochre (spike)");
        key.SetValue("IconUri", LogoPath("app"));
        key.SetValue("IconBackgroundColor", "FFCC7722");
    }

    private static void RegisterProtocol()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Protocol}");
        key.SetValue("", "URL:Ochre spike");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{Environment.ProcessPath}\" \"%1\"");
        Console.WriteLine($"  registered {Protocol}: -> {Environment.ProcessPath}");
    }

    private static void Unregister()
    {
        ToastNotificationManager.History.Clear(Aumid);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{Aumid}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Protocol}", throwOnMissingSubKey: false);
        Console.WriteLine("Removed the AUMID and protocol keys and cleared the spike's notifications.");
    }

    private static string LogoPath(string status) => Path.Combine(DataDir, $"logo-{status}.png");

    private static void WriteLogos()
    {
        (string Status, Color Color)[] logos =
        [
            ("app", Color.FromArgb(0xcc, 0x77, 0x22)),
            ("waiting", Color.FromArgb(0xe0, 0xa0, 0x20)),
            ("busy", Color.FromArgb(0x3b, 0x82, 0xf6)),
            ("finished", Color.FromArgb(0x22, 0xa0, 0x55)),
            ("failed", Color.FromArgb(0xd0, 0x3a, 0x3a)),
            ("observed", Color.FromArgb(0x88, 0x88, 0x88)),
        ];
        foreach (var (status, color) in logos)
        {
            using var bitmap = new Bitmap(96, 96);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var fill = new SolidBrush(color);
                g.FillEllipse(fill, 4, 4, 88, 88);
            }
            bitmap.Save(LogoPath(status), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
