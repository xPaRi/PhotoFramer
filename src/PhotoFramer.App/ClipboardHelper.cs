using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoFramer.App;

/// <summary>Přístup ke schránce s opakováním: schránku občas drží jiná aplikace a operace selže.</summary>
public static class ClipboardHelper
{
    private const int Attempts = 6;
    private const int DelayMs = 100;

    public const string InaccessibleMessage = "Schránka je nepřístupná (používá ji jiná aplikace). Zkuste to za chvíli znovu.";

    public enum ReadStatus { Image, FileList, Empty, Inaccessible }

    /// <summary>Přečte obrázek nebo seznam souborů ze schránky.</summary>
    public static ReadStatus TryRead(out BitmapSource? image, out string? firstFile)
    {
        image = null;
        firstFile = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    image = Clipboard.GetImage();
                    if (image is not null)
                        return ReadStatus.Image;
                }
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count > 0)
                    {
                        firstFile = files[0];
                        return ReadStatus.FileList;
                    }
                }
                return ReadStatus.Empty;
            }
            catch (ExternalException) when (attempt < Attempts)
            {
                Thread.Sleep(DelayMs);
            }
            catch (ExternalException)
            {
                return ReadStatus.Inaccessible;
            }
        }
        return ReadStatus.Inaccessible;
    }

    /// <summary>Uloží obrázek do schránky; vrací false, pokud je schránka nepřístupná.</summary>
    public static bool TryWriteImage(BitmapSource image)
    {
        // neprůhledný formát, aby obrázek ve vkládající aplikaci nebyl průhledný/černý
        var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.Bitmap, opaque), copy: true);
                return true;
            }
            catch (ExternalException) when (attempt < Attempts)
            {
                Thread.Sleep(DelayMs);
            }
            catch (ExternalException)
            {
                return false;
            }
        }
        return false;
    }
}
