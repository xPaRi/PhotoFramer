using PhotoFramer.Core.Models;
using SkiaSharp;

namespace PhotoFramer.Core.Imaging;

/// <summary>Rozložení výstupu: velikost, umístění fotky a viditelný obrys podpisu (v pixelech výstupu).</summary>
public readonly record struct FrameLayout(int OuterWidth, int OuterHeight, SKRectI Photo, SKRect? Signature);

public static class FrameRenderer
{
    public static FrameLayout GetLayout(int photoWidth, int photoHeight, Profile profile)
    {
        var border = Math.Max(0, profile.BorderWidth);
        var outerW = photoWidth + 2 * border;
        var outerH = photoHeight + 2 * border;
        var photo = new SKRectI(border, border, border + photoWidth, border + photoHeight);

        SKRect? signature = null;
        var sig = profile.Signature;
        if (sig.Enabled && !string.IsNullOrEmpty(sig.Text))
        {
            using var font = CreateFont(sig);
            font.MeasureText(sig.Text, out var ink);
            var right = outerW - sig.MarginRight;
            var bottom = outerH - sig.MarginBottom;
            signature = SKRect.Create(right - ink.Width, bottom - ink.Height, ink.Width, ink.Height);
        }
        return new FrameLayout(outerW, outerH, photo, signature);
    }

    public static SKBitmap Render(SKBitmap photo, Profile profile)
    {
        var layout = GetLayout(photo.Width, photo.Height, profile);
        var result = new SKBitmap(new SKImageInfo(layout.OuterWidth, layout.OuterHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);

        canvas.Clear(ParseColor(profile.BorderColor, SKColors.White));
        canvas.DrawBitmap(photo, layout.Photo.Left, layout.Photo.Top);

        foreach (var line in profile.Lines)
        {
            if (line.Thickness <= 0)
                continue;
            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            var inner = SKRect.Create(layout.Photo.Left, layout.Photo.Top, layout.Photo.Width, layout.Photo.Height);
            inner.Inflate(line.Offset, line.Offset);
            var outer = inner;
            outer.Inflate(line.Thickness, line.Thickness);
            path.AddRect(outer);
            path.AddRect(inner);
            using var paint = new SKPaint { Color = ParseColor(line.Color, SKColors.Blue), IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawPath(path, paint);
        }

        var sig = profile.Signature;
        if (layout.Signature is { } box)
        {
            using var font = CreateFont(sig);
            font.MeasureText(sig.Text, out var ink);
            using var paint = new SKPaint { Color = ParseColor(sig.Color, SKColors.Blue), IsAntialias = true };
            // baseline tak, aby viditelný obrys seděl do `box`
            canvas.DrawText(sig.Text, box.Left - ink.Left, box.Top - ink.Top, SKTextAlign.Left, font, paint);
        }
        return result;
    }

    public static SKColor ParseColor(string? hex, SKColor fallback) =>
        SKColor.TryParse(hex, out var c) ? c : fallback;

    private static SKFont CreateFont(SignatureSettings sig)
    {
        var typeface = SKTypeface.FromFamilyName(
            sig.FontFamily,
            sig.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            sig.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        return new SKFont(typeface, Math.Max(1, sig.FontSize))
        {
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
        };
    }
}
