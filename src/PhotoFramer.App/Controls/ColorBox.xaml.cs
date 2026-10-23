using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PhotoFramer.App.Controls;

/// <summary>Náhled barvy + pole pro hex + vyskakovací okno s posuvníky RGB.</summary>
public partial class ColorBox : UserControl
{
    public static readonly DependencyProperty HexProperty = DependencyProperty.Register(
        nameof(Hex), typeof(string), typeof(ColorBox),
        new FrameworkPropertyMetadata("#000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorBox)d).OnHexChanged()));

    private static readonly string[] PresetColors =
        ["#FFFFFF", "#000000", "#808080", "#3474B3", "#1F4E79", "#C00000", "#2E7D32", "#F2C14E"];

    private bool _updating;

    public ColorBox()
    {
        InitializeComponent();
        Presets.ItemsSource = PresetColors.Select(c => new SolidColorBrush((Color)ColorConverter.ConvertFromString(c))).ToList();
        OnHexChanged();
    }

    public string Hex
    {
        get => (string)GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    private void OnHexChanged()
    {
        if (!TryParse(Hex, out var color))
            return;
        Swatch.Background = new SolidColorBrush(color);
        if (_updating)
            return;
        _updating = true;
        SliderR.Value = color.R;
        SliderG.Value = color.G;
        SliderB.Value = color.B;
        _updating = false;
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        // popup je samostatné okno, nedědí téma; přizpůsobí se tmavému/světlému režimu
        var dark = Application.Current.ThemeMode == ThemeMode.Dark;
        PopupBorder.Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White);
        TextElement.SetForeground(PopupBorder, new SolidColorBrush(dark ? Colors.White : Colors.Black));
        Popup.IsOpen = true;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating)
            return;
        _updating = true;
        Hex = $"#{(int)SliderR.Value:X2}{(int)SliderG.Value:X2}{(int)SliderB.Value:X2}";
        _updating = false;
    }

    private void Preset_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SolidColorBrush brush)
            Hex = $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
    }

    private static bool TryParse(string? hex, out Color color)
    {
        color = default;
        try
        {
            if (string.IsNullOrWhiteSpace(hex))
                return false;
            // # je volitelné; neúplná hodnota (méně než 6 číslic) se nepoužije
            var digits = hex.TrimStart('#');
            if (digits.Length != 6)
                return false;
            color = (Color)ColorConverter.ConvertFromString("#" + digits);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
