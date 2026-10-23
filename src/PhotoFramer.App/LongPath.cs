using System.Runtime.InteropServices;
using System.Text;

namespace PhotoFramer.App;

/// <summary>Převod zkrácených názvů 8.3 (např. NAPOTK~1.PNG, které předává např. Total Commander) na dlouhé.</summary>
public static class LongPath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferSize);

    public static string Expand(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var buffer = new StringBuilder(1024);
            var length = GetLongPathName(full, buffer, (uint)buffer.Capacity);
            if (length == 0)
                return full;
            if (length > buffer.Capacity)
            {
                buffer = new StringBuilder((int)length);
                length = GetLongPathName(full, buffer, (uint)buffer.Capacity);
            }
            return length == 0 ? full : buffer.ToString();
        }
        catch (Exception)
        {
            return path;
        }
    }
}
