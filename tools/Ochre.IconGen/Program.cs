using SkiaSharp;
using Svg.Skia;

// Rasterises ochre.svg, the one source of the logo, into:
//
//   src/Ochre.Platform.Windows/Assets/ochre.ico      the tray, window and .exe icon, one frame per size Windows asks for
//   src/Ochre.Platform.Windows/Assets/ochre-256.png  the notification identity's logo
//   docs/assets/ochre-icon.png                       512 px, the README header
//
// Every frame is rendered from the vector at its own size, so Windows never scales a bitmap to fit the tray.
//
//   dotnet run --project tools/Ochre.IconGen [-- --preview captures/<name>.png]

var root = FindRepoRoot(AppContext.BaseDirectory);
var svgPath = Path.Combine(root, "ochre.svg");
if (!File.Exists(svgPath))
{
    Console.Error.WriteLine($"Source SVG not found: {svgPath}");
    return 1;
}

using var svg = new SKSvg();
var picture = svg.Load(svgPath) ?? throw new InvalidOperationException($"Could not read {svgPath}");
var fit = Fit.Of(picture);
Console.WriteLine($"Source: {svgPath}");
Console.WriteLine($"Content {fit.Content.Width:0.#} x {fit.Content.Height:0.#} of a {picture.CullRect.Width:0.#} x {picture.CullRect.Height:0.#} view box");

// The tray and small window icons use SM_CXSMICON at the system DPI: 16 at 100 %, then 20, 24, 28, 32, 36 and 40
// up to 250 %. 48 to 96 cover large icons and Alt+Tab at the same scales; 128 and 256 cover Explorer's big views.
int[] icoSizes = [16, 20, 24, 28, 32, 36, 40, 48, 64, 72, 96, 128, 256];

var assets = Path.Combine(root, "src", "Ochre.Platform.Windows", "Assets");
var docs = Path.Combine(root, "docs", "assets");
Directory.CreateDirectory(assets);
Directory.CreateDirectory(docs);

WriteIco(picture, fit, Path.Combine(assets, "ochre.ico"), icoSizes);
WritePng(picture, fit, Path.Combine(assets, "ochre-256.png"), 256);
WritePng(picture, fit, Path.Combine(docs, "ochre-icon.png"), 512);

var preview = args.SkipWhile(a => a != "--preview").Skip(1).FirstOrDefault();
if (preview is not null) WritePreview(picture, fit, Path.GetFullPath(preview, root), icoSizes);

Console.WriteLine("Done.");
return 0;

static SKImage Render(SKPicture picture, Fit fit, int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException($"Could not make a {size} px surface");
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    var scale = size / fit.Side;
    canvas.Scale(scale);
    canvas.Translate(-fit.X, -fit.Y);
    canvas.DrawPicture(picture);
    canvas.Flush();
    return surface.Snapshot();
}

static void WritePng(SKPicture picture, Fit fit, string path, int size)
{
    using var image = Render(picture, fit, size);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
    Console.WriteLine($"  {Rel(path)}  {size} px");
}

// PNG frames are only for 256 px: small frames as 32-bit DIBs are what every Windows icon path reads the same way,
// including the older ones behind the notification area.
static void WriteIco(SKPicture picture, Fit fit, string path, int[] sizes)
{
    var frames = sizes.Select(size =>
    {
        using var image = Render(picture, fit, size);
        if (size >= 256)
        {
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        return Dib(image, size);
    }).ToList();

    using var file = File.Create(path);
    using var w = new BinaryWriter(file);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)sizes.Length);
    var offset = 6 + sizes.Length * 16;
    for (var i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(frames[i].Length);
        w.Write(offset);
        offset += frames[i].Length;
    }
    foreach (var frame in frames) w.Write(frame);
    Console.WriteLine($"  {Rel(path)}  {string.Join(", ", sizes)}");
}

