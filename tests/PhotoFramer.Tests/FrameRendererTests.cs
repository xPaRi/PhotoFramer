using PhotoFramer.Core.Imaging;
using PhotoFramer.Core.Models;
using SkiaSharp;

namespace PhotoFramer.Tests;

public class FrameRendererTests
{
    private static SKBitmap Photo(int size, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }

    private static Profile DefaultProfile() => new();

    [Theory]
    [InlineData(900, 17)]
    [InlineData(900, 40)]
    [InlineData(300, 0)]
    public void OutputSize_IsPhotoPlusTwiceBorder(int photo, int border)
    {
        var profile = DefaultProfile();
        profile.BorderWidth = border;
        profile.Lines.Clear();

        using var src = Photo(photo, SKColors.Gray);
        using var result = FrameRenderer.Render(src, profile);

        Assert.Equal(photo + 2 * border, result.Width);
        Assert.Equal(photo + 2 * border, result.Height);
    }

    [Fact]
    public void Photo_IsPlacedInsideBorder_AndBorderUsesConfiguredColor()
    {
        var profile = DefaultProfile();
        profile.BorderWidth = 20;
        profile.BorderColor = "#FFFFFF";
        profile.Lines.Clear();
        profile.Signature.Enabled = false;

        using var src = Photo(100, SKColors.Red);
        using var result = FrameRenderer.Render(src, profile);

        Assert.Equal(SKColors.White, result.GetPixel(0, 0));
        Assert.Equal(SKColors.White, result.GetPixel(19, 60));
        Assert.Equal(SKColors.Red, result.GetPixel(20, 20));
        Assert.Equal(SKColors.Red, result.GetPixel(119, 119));
        Assert.Equal(SKColors.White, result.GetPixel(120, 120));
    }

    [Fact]
    public void Lines_AreDrawnAtOffsetFromPhotoEdge_WithThickness()
    {
        var profile = DefaultProfile();
        profile.BorderWidth = 17;
        profile.Signature.Enabled = false;
        profile.Lines.Clear();
        profile.Lines.Add(new FrameLine { Offset = 2, Thickness = 2, Color = "#3474B3" });
        var blue = SKColor.Parse("#3474B3");

        using var src = Photo(100, SKColors.Black);
        using var result = FrameRenderer.Render(src, profile);

        // fotka začíná na x = 17; linka leží na x = 13..14 (vzdálenost 2 px, tloušťka 2 px)
        Assert.Equal(SKColors.White, result.GetPixel(12, 60));
        Assert.Equal(blue, result.GetPixel(13, 60));
        Assert.Equal(blue, result.GetPixel(14, 60));
        Assert.Equal(SKColors.White, result.GetPixel(15, 60));
        Assert.Equal(SKColors.White, result.GetPixel(16, 60));
        Assert.Equal(SKColors.Black, result.GetPixel(17, 60));
        // spodní strana: fotka končí na y = 116, linka y = 119..120
        Assert.Equal(blue, result.GetPixel(60, 119));
        Assert.Equal(blue, result.GetPixel(60, 120));
        Assert.Equal(SKColors.White, result.GetPixel(60, 121));
    }

    [Fact]
    public void MultipleLines_EachHasOwnOffsetAndThickness()
    {
        var profile = DefaultProfile();
        profile.BorderWidth = 30;
        profile.Signature.Enabled = false;
        profile.Lines.Clear();
        profile.Lines.Add(new FrameLine { Offset = 1, Thickness = 1, Color = "#FF0000" });
        profile.Lines.Add(new FrameLine { Offset = 10, Thickness = 4, Color = "#00FF00" });

        using var src = Photo(50, SKColors.Black);
        using var result = FrameRenderer.Render(src, profile);

        Assert.Equal(SKColors.Red, result.GetPixel(28, 50));   // 30 - 1 - 1
        Assert.Equal(SKColors.Lime, result.GetPixel(16, 50));  // 30 - 10 - 4 .. 30 - 10 - 1
        Assert.Equal(SKColors.Lime, result.GetPixel(19, 50));
        Assert.Equal(SKColors.White, result.GetPixel(20, 50));
    }

    [Fact]
    public void Signature_IsInBottomRightOfBorder_WithConfiguredMargins()
    {
        var profile = DefaultProfile();
        profile.BorderWidth = 40;
        profile.Lines.Clear();
        profile.Signature.MarginRight = 15;
        profile.Signature.MarginBottom = 4;

        using var src = Photo(300, SKColors.Black);
        var layout = FrameRenderer.GetLayout(src.Width, src.Height, profile);

        var box = layout.Signature!.Value;
        Assert.Equal(layout.OuterWidth - 15, box.Right, 0.01);
        Assert.Equal(layout.OuterHeight - 4, box.Bottom, 0.01);
        Assert.True(box.Top >= layout.Photo.Bottom, "podpis musí ležet v bílém pruhu pod fotkou");
        Assert.True(box.Width > 0 && box.Height > 0);
    }

    [Fact]
    public void Signature_IsActuallyDrawnWithinItsBox_AndNowhereElseInTheStrip()
    {
        var profile = DefaultProfile();
        profile.BorderWidth = 40;
        profile.Lines.Clear();
        profile.Signature.Text = "PaRi";
        profile.Signature.FontSize = 20;
        profile.Signature.Color = "#0000FF";

        using var src = Photo(300, SKColors.Black);
        using var result = FrameRenderer.Render(src, profile);
        var box = FrameRenderer.GetLayout(src.Width, src.Height, profile).Signature!.Value;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 300 + 40; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var c = result.GetPixel(x, y);
                if (c.Red < 200)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }
        }

        Assert.True(maxX >= 0, "podpis nebyl vykreslen");
        Assert.True(minX >= box.Left - 2 && maxX <= box.Right + 2, $"x {minX}-{maxX} mimo {box.Left}-{box.Right}");
        Assert.True(minY >= box.Top - 2 && maxY <= box.Bottom + 2, $"y {minY}-{maxY} mimo {box.Top}-{box.Bottom}");
    }

    [Fact]
    public void Signature_Disabled_LeavesStripWhite()
    {
        var profile = DefaultProfile();
        profile.Lines.Clear();
        profile.Signature.Enabled = false;

        using var src = Photo(100, SKColors.Black);
        using var result = FrameRenderer.Render(src, profile);

        Assert.Null(FrameRenderer.GetLayout(100, 100, profile).Signature);
        for (var x = 0; x < result.Width; x++)
            Assert.Equal(SKColors.White, result.GetPixel(x, result.Height - 1));
    }

    [Fact]
    public void Signature_WithDiacritics_IsRenderedWithAccents()
    {
        // pevný font, aby test nezávisel na výchozím fontu profilu
        var profile = DefaultProfile();
        profile.Signature.FontFamily = "Segoe UI";
        profile.Signature.Text = "ěěěě";
        var diacritics = FrameRenderer.GetLayout(900, 900, profile).Signature!.Value;
        profile.Signature.Text = "eeee";
        var ascii = FrameRenderer.GetLayout(900, 900, profile).Signature!.Value;

        Assert.True(diacritics.Width > 0);
        Assert.True(diacritics.Height > ascii.Height, "háček musí zvýšit viditelný obrys");
    }

    [Fact]
    public void ReferenceSample_HasExpectedSize()
    {
        // example.jpg: 900 px fotka + 2 × 17 px pruh = 934 px
        var layout = FrameRenderer.GetLayout(900, 900, DefaultProfile());
        Assert.Equal(934, layout.OuterWidth);
        Assert.Equal(934, layout.OuterHeight);
    }
}
