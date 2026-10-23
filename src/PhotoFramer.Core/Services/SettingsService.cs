using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoFramer.Core.Models;

namespace PhotoFramer.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoFramer", "settings.json");

    private readonly string _path;
    private readonly IAppLog _log;

    public SettingsService(IAppLog log, string? path = null)
    {
        _log = log;
        _path = path ?? DefaultPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
                if (settings is { Profiles.Count: > 0 })
                    return settings;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Nastavení nelze načíst ({_path}), použijí se výchozí.", ex);
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            _log.Error($"Nastavení nelze uložit ({_path}).", ex);
        }
    }
}
