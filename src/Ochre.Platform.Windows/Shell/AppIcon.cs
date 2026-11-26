using System.Runtime.InteropServices;

namespace Ochre.Platform.Windows.Shell;

/// <summary>
/// Ochre's icon, from the frames tools/Ochre.IconGen renders out of ochre.svg. Both icons live for the whole process.
/// </summary>
internal static partial class AppIcon
{
    private const int SmCxSmIcon = 49;

    /// <summary>
    /// The notification area shows a small icon at the system DPI. Handing it the frame drawn for exactly that size
    /// keeps Windows from shrinking a larger one, which is what blurs tray icons.
    /// </summary>
    public static Icon Tray()
    {
        var size = GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForSystem());
        using var stream = Open("ochre.ico");
        return new Icon(stream, size, size);
    }

    /// <summary>Every frame, so each window takes the sizes it needs for its own monitor's DPI.</summary>
    public static Icon Window()
    {
        using var stream = Open("ochre.ico");
        return new Icon(stream);
    }

    /// <summary>The logo as a 256 px PNG, for places that take an image file rather than an icon.</summary>
    public static void WriteLogo(string path)
    {
        using var stream = Open("ochre-256.png");
        using var file = File.Create(path);
        stream.CopyTo(file);
    }

    private static Stream Open(string name) =>
        typeof(AppIcon).Assembly.GetManifestResourceStream($"Ochre.Assets.{name}") ?? throw new InvalidOperationException($"missing resource {name}");

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
