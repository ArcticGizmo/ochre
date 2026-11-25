using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows;

/// <summary>
/// <see cref="OpenInNewWindow"/> resolves the default browser's executable from the per-user URL association and
/// launches it with the browser's "new window" switch, so the window opens on the active virtual desktop rather
/// than the OS activating an existing window on whatever desktop it lives on.
/// </summary>
public sealed class UrlOpener : IUrlOpener
{
    public void Open(string url)
    {
        if (OpenTargets.WebUrl(url) is not { } safe) return;
        try
        {
            Process.Start(new ProcessStartInfo(safe) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // No default handler, or blocked.
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void OpenInNewWindow(string url)
    {
        if (OpenTargets.WebUrl(url) is not { } safe) return;
        if (!OpenTargets.IsHttp(safe))
        {
            // mailto: belongs to the mail client.
            Open(safe);
            return;
        }

        if (ResolveDefaultBrowser() is { } browser)
        {
            try
            {
                var psi = new ProcessStartInfo(browser.ExePath) { UseShellExecute = false };
                psi.ArgumentList.Add(browser.Gecko ? "-new-window" : "--new-window");
                // Chromium's "--" ends switch parsing, so even a URL starting with "-" is read as a URL. Gecko has no
                // terminator and relies on WebUrl guaranteeing a scheme-led URL.
                if (!browser.Gecko) psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(safe);
                Process.Start(psi)?.Dispose();
                return;
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        Open(safe);
    }

    private readonly record struct Browser(string ExePath, bool Gecko);

    /// <summary>
    /// The default https handler: the user's UrlAssociations choice gives a ProgId whose <c>shell\open\command</c>
    /// holds the launch command. Null if anything is missing, so the caller falls back to a shell open.
    /// </summary>
    private static Browser? ResolveDefaultBrowser()
    {
        try
        {
            string? progId;
            using (var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                progId = choice?.GetValue("ProgId") as string;
            if (string.IsNullOrWhiteSpace(progId)) return null;

            string? command;
            using (var cmd = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command"))
                command = cmd?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(command)) return null;

            var exe = ExtractExePath(command);
            if (exe.Length == 0 || !File.Exists(exe)) return null;

            var name = Path.GetFileName(exe);
            var gecko = progId.Contains("firefox", StringComparison.OrdinalIgnoreCase)
                || progId.Contains("mozilla", StringComparison.OrdinalIgnoreCase)
                || name.Contains("firefox", StringComparison.OrdinalIgnoreCase);
            return new Browser(exe, gecko);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ExtractExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : "";
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}
