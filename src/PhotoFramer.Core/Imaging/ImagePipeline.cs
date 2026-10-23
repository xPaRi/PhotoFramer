using PhotoFramer.Core.Models;
using PhotoFramer.Core.Nik;
using PhotoFramer.Core.Services;
using SkiaSharp;

namespace PhotoFramer.Core.Imaging;

public enum PipelineStatus
{
    Done,
    SkippedNikCancelled,
    Failed,
}

public sealed record PipelineResult(PipelineStatus Status, string? OutputPath, string Message);

/// <summary>
/// Zpracování fotky ve dvou krocích, aby mezi nimi šlo měnit rámeček:
/// <see cref="PrepareAsync"/> (ořez, zmenšení, Nik) a <see cref="SaveFramed"/> (rámeček, podpis, JPEG).
/// </summary>
public sealed class ImagePipeline
{
    private readonly IExternalEditor? _editor;
    private readonly IAppLog _log;

    /// <param name="editor">Externí editor (Analog Efex); lze null, pokud profil Nik krok nepoužívá.</param>
    public ImagePipeline(IExternalEditor? editor, IAppLog log)
    {
        _editor = editor;
        _log = log;
    }

    public static string TempFolder { get; } = Path.Combine(Path.GetTempPath(), "PhotoFramer");

    /// <summary>
    /// Ořez (zadaný výřez, jinak ze středu), zmenšení na cílovou velikost a volitelně úprava v Nik.
    /// Vrací null, pokud uživatel Nik zavřel bez uložení. Výsledek je třeba uvolnit.
    /// </summary>
    public async Task<SKBitmap?> PrepareAsync(SKBitmap source, Profile profile, SKRectI? crop, CancellationToken ct = default)
    {
        SKBitmap? cropped = null;
        SKBitmap? resized = null;
        string? tempPath = null;
        try
        {
            var square = source;
            if (crop is { } area)
            {
                cropped = ImageIO.Crop(source, area);
                square = cropped;
            }
            if (square.Width != square.Height)
            {
                _log.Warn($"Fotka není čtvercová ({square.Width}×{square.Height}), ořezávám na střed.");
                var centered = ImageIO.CropCenterSquare(square);
                cropped?.Dispose();
                cropped = centered;
                square = centered;
            }

            var size = profile.TargetSize > 0 ? profile.TargetSize : square.Width;
            resized = await Task.Run(() => ImageIO.Resize(square, size, size), ct);

            if (!profile.NikEnabled)
                return Take(ref resized);

            if (_editor is null)
                throw new InvalidOperationException("Nik krok je zapnutý, ale editor není k dispozici.");

            Directory.CreateDirectory(TempFolder);
            tempPath = Path.Combine(TempFolder, $"{Guid.NewGuid():N}.tif");
            await Task.Run(() => ImageIO.SaveTiff(resized, tempPath), ct);

            if (await _editor.EditAsync(tempPath, ct) == EditOutcome.Unchanged)
            {
                _log.Warn("Analog Efex bez uložení.");
                return null;
            }
            return await Task.Run(() => ImageIO.Load(tempPath), ct);
        }
        finally
        {
            cropped?.Dispose();
            resized?.Dispose();
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); } catch (Exception ex) { _log.Warn($"Dočasný soubor nelze smazat: {ex.Message}"); }
            }
        }
    }

    /// <summary>Přidá rámeček a podpis a uloží JPEG nebo PNG podle profilu.</summary>
    public void SaveFramed(SKBitmap edited, Profile profile, string outputPath)
    {
        using var framed = FrameRenderer.Render(edited, profile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (profile.OutputFormat == OutputFormat.Png)
            ImageIO.SavePng(framed, outputPath, profile.PngCompression);
        else
            ImageIO.SaveJpeg(framed, outputPath, profile.JpegQuality);
        _log.Info($"Uloženo: {outputPath} ({framed.Width}×{framed.Height})");
    }

    /// <summary>Celé zpracování jedné fotky ze souboru najednou.</summary>
    /// <param name="outputPath">Cílový soubor; bez něj se odvodí z profilu (složka + přípona názvu).</param>
    public async Task<PipelineResult> ProcessAsync(string inputPath, Profile profile, SKRectI? crop = null, string? outputPath = null, CancellationToken ct = default)
    {
        try
        {
            _log.Info($"Zpracovávám: {inputPath} (profil {profile.Name})");
            using var source = await Task.Run(() => ImageIO.Load(inputPath), ct);
            using var edited = await PrepareAsync(source, profile, crop, ct);
            if (edited is null)
                return new PipelineResult(PipelineStatus.SkippedNikCancelled, null, "Úprava v Analog Efex zrušena, fotka přeskočena.");

            var output = outputPath ?? GetOutputPath(inputPath, profile);
            await Task.Run(() => SaveFramed(edited, profile, output), ct);
            return new PipelineResult(PipelineStatus.Done, output, $"Uloženo: {output}");
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Zpracování zrušeno.");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Chyba při zpracování {inputPath}", ex);
            return new PipelineResult(PipelineStatus.Failed, null, $"Chyba: {ex.Message}");
        }
    }

    public static string GetOutputPath(string inputPath, Profile profile)
    {
        var folder = string.IsNullOrWhiteSpace(profile.OutputFolder)
            ? Path.GetDirectoryName(Path.GetFullPath(inputPath))!
            : profile.OutputFolder;
        var name = Path.GetFileNameWithoutExtension(inputPath) + profile.Suffix;
        var extension = profile.OutputFormat == OutputFormat.Png ? ".png" : ".jpg";
        var output = Path.Combine(folder, name + extension);

        // nikdy nepřepsat originál
        if (string.Equals(output, Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase))
            output = Path.Combine(folder, name + "_framed" + extension);
        return output;
    }

    private static T? Take<T>(ref T? value) where T : class
    {
        var result = value;
        value = null;
        return result;
    }
}
