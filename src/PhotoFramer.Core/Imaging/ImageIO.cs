using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace PhotoFramer.Core.Imaging;

/// <summary>Čtení (JPEG/TIFF/PNG) a zápis TIFF přes WPF kodeky, JPEG zápis přes SkiaSharp.</summary>
public static class ImageIO
{
    public static SKBitmap Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return FromBitmapSource(decoder.Frames[0]);
    }

    /// <summary>Převede libovolný WPF obrázek (soubor, schránka) na neprůhledný SKBitmap.</summary>
    public static SKBitmap FromBitmapSource(BitmapSource frame)
    {
        if (frame.Format != PixelFormats.Bgra32)
            frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var bitmap = new SKBitmap(new SKImageInfo(frame.PixelWidth, frame.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        frame.CopyPixels(System.Windows.Int32Rect.Empty, bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
        bitmap.SetImmutable();
        return Opaque(bitmap);
    }

    /// <summary>Uloží 8bitový sRGB TIFF.</summary>
    public static void SaveTiff(SKBitmap bitmap, string path)
    {
        var source = ToBitmapSource(bitmap);
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.None };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void SaveJpeg(SKBitmap bitmap, string path, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    /// <summary>Uloží PNG; compression 0 (nejrychlejší) až 9 (nejmenší soubor).</summary>
    public static void SavePng(SKBitmap bitmap, string path, int compression)
    {
        using var pixmap = bitmap.PeekPixels();
        using var stream = File.Create(path);
        var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, Math.Clamp(compression, 0, 9));
        if (!pixmap.Encode(stream, options))
            throw new IOException("PNG se nepodařilo zakódovat.");
    }

    public static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        var source = BitmapSource.Create(
            bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null,
            bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
        source.Freeze();
        return source;
    }

    /// <summary>Vstup může mít alfa kanál; výstup je vždy neprůhledný (bílé pozadí pod průhledností).</summary>
    private static SKBitmap Opaque(SKBitmap src)
    {
        var result = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(src, 0, 0);
        src.Dispose();
        return result;
    }

    /// <summary>Zmenší (nebo zvětší) na čtverec/obdélník daných rozměrů kvalitním resamplingem.</summary>
    public static SKBitmap Resize(SKBitmap src, int width, int height)
    {
        var current = src;
        var disposeCurrent = false;
        // velké zmenšení: nejdřív mipmapy/lineárně na ~2× cíl, pak Mitchell
        while (current.Width > width * 2 && current.Height > height * 2)
        {
            var step = new SKBitmap(new SKImageInfo(Math.Max(width * 2, current.Width / 2), Math.Max(height * 2, current.Height / 2), SKColorType.Bgra8888, SKAlphaType.Opaque));
            current.ScalePixels(step, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (disposeCurrent) current.Dispose();
            current = step;
            disposeCurrent = true;
        }

        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        current.ScalePixels(result, new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (disposeCurrent) current.Dispose();
        return result;
    }

    /// <summary>Výřez zadané oblasti (oblast se ořízne na hranice obrázku).</summary>
    public static SKBitmap Crop(SKBitmap src, SKRectI area)
    {
        area.Intersect(new SKRectI(0, 0, src.Width, src.Height));
        var result = new SKBitmap(new SKImageInfo(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(src, area, new SKRect(0, 0, area.Width, area.Height));
        return result;
    }

    /// <summary>Ořez na čtverec ze středu.</summary>
    public static SKBitmap CropCenterSquare(SKBitmap src)
    {
        var side = Math.Min(src.Width, src.Height);
        var area = new SKRectI((src.Width - side) / 2, (src.Height - side) / 2, (src.Width - side) / 2 + side, (src.Height - side) / 2 + side);
        var result = new SKBitmap(new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(src, area, new SKRect(0, 0, side, side));
        return result;
    }
}
