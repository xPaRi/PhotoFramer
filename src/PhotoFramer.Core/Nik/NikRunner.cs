using System.Diagnostics;
using PhotoFramer.Core.Services;

namespace PhotoFramer.Core.Nik;

/// <summary>Popis externího editoru z Nik Collection (jiný plugin = jiný popis).</summary>
public sealed record NikPlugin(string Name, string ExeFilePattern)
{
    public static readonly NikPlugin AnalogEfexPro2 = new("Analog Efex Pro 2", "Analog Efex Pro 2*.exe");
}

public enum EditOutcome
{
    /// <summary>Soubor byl upraven (uživatel kliknul Save).</summary>
    Saved,
    /// <summary>Soubor se nezměnil – uživatel zavřel okno bez uložení.</summary>
    Unchanged,
}

public interface IExternalEditor
{
    /// <summary>Otevře soubor v editoru, počká na ukončení a zjistí, zda byl přepsán.</summary>
    Task<EditOutcome> EditAsync(string filePath, CancellationToken ct);
}

public sealed class NikRunner : IExternalEditor
{
    private readonly NikPlugin _plugin;
    private readonly string? _configuredPath;
    private readonly IAppLog _log;

    public NikRunner(NikPlugin plugin, string? configuredPath, IAppLog log)
    {
        _plugin = plugin;
        _configuredPath = configuredPath;
        _log = log;
    }

    public static IEnumerable<string> DefaultRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Nik Collection");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Nik Collection");
    }

    /// <summary>Nakonfigurovaná cesta, jinak hledání pod Nik Collection (přednost 64-bit).</summary>
    public static string? FindExe(NikPlugin plugin, string? configuredPath, IEnumerable<string>? roots = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var candidates = new List<string>();
        foreach (var root in roots ?? DefaultRoots())
        {
            if (!Directory.Exists(root))
                continue;
            candidates.AddRange(Directory.EnumerateFiles(root, plugin.ExeFilePattern, SearchOption.AllDirectories));
        }
        return candidates
            .OrderByDescending(p => p.Contains("64-Bit", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    public async Task<EditOutcome> EditAsync(string filePath, CancellationToken ct)
    {
        var exe = FindExe(_plugin, _configuredPath)
            ?? throw new FileNotFoundException($"{_plugin.Name}: spustitelný soubor nenalezen. Nastavte cestu v nastavení.");

        var before = new FileInfo(filePath);
        var (timeBefore, sizeBefore) = (before.LastWriteTimeUtc, before.Length);

        _log.Info($"Spouštím {_plugin.Name}: {exe}");
        using var process = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { filePath }, UseShellExecute = false })
            ?? throw new InvalidOperationException($"{_plugin.Name} se nepodařilo spustit.");

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* už skončil */ }
            throw;
        }

        var after = new FileInfo(filePath);
        var changed = after.LastWriteTimeUtc != timeBefore || after.Length != sizeBefore;
        _log.Info($"{_plugin.Name} skončil (kód {process.ExitCode}), soubor {(changed ? "upraven" : "beze změny")}.");
        return changed ? EditOutcome.Saved : EditOutcome.Unchanged;
    }
}