// A BITMAPINFOHEADER, then straight (not premultiplied) BGRA rows bottom-up, then the 1-bit mask (set where the
// pixel is fully transparent), each mask row padded to 4 bytes. The header's height counts both halves.
static byte[] Dib(SKImage image, int size)
{
    using var pixels = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul));
    if (!image.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0))
        throw new InvalidOperationException($"Could not read the {size} px frame");
    var bgra = pixels.Bytes;
    var maskStride = (size + 31) / 32 * 4;

    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(40);
    w.Write(size);
    w.Write(size * 2);
    w.Write((ushort)1);
    w.Write((ushort)32);
    w.Write(0);
    w.Write(size * size * 4 + maskStride * size);
    w.Write(0);
    w.Write(0);
    w.Write(0);
    w.Write(0);
    for (var y = size - 1; y >= 0; y--) w.Write(bgra, y * pixels.RowBytes, size * 4);
    for (var y = size - 1; y >= 0; y--)
    {
        var row = new byte[maskStride];
        for (var x = 0; x < size; x++)
            if (bgra[y * pixels.RowBytes + x * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
        w.Write(row);
    }
    return ms.ToArray();
}

// Every icon frame side by side, each also blown up eight times with no smoothing, on a light and a dark strip,
// so the small frames can be judged pixel by pixel.
static void WritePreview(SKPicture picture, Fit fit, string path, int[] sizes)
{
    const int zoom = 8, gap = 12;
    var small = sizes.Where(s => s <= 48).ToArray();
    var width = gap + small.Sum(s => s * zoom + gap);
    var band = 48 * zoom + gap * 2;
    var info = new SKImageInfo(width, band * 2, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    var c = surface.Canvas;
    using var light = new SKPaint { Color = new SKColor(0xf3, 0xf3, 0xf3) };
    using var dark = new SKPaint { Color = new SKColor(0x20, 0x20, 0x20) };
    c.DrawRect(0, 0, width, band, light);
    c.DrawRect(0, band, width, band, dark);
    var x = gap;
    foreach (var s in small)
    {
        using var image = Render(picture, fit, s);
        foreach (var top in new[] { gap, band + gap })
            c.DrawImage(image, SKRect.Create(x, top, s * zoom, s * zoom), new SKSamplingOptions(SKFilterMode.Nearest));
        x += s * zoom + gap;
    }
    using var shot = surface.Snapshot();
    using var data = shot.Encode(SKEncodedImageFormat.Png, 100);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, data.ToArray());
    Console.WriteLine($"  preview {path}");
}

static string FindRepoRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "ochre.svg"))) return dir.FullName;
    throw new InvalidOperationException("ochre.svg not found above " + start);
}

static string Rel(string path) => Path.GetFileName(path);

/// <summary>
/// The square, in SVG units, that frames the drawn content with a sliver of margin. The artwork doesn't fill its view
/// box, and every pixel counts at 16 px, so the icon is cropped to what's drawn rather than to the view box.
/// </summary>
internal readonly record struct Fit(SKRect Content, float X, float Y, float Side)
{
    private const float Pad = 0.015f;
    private const int Probe = 1024;

    public static Fit Of(SKPicture picture)
    {
        // Measured from a large render's alpha, which is exact whatever the SVG's paths and transforms are.
        var view = picture.CullRect;
        var unit = Probe / Math.Max(view.Width, view.Height);
        using var surface = SKSurface.Create(new SKImageInfo(Probe, Probe, SKColorType.Bgra8888, SKAlphaType.Premul))!;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale(unit);
        surface.Canvas.Translate(-view.Left, -view.Top);
        surface.Canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        int minX = Probe, minY = Probe, maxX = -1, maxY = -1;
        for (var y = 0; y < Probe; y++)
            for (var x = 0; x < Probe; x++)
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
        if (maxX < 0) throw new InvalidOperationException("The SVG draws nothing");
        var content = new SKRect(view.Left + minX / unit, view.Top + minY / unit, view.Left + (maxX + 1) / unit, view.Top + (maxY + 1) / unit);
        var side = Math.Max(content.Width, content.Height) * (1 + Pad * 2);
        return new Fit(content, content.MidX - side / 2, content.MidY - side / 2, side);
    }
}
