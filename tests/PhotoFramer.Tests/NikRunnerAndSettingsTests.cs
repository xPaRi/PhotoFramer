using PhotoFramer.Core.Imaging;
using PhotoFramer.Core.Models;
using PhotoFramer.Core.Nik;
using PhotoFramer.Core.Services;
using SkiaSharp;

namespace PhotoFramer.Tests;

public class NikRunnerAndSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PhotoFramerTests_" + Guid.NewGuid().ToString("N"));

    public NikRunnerAndSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void FindExe_PrefersConfiguredPath()
    {
        var exe = Path.Combine(_dir, "custom.exe");
        File.WriteAllText(exe, "");
        Assert.Equal(exe, NikRunner.FindExe(NikPlugin.AnalogEfexPro2, exe, roots: [_dir]));
    }

    [Fact]
    public void FindExe_Prefers64Bit()
    {
        var a = Path.Combine(_dir, "Analog Efex Pro 2");
        var b = Path.Combine(a, "Analog Efex Pro 2 (64-Bit)");
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(a, "Analog Efex Pro 2.exe"), "");
        var x64 = Path.Combine(b, "Analog Efex Pro 2.exe");
        File.WriteAllText(x64, "");

        Assert.Equal(x64, NikRunner.FindExe(NikPlugin.AnalogEfexPro2, "", roots: [_dir]));
    }

    [Fact]
    public void Settings_RoundTrip_KeepsProfilesAndLines()
    {
        var path = Path.Combine(_dir, "settings.json");
        var svc = new SettingsService(NullLog.Instance, path);
        var settings = new AppSettings();
        var web = new Profile { Name = "Web", TargetSize = 1200, NikEnabled = false };
        web.Lines.Add(new FrameLine { Offset = 5, Thickness = 3, Color = "#112233" });
        settings.Profiles.Add(web);
        settings.ActiveProfile = "Web";

        svc.Save(settings);
        var loaded = svc.Load();

        Assert.Equal(2, loaded.Profiles.Count);
        Assert.Equal("Web", loaded.GetActiveProfile().Name);
        Assert.Equal(1200, loaded.GetActiveProfile().TargetSize);
        Assert.False(loaded.GetActiveProfile().NikEnabled);
        Assert.Equal(2, loaded.GetActiveProfile().Lines.Count);
        Assert.Equal("#112233", loaded.GetActiveProfile().Lines[1].Color);
    }

    [Fact]
    public void Settings_CorruptFile_FallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        var loaded = new SettingsService(NullLog.Instance, path).Load();
        Assert.Single(loaded.Profiles);
    }

    private sealed class FakeEditor(EditOutcome outcome) : IExternalEditor
    {
        public Task<EditOutcome> EditAsync(string filePath, CancellationToken ct) => Task.FromResult(outcome);
    }

    private string CreateSquareJpeg(int size)
    {
        var path = Path.Combine(_dir, "in.jpg");
        using var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.Teal);
        ImageIO.SaveJpeg(bmp, path, 90);
        return path;
    }

    [Fact]
    public async Task Pipeline_WithoutNik_ProducesFramedJpeg_AndCropsNonSquareToCenter()
    {
        var input = CreateSquareJpeg(1200);
        var profile = new Profile { NikEnabled = false, OutputFolder = Path.Combine(_dir, "out") };
        var pipeline = new ImagePipeline(null, NullLog.Instance);

        var result = await pipeline.ProcessAsync(input, profile);

        Assert.Equal(PipelineStatus.Done, result.Status);
        using var output = ImageIO.Load(result.OutputPath!);
        Assert.Equal(934, output.Width);
        Assert.Equal(934, output.Height);
        Assert.EndsWith("in_frame.jpg", result.OutputPath);

        using var wide = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var widePath = Path.Combine(_dir, "wide.jpg");
        ImageIO.SaveJpeg(wide, widePath, 90);
        var wideResult = await pipeline.ProcessAsync(widePath, profile);
        Assert.Equal(PipelineStatus.Done, wideResult.Status);
        using var wideOutput = ImageIO.Load(wideResult.OutputPath!);
        Assert.Equal(934, wideOutput.Width); // ořezáno na střed, pak 900 + 2 × 17
    }

    [Fact]
    public async Task Pipeline_NikUnchanged_SkipsPhoto_AndCleansTempFiles()
    {
        var input = CreateSquareJpeg(500);
        var profile = new Profile { NikEnabled = true, OutputFolder = Path.Combine(_dir, "out") };
        var pipeline = new ImagePipeline(new FakeEditor(EditOutcome.Unchanged), NullLog.Instance);

        var before = Directory.Exists(ImagePipeline.TempFolder) ? Directory.GetFiles(ImagePipeline.TempFolder).Length : 0;
        var result = await pipeline.ProcessAsync(input, profile);
        var after = Directory.Exists(ImagePipeline.TempFolder) ? Directory.GetFiles(ImagePipeline.TempFolder).Length : 0;

        Assert.Equal(PipelineStatus.SkippedNikCancelled, result.Status);
        Assert.False(Directory.Exists(profile.OutputFolder));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Pipeline_NikSaved_UsesEditedFile()
    {
        var input = CreateSquareJpeg(500);
        var profile = new Profile { NikEnabled = true, OutputFolder = Path.Combine(_dir, "out") };
        var pipeline = new ImagePipeline(new FakeEditor(EditOutcome.Saved), NullLog.Instance);

        var result = await pipeline.ProcessAsync(input, profile);

        Assert.Equal(PipelineStatus.Done, result.Status);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public async Task Pipeline_PngOutput_WritesDecodablePngWithPngExtension()
    {
        var input = CreateSquareJpeg(500);
        var profile = new Profile { NikEnabled = false, OutputFormat = OutputFormat.Png, PngCompression = 9, OutputFolder = Path.Combine(_dir, "out") };

        var result = await new ImagePipeline(null, NullLog.Instance).ProcessAsync(input, profile);

        Assert.Equal(PipelineStatus.Done, result.Status);
        Assert.EndsWith("in_frame.png", result.OutputPath);
        using var output = ImageIO.Load(result.OutputPath!);
        Assert.Equal(934, output.Width);
        // PNG signature
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(result.OutputPath!).Take(4).ToArray());
    }

    [Fact]
    public void Quality_IsClampedToValidRange()
    {
        var profile = new Profile { JpegQuality = 95111111, PngCompression = 42 };
        Assert.Equal(100, profile.JpegQuality);
        Assert.Equal(9, profile.PngCompression);
    }

    [Fact]
    public void OutputPath_EmptySuffix_IsNotFilledIn()
    {
        var profile = new Profile { Suffix = "", OutputFolder = "" };
        var input = Path.Combine(_dir, "a.jpg");
        Assert.Equal(input, ImagePipeline.GetOutputPath(input, profile));
        Assert.Equal(Path.Combine(_dir, "a.png"), ImagePipeline.GetOutputPath(input, new Profile { Suffix = "", OutputFormat = OutputFormat.Png }));
    }
}
