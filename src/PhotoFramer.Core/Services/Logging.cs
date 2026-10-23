namespace PhotoFramer.Core.Services;

public interface IAppLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLog : IAppLog
{
    public static readonly NullLog Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? ex = null) { }
}

/// <summary>Jednoduchý log do souboru; každý řádek se zároveň ohlásí událostí (pro UI).</summary>
public sealed class FileLog : IAppLog
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoFramer", "photoframer.log");

    private readonly string _path;
    private readonly object _lock = new();

    public FileLog(string? path = null) => _path = path ?? DefaultPath;

    /// <summary>Může přijít z libovolného vlákna.</summary>
    public event Action<string>? LineWritten;

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} {ex}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // log nesmí shodit aplikaci
        }
        LineWritten?.Invoke(line);
    }
}
