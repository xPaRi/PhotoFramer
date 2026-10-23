using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhotoFramer.App.Controls;

public enum InputFilterMode
{
    None,
    /// <summary>Jen číslice (nezáporná celá čísla).</summary>
    Digits,
    /// <summary>Barva: volitelné # a nejvýše 6 hexadecimálních číslic.</summary>
    Hex,
}

/// <summary>Připojená vlastnost, která do TextBoxu nepustí neplatné znaky (psaní, vkládání, mezeru).</summary>
public static class InputFilter
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(InputFilterMode), typeof(InputFilter),
        new PropertyMetadata(InputFilterMode.None, OnModeChanged));

    public static InputFilterMode GetMode(DependencyObject d) => (InputFilterMode)d.GetValue(ModeProperty);
    public static void SetMode(DependencyObject d, InputFilterMode value) => d.SetValue(ModeProperty, value);

    private static readonly Regex DigitsPattern = new(@"^\d*$", RegexOptions.Compiled);
    private static readonly Regex HexPattern = new(@"^#?[0-9A-Fa-f]{0,6}$", RegexOptions.Compiled);

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
            return;

        box.PreviewTextInput -= OnPreviewTextInput;
        box.PreviewKeyDown -= OnPreviewKeyDown;
        DataObject.RemovePastingHandler(box, OnPasting);

        if ((InputFilterMode)e.NewValue == InputFilterMode.None)
            return;

        box.PreviewTextInput += OnPreviewTextInput;
        box.PreviewKeyDown += OnPreviewKeyDown;
        DataObject.AddPastingHandler(box, OnPasting);
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        if (!IsValid(box, Proposed(box, e.Text)))
            e.Handled = true;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // mezera nevyvolá PreviewTextInput
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !IsValid(box, Proposed(box, text)))
            e.CancelCommand();
    }

    private static string Proposed(TextBox box, string input) =>
        box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, input);

    private static bool IsValid(TextBox box, string text) => GetMode(box) switch
    {
        InputFilterMode.Digits => DigitsPattern.IsMatch(text),
        InputFilterMode.Hex => HexPattern.IsMatch(text),
        _ => true,
    };
}
