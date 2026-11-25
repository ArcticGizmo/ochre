using System.Drawing.Drawing2D;

namespace Ochre.Platform.Windows.Shell;

/// <summary>A placeholder ochre disc, drawn at runtime until there's a real icon asset.</summary>
internal static class AppIcon
{
    /// <summary>
    /// The icon lives for the whole process, so its GDI handle is deliberately never destroyed; Windows reclaims it
    /// at exit.
    /// </summary>
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(0xcc, 0x77, 0x22));
            g.FillEllipse(fill, 2, 2, 28, 28);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
