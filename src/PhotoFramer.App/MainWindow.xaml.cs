using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PhotoFramer.App.ViewModels;
using PhotoFramer.Core.Models;

namespace PhotoFramer.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // téma musí být nastaveno před načtením XAML, aby se styly odvozovaly od Fluent šablon
        ApplyTheme(viewModel.Settings.DarkMode);
        InitializeComponent();
        viewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.DarkMode))
                ApplyTheme(viewModel.Settings.DarkMode);
        };
        RestorePlacement(viewModel.Settings);
        SettingsHost.BeginAnimation(WidthProperty, null);
        SettingsHost.Width = viewModel.Settings.SettingsOpen ? SettingsWidth : 0;

        viewModel.CloseRequested += Close;
        Loaded += async (_, _) =>
        {
            if (StartupFile is not null)
                await viewModel.OpenPathAsync(StartupFile, fromCommandLine: true);
        };
        Closing += (_, _) =>
        {
            StorePlacement(viewModel.Settings);
            viewModel.SaveSettings();
        };
    }

    public string? StartupFile { get; set; }

    private static void ApplyTheme(bool dark) =>
        Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

    private void RestorePlacement(AppSettings s)
    {
        if (s.WindowWidth is not { } width || s.WindowHeight is not { } height ||
            s.WindowLeft is not { } left || s.WindowTop is not { } top)
        {
            // výchozí velikost nesmí přesáhnout pracovní plochu
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            return;
        }

        // okno musí být aspoň částečně na některé z obrazovek (monitor mohl zmizet)
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(left, top, Math.Max(width, MinWidth), Math.Max(height, MinHeight));
        var visible = Rect.Intersect(screen, bounds);
        if (visible.IsEmpty || visible.Width < 100 || visible.Height < 100)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void StorePlacement(AppSettings s)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        s.WindowMaximized = WindowState == WindowState.Maximized;
    }

    private void ProfileNameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            ProfileNameBox.Focus();
            ProfileNameBox.SelectAll();
        }
    }

    private const double SettingsWidth = 460;

    /// <summary>Vysune nebo zasune panel nastavení.</summary>
    private void MenuButton_Toggled(object sender, RoutedEventArgs e)
    {
        if (SettingsHost is null)
            return; // událost při inicializaci XAML

        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            _viewModel.Settings.SettingsOpen ? SettingsWidth : 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase(),
        };
        SettingsHost.BeginAnimation(WidthProperty, animation);
    }

    /// <summary>Klávesy ořezu platí v celém okně, kromě editace textu a výběru v comboboxu.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.ShowCrop || e.OriginalSource is TextBoxBase or Selector or ComboBoxItem
            || e.OriginalSource is DependencyObject source && IsInMenu(source))
            return;
        if (Crop.HandleKey(e.Key))
            e.Handled = true;
    }

    /// <summary>Šipky v otevřeném menu patří menu, ne ořezu.</summary>
    private static bool IsInMenu(DependencyObject element)
    {
        for (var d = element; d is not null;
             d = (d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : null)
                 ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is MenuBase or MenuItem)
                return true;
        }
        return false;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void LogBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => LogBox.ScrollToEnd();

    private void ProfileNameBox_LostFocus(object sender, RoutedEventArgs e) => _viewModel.EndRenameCommand.Execute(null);

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await _viewModel.OpenPathAsync(files[0], fromCommandLine: false);
    }
}
