using System.Drawing.Drawing2D;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Ochre.Core.Platform;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Ochre.Platform.Windows.Notifications;

/// <summary>
/// Windows toast notifications from an unpackaged app (docs/spike-notifications-results.md): an AppUserModelID
/// registered under HKCU is the app's identity, with no Start menu shortcut. Every button is handled in-process
/// (foreground activation), since typed text and choices only reach a running app; Ochre clears its notifications
/// when it quits and starts, so none outlives the items it names.
/// </summary>
public sealed class ToastNotifier : IDesktopNotifier
{
    private readonly string _aumid;
    private readonly string _logos;
    private readonly Action<string> _log;
    // A toast must stay referenced or its Activated event is lost to the GC; replaced by tag as notifications are.
    private readonly Dictionary<string, ToastNotification> _shown = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private bool _broken;

    /// <param name="aumid">The app's notification identity; one per profile so the dev build's never mix with the installed one's.</param>
    /// <param name="dataFolder">Where the status logos are drawn.</param>
    public ToastNotifier(string aumid, string displayName, string dataFolder, Action<string> log)
    {
        _aumid = aumid;
        _logos = dataFolder;
        _log = log;
        try
        {
            Directory.CreateDirectory(dataFolder);
            WriteLogos();
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{aumid}");
            key.SetValue("DisplayName", displayName);
            key.SetValue("IconUri", LogoPath("app"));
            key.SetValue("IconBackgroundColor", "FFCC7722");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            Fail("could not register for notifications", ex);
        }
    }

    public event Action<NotificationAction>? Activated;

    public void Show(DesktopNotification n)
    {
        if (_broken) return;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(Xml(n));
            var toast = new ToastNotification(doc) { Tag = n.Tag, Group = n.Group };
            toast.Activated += (_, e) => OnActivated(n.Tag, e);
            toast.Failed += (_, e) => _log($"notification {n.Tag} failed: 0x{e.ErrorCode.HResult:X8}");
            lock (_gate) _shown[n.Tag] = toast;
            ToastNotificationManager.CreateToastNotifier(_aumid).Show(toast);
        }
        catch (Exception ex)
        {
            // Notifications are a convenience; whatever Windows throws here must not reach the attention board.
            Fail($"could not show notification {n.Tag}", ex);
        }
    }

    public void Remove(string tag, string group)
    {
        lock (_gate) _shown.Remove(tag);
        if (_broken) return;
        try
        {
            ToastNotificationManager.History.Remove(tag, group, _aumid);
        }
        catch (Exception ex)
        {
            Fail($"could not remove notification {tag}", ex);
        }
    }

    public void Clear()
    {
        lock (_gate) _shown.Clear();
        if (_broken) return;
        try
        {
            ToastNotificationManager.History.Clear(_aumid);
        }
        catch (Exception ex)
        {
            Fail("could not clear notifications", ex);
        }
    }

    public void Dispose() => Clear();

    private void OnActivated(string tag, object args)
    {
        if (args is not ToastActivatedEventArgs a) return;
        var parsed = ParseArguments(a.Arguments);
        if (!parsed.TryGetValue("action", out var action)) return;
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in a.UserInput)
            if (value is string s) inputs[key] = s;
        try
        {
            Activated?.Invoke(new NotificationAction(tag, action, inputs));
        }
        catch (Exception ex)
        {
            _log($"handling a notification click failed: {ex.Message}");
        }
    }

    private void Fail(string what, Exception ex)
    {
        _log($"{what}: {ex.GetType().Name}: {ex.Message}");
        // A machine that refuses once (notifications blocked by policy) will keep refusing; stop trying and say so once.
        if (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) _broken = true;
    }

    // --- Content -------------------------------------------------------------------------------------------------

    private string Xml(DesktopNotification n)
    {
        var open = Args("open");
        var x = new StringBuilder();
        // Reminder keeps a blocked agent's notification on screen until it's dealt with.
        var scenario = n.Look == NotificationLook.NeedsYou ? " scenario=\"reminder\"" : "";
        x.Append($"<toast{scenario} useButtonStyle=\"true\" launch=\"{X(open)}\">");
        x.Append($"<header id=\"{X(n.Group)}\" title=\"{X(n.GroupTitle)}\" arguments=\"{X(open)}\"/>");
        x.Append("<visual><binding template=\"ToastGeneric\">");
        x.Append($"<text>{X(n.Title)}</text>");
        if (n.Body.Length > 0) x.Append($"<text>{X(n.Body)}</text>");
        if (n.Attribution is { Length: > 0 } attribution) x.Append($"<text placement=\"attribution\">{X(attribution)}</text>");
        x.Append($"<image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\"{X(new Uri(LogoPath(LogoOf(n.Look))).AbsoluteUri)}\"/>");
        x.Append("</binding></visual><actions>");
        foreach (var input in n.Inputs)
        {
            if (input.Choices is { Count: > 0 } choices)
            {
                x.Append($"<input id=\"{X(input.Id)}\" type=\"selection\" defaultInput=\"{X(choices[0].Key)}\">");
                foreach (var (id, label) in choices) x.Append($"<selection id=\"{X(id)}\" content=\"{X(label)}\"/>");
                x.Append("</input>");
            }
            else
            {
                x.Append($"<input id=\"{X(input.Id)}\" type=\"text\" placeHolderContent=\"{X(input.Placeholder)}\"/>");
            }
        }
        foreach (var b in n.Buttons)
        {
            var style = b.Style switch
            {
                NotificationButtonStyle.Positive => " hint-buttonStyle=\"Success\"",
                NotificationButtonStyle.Negative => " hint-buttonStyle=\"Critical\"",
                _ => "",
            };
            var beside = b.InputId is { } inputId ? $" hint-inputId=\"{X(inputId)}\"" : "";
            x.Append($"<action content=\"{X(b.Label)}\" activationType=\"foreground\" arguments=\"{X(Args(b.Action))}\"{style}{beside}/>");
        }
        x.Append("</actions></toast>");
        return x.ToString();
    }

    private static string Args(string action) => "action=" + Uri.EscapeDataString(action);

    private static Dictionary<string, string> ParseArguments(string arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in arguments.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0) result[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return result;
    }

    private static string X(string s) => SecurityElement.Escape(s);

    private static string LogoOf(NotificationLook look) => look switch
    {
        NotificationLook.NeedsYou => "needs",
        NotificationLook.Finished => "finished",
        NotificationLook.Failed => "failed",
        _ => "waiting",
    };

    private string LogoPath(string name) => Path.Combine(_logos, $"logo-{name}.png");

    // Ochre's status colours as round logos, drawn once at start until there are real icon assets.
    private void WriteLogos()
    {
        (string Name, Color Color)[] logos =
        [
            ("app", Color.FromArgb(0xcc, 0x77, 0x22)),
            ("needs", Color.FromArgb(0xd9, 0xb4, 0x4a)),
            ("finished", Color.FromArgb(0x86, 0xb5, 0x72)),
            ("failed", Color.FromArgb(0xe0, 0x6c, 0x5a)),
            ("waiting", Color.FromArgb(0xa3, 0x9d, 0x96)),
        ];
        foreach (var (name, color) in logos)
        {
            using var bitmap = new Bitmap(96, 96);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var fill = new SolidBrush(color);
                g.FillEllipse(fill, 4, 4, 88, 88);
            }
            bitmap.Save(LogoPath(name), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
