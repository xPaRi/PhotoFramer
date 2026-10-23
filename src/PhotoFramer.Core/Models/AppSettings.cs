using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoFramer.Core.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Profile> _profiles = [new Profile()];
    [ObservableProperty] private string _activeProfile = "PaRi";

    /// <summary>Prázdné = automatické hledání pod Nik Collection.</summary>
    [ObservableProperty] private string _nikExePath = "";

    [ObservableProperty] private bool _darkMode = true;
    [ObservableProperty] private bool _showLog = true;

    /// <summary>Vysunutý panel nastavení (hamburger).</summary>
    [ObservableProperty] private bool _settingsOpen;

    /// <summary>Poloha a velikost hlavního okna (prázdné = výchozí).</summary>
    [ObservableProperty] private double? _windowLeft;
    [ObservableProperty] private double? _windowTop;
    [ObservableProperty] private double? _windowWidth;
    [ObservableProperty] private double? _windowHeight;
    [ObservableProperty] private bool _windowMaximized;

    /// <summary>Při uložení do souboru uložit hotový obrázek také do schránky.</summary>
    [ObservableProperty] private bool _copyToClipboardOnSave;

    /// <summary>Po uložení fotky otevřené z příkazové řádky zavřít okno.</summary>
    [ObservableProperty] private bool _closeWhenDone = true;

    public Profile GetActiveProfile() =>
        Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles.First();
}
