using System.Runtime.InteropServices;
using Microsoft.Win32;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Install;

/// <summary>
/// What an installed Ochre registers for the user, none of which needs elevation: its folder on the per-user PATH (so
/// <c>ochre</c> works in any terminal), and optionally a sign-in start. The installer's hooks add and remove them.
/// Adapted from Perch's PathInstaller and LoginItem (perch 9275e0b, src/Perch.Platform.Windows).
/// </summary>
public static partial class InstallRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Ochre";
    private const string PathValue = "Path";

    private static string InstallDir => AppContext.BaseDirectory.TrimEnd('\\', '/');

    public static void AddToPath() => EditPath(current => PathList.WithEntry(current, InstallDir, Environment.ExpandEnvironmentVariables));

    public static void RemoveFromPath() => EditPath(current => PathList.WithoutEntry(current, InstallDir, Environment.ExpandEnvironmentVariables));

    /// <summary>Removes the sign-in start and the notification identity, so an uninstall leaves nothing pointing at a
    /// deleted exe.</summary>
    public static void RemoveUserEntries(string notificationId)
    {
        new SignInStart().Set(false);
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{notificationId}", throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Left behind, it only names an app that's gone.
        }
    }

    // HKCU\Environment\Path is read unexpanded and written back with the kind it had (REG_EXPAND_SZ for a new value,
    // as Windows itself makes it): Environment.SetEnvironmentVariable would expand every %VAR% entry for good.
    private static void EditPath(Func<string, string?> edit)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey("Environment", writable: true);
            var current = key.GetValue(PathValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (edit(current ?? "") is not { } updated) return;
            var kind = current is not null && key.GetValueKind(PathValue) == RegistryValueKind.String ? RegistryValueKind.String : RegistryValueKind.ExpandString;
            key.SetValue(PathValue, updated, kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Without it, `ochre` just isn't on PATH; the app itself is unaffected.
            return;
        }
        // New terminals pick up the change without signing out.
        SendMessageTimeout(0xffff, 0x1A, 0, "Environment", 0x2, 5000, out _);
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint hWnd, uint msg, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);

    /// <summary>A value under the per-user run key that starts this exe in the tray.</summary>
    public sealed class SignInStart : ISignInStart
    {
        public bool IsOn
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(RunValue) is not null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    return false;
                }
            }
        }

        public void Set(bool on)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (on && Environment.ProcessPath is { Length: > 0 } exe) key.SetValue(RunValue, $"\"{exe}\" --tray", RegistryValueKind.String);
                else key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Best-effort: Ochre just won't start at sign-in.
            }
        }
    }
}
