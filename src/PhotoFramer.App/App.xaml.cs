using System.Windows;
using PhotoFramer.App.ViewModels;
using PhotoFramer.Core.Services;

namespace PhotoFramer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // tooltipy nad prvkem, aby je nepřekrýval (zvětšený) kurzor myši
        System.Windows.Controls.ToolTipService.PlacementProperty.OverrideMetadata(
            typeof(DependencyObject),
            new FrameworkPropertyMetadata(System.Windows.Controls.Primitives.PlacementMode.Top));

        var log = new FileLog();
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("Neošetřená chyba aplikace.", args.Exception);
            MessageBox.Show(args.Exception.Message, "PhotoFramer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var viewModel = new MainViewModel(log, new SettingsService(log));
        var window = new MainWindow(viewModel);

        // první argument = soubor ke zpracování (např. z Total Commanderu); otevře se hned po spuštění okna k ořezu
        var files = e.Args.Where(a => !a.StartsWith('-')).ToList();
        if (files.Count > 1)
            log.Warn($"Zadáno více souborů ({files.Count}); zpracuje se jen první: {files[0]}");
        if (files.Count > 0)
            window.StartupFile = files[0];

        window.Show();
    }
}
