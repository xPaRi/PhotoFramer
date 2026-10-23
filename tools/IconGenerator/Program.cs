// Vygeneruje ikonu aplikace (víceúrovňové .ico) pomocí SkiaSharp.
// Použití: dotnet run --project tools\IconGenerator -- <cílový.ico>
using SkiaSharp;

var target = args.Length > 0 ? args[0] : "PhotoFramer.ico";
int[] sizes = [16, 24, 32, 48, 64, 128, 256];

var pngs = sizes.Select(s => (Size: s, Data: RenderPng(s))).ToList();
WriteIco(target, pngs);
Console.WriteLine($"Ikona uložena: {Path.GetFullPath(target)}");

// Náhled největší velikosti vedle .ico
File.WriteAllBytes(Path.ChangeExtension(target, ".png"), pngs[^1].Data);

static byte[] RenderPng(int size)
{
    using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    Draw(canvas, size);
    using var image = SKImage.FromBitmap(bitmap);
    return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
}

static void Draw(SKCanvas c, int size)
{
    float s = size;
    var blue = SKColor.Parse("#0080FF");
    bool detailed = size >= 48;

    // bílý rámeček (pruh) se stínem
    var frame = new SKRect(s * 0.06f, s * 0.05f, s * 0.94f, s * 0.93f);
    using (var shadow = new SKPaint
    {
        Color = new SKColor(0, 0, 0, 90),
        IsAntialias = true,
        ImageFilter = SKImageFilter.CreateBlur(s * 0.025f, s * 0.025f),
    })
        c.DrawRoundRect(new SKRect(frame.Left, frame.Top + s * 0.02f, frame.Right, frame.Bottom + s * 0.02f), s * 0.07f, s * 0.07f, shadow);
    using (var white = new SKPaint { Color = SKColors.White, IsAntialias = true })
        c.DrawRoundRect(frame, s * 0.07f, s * 0.07f, white);

    // fotka
    var photo = new SKRect(s * 0.16f, s * 0.15f, s * 0.84f, s * 0.74f);
    if (!detailed)
        photo = new SKRect(s * 0.17f, s * 0.17f, s * 0.83f, s * 0.83f);

    c.Save();
    c.ClipRect(photo);
    using (var sky = new SKPaint
    {
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, photo.Top), new SKPoint(0, photo.Bottom),
            [SKColor.Parse("#3F8CE0"), SKColor.Parse("#BFE3FF")], SKShaderTileMode.Clamp),
    })
        c.DrawRect(photo, sky);

    // slunce
    using (var sun = new SKPaint { Color = SKColor.Parse("#FFD24A"), IsAntialias = true })
        c.DrawCircle(photo.Left + photo.Width * 0.72f, photo.Top + photo.Height * 0.30f, photo.Width * 0.11f, sun);

    // hory
    using (var far = new SKPaint { Color = SKColor.Parse("#4D7FB0"), IsAntialias = true })
    using (var farPath = new SKPath())
    {
        farPath.MoveTo(photo.Left + photo.Width * 0.30f, photo.Bottom);
        farPath.LineTo(photo.Left + photo.Width * 0.62f, photo.Top + photo.Height * 0.40f);
        farPath.LineTo(photo.Right + photo.Width * 0.05f, photo.Bottom);
        farPath.Close();
        c.DrawPath(farPath, far);
    }
    using (var near = new SKPaint { Color = SKColor.Parse("#1F4468"), IsAntialias = true })
    using (var nearPath = new SKPath())
    {
        nearPath.MoveTo(photo.Left - photo.Width * 0.05f, photo.Bottom);
        nearPath.LineTo(photo.Left + photo.Width * 0.30f, photo.Top + photo.Height * 0.46f);
        nearPath.LineTo(photo.Left + photo.Width * 0.62f, photo.Bottom);
        nearPath.Close();
        c.DrawPath(nearPath, near);
    }
    c.Restore();

    // tenká modrá linka kolem fotky (jako v hotových fotografiích)
    float gap = s * 0.025f;
    using (var line = new SKPaint
    {
        Color = blue, IsAntialias = true, Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1f, s * 0.012f),
    })
    {
        var outer = photo;
        outer.Inflate(gap, gap);
        c.DrawRect(outer, line);
    }

    if (!detailed)
        return;

    // podpis: modrý ručně psaný tah v pravém dolním rohu pruhu
    using var pen = new SKPaint
    {
        Color = blue, IsAntialias = true, Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1.5f, s * 0.022f), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };
    using var sig = new SKPath();
    float x0 = s * 0.50f, y0 = s * 0.85f;
    sig.MoveTo(x0, y0);
    sig.CubicTo(x0 + s * 0.05f, y0 - s * 0.10f, x0 + s * 0.09f, y0 + s * 0.02f, x0 + s * 0.13f, y0 - s * 0.05f);
    sig.CubicTo(x0 + s * 0.16f, y0 - s * 0.10f, x0 + s * 0.18f, y0 + s * 0.01f, x0 + s * 0.22f, y0 - s * 0.04f);
    sig.CubicTo(x0 + s * 0.25f, y0 - s * 0.08f, x0 + s * 0.28f, y0 + s * 0.01f, x0 + s * 0.33f, y0 - s * 0.02f);
    c.DrawPath(sig, pen);
}

static void WriteIco(string path, List<(int Size, byte[] Data)> images)
{
    using var stream = File.Create(path);
    using var w = new BinaryWriter(stream);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)images.Count);

    var offset = 6 + 16 * images.Count;
    foreach (var (size, data) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(data.Length);
        w.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in images)
        w.Write(data);
}
