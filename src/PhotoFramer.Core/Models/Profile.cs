using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoFramer.Core.Models;

public enum OutputFormat
{
    Jpeg,
    Png,
}

/// <summary>Linka v pruhu kolem fotky. Vzdálenost se měří od okraje fotky směrem ven.</summary>
public partial class FrameLine : ObservableObject
{
    [ObservableProperty] private int _offset = 2;
    [ObservableProperty] private int _thickness = 2;
    [ObservableProperty] private string _color = "#0080FF";
}

public partial class SignatureSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _text = "PaRi";
    [ObservableProperty] private string _fontFamily = "Brush Script MT";
    [ObservableProperty] private int _fontSize = 15;
    partial void OnFontSizeChanged(int value) { var v = Math.Clamp(value, 1, 500); if (v != value) FontSize = v; }
    [ObservableProperty] private bool _bold;
    [ObservableProperty] private bool _italic = false;
    [ObservableProperty] private string _color = "#0080FF";
    [ObservableProperty] private int _marginRight = 14;
    [ObservableProperty] private int _marginBottom = 1;
}

public partial class Profile : ObservableObject
{
    [ObservableProperty] private string _name = "PaRi";

    /// <summary>Cílová velikost čtverce; 0 = nezmenšovat.</summary>
    [ObservableProperty] private int _targetSize = 900;
    partial void OnTargetSizeChanged(int value) { var v = Math.Clamp(value, 0, 20000); if (v != value) TargetSize = v; }

    [ObservableProperty] private bool _nikEnabled = true;

    [ObservableProperty] private int _borderWidth = 17;
    [ObservableProperty] private string _borderColor = "#FFFFFF";
    [ObservableProperty] private ObservableCollection<FrameLine> _lines = [new FrameLine()];
    [ObservableProperty] private SignatureSettings _signature = new();

    [ObservableProperty] private OutputFormat _outputFormat = OutputFormat.Jpeg;

    [ObservableProperty] private int _jpegQuality = 95;
    partial void OnJpegQualityChanged(int value) { var v = Math.Clamp(value, 1, 100); if (v != value) JpegQuality = v; }

    /// <summary>Úroveň komprese PNG 0–9 (bezeztrátové, ovlivňuje jen velikost souboru a rychlost).</summary>
    [ObservableProperty] private int _pngCompression = 6;
    partial void OnPngCompressionChanged(int value) { var v = Math.Clamp(value, 0, 9); if (v != value) PngCompression = v; }

    /// <summary>Prázdné = vedle původního souboru.</summary>
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _suffix = "_frame";

    public override string ToString() => Name;
}
