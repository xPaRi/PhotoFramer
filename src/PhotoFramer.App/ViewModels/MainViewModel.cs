using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhotoFramer.Core.Imaging;
using PhotoFramer.Core.Models;
using PhotoFramer.Core.Nik;
using PhotoFramer.Core.Services;
using SkiaSharp;

namespace PhotoFramer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string ImageFilter = "Obrázky|*.jpg;*.jpeg;*.tif;*.tiff;*.png|Všechny soubory|*.*";

    private readonly FileLog _log;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _previewTimer;
    private CancellationTokenSource? _cts;

    private SKBitmap? _photo;      // originál v plném rozlišení (pro ořez)
    private SKBitmap? _edited;     // po ořezu, zmenšení a Nik; rámeček se kreslí až při zobrazení/uložení
    private bool _openedFromCommandLine;
    private string _clipboardStamp = "";

    public MainViewModel(FileLog log, SettingsService settingsService)
    {
        _log = log;
        _settingsService = settingsService;
        Settings = settingsService.Load();
        FontFamilies = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).ToList();

        foreach (var profile in Settings.Profiles)
            Hook(profile);

        _activeProfile = Settings.GetActiveProfile();

        // výchozí externí aplikace = Analog Efex, který se skutečně použije
        if (string.IsNullOrWhiteSpace(Settings.NikExePath))
            Settings.NikExePath = NikRunner.FindExe(NikPlugin.AnalogEfexPro2, null) ?? "";
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.NikExePath)) OnPropertyChanged(nameof(NikPathWarning));
        };

        var dispatcher = Dispatcher.CurrentDispatcher;
        _log.LineWritten += line => dispatcher.InvokeAsync(() => AddLogLine(line));
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RenderPreview(); };
        SchedulePreview();
    }

    public AppSettings Settings { get; }
    public IReadOnlyList<string> FontFamilies { get; }

    public IReadOnlyList<KeyValuePair<OutputFormat, string>> OutputFormats { get; } =
    [
        new(OutputFormat.Jpeg, "JPG"),
        new(OutputFormat.Png, "PNG"),
    ];
    private readonly List<string> _logLines = [];

    /// <summary>Posledních pár set řádků logu, oddělených jedním zalomením.</summary>
    [ObservableProperty] private string _logText = "";

    [ObservableProperty] private Profile _activeProfile;

    /// <summary>Fotka s rámečkem a podpisem (po ořezu), jinak zástupný náhled.</summary>
    [ObservableProperty] private BitmapSource? _previewImage;

    [ObservableProperty] private string _status = "Přetáhněte fotku do okna nebo klikněte na Otevřít…";

    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CropCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteFromClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [ObservableProperty] private bool _isBusy;

    /// <summary>Probíhá krok, který lze přerušit (čekání na externí aplikaci).</summary>
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [ObservableProperty] private bool _isCancellable;

    /// <summary>Otevřená fotka v plném rozlišení pro ořez.</summary>
    [ObservableProperty] private BitmapSource? _photoImage;

    [NotifyPropertyChangedFor(nameof(ShowCrop))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CropCommand))]
    [ObservableProperty] private bool _hasPhoto;

    /// <summary>Ořez a případně Nik jsou hotové, zobrazuje se fotka s rámečkem.</summary>
    [NotifyPropertyChangedFor(nameof(ShowCrop))]
    [NotifyCanExecuteChangedFor(nameof(CropCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [ObservableProperty] private bool _isCropped;

    [ObservableProperty] private SKRectI _cropRect;
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [ObservableProperty] private string? _currentPath;

    public bool ShowCrop => HasPhoto && !IsCropped;

    /// <summary>Záhlaví okna: název otevřeného souboru, jinak jen název aplikace.</summary>
    public string WindowTitle => CurrentPath is not null ? $"{Path.GetFileName(CurrentPath)} – PhotoFramer"
        : HasPhoto ? "Schránka – PhotoFramer"
        : "PhotoFramer";

    /// <summary>Varování pod cestou k externí aplikaci; null = bez varování.</summary>
    public string? NikPathWarning =>
        !ActiveProfile.NikEnabled ? null
        : string.IsNullOrWhiteSpace(Settings.NikExePath) ? "Cesta není vyplněna – při ořezu se zkusí najít Analog Efex automaticky."
        : !File.Exists(Settings.NikExePath) ? "Soubor neexistuje."
        : null;

    /// <summary>Uložení souboru otevřeného z příkazové řádky skončilo úspěchem a je zapnuto zavírání.</summary>
    public event Action? CloseRequested;

    partial void OnActiveProfileChanged(Profile value)
    {
        Settings.ActiveProfile = value.Name;
        OnPropertyChanged(nameof(NikPathWarning));
        SchedulePreview();
    }

    partial void OnIsCroppedChanged(bool value) => SchedulePreview();

    // ---- profily ----

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new Profile { Name = UniqueName("Nový profil") };
        Settings.Profiles.Add(profile);
        Hook(profile);
        ActiveProfile = profile;
    }

    [ObservableProperty] private bool _isRenaming;

    /// <summary>Přepne combobox s profily na textové pole s názvem.</summary>
    [RelayCommand]
    private void RenameProfile() => IsRenaming = true;

    [RelayCommand]
    private void EndRename()
    {
        if (!IsRenaming)
            return;
        if (string.IsNullOrWhiteSpace(ActiveProfile.Name))
            ActiveProfile.Name = UniqueName("Profil");
        IsRenaming = false;
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ActiveProfile);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Profile>(json)!;
        copy.Name = UniqueName(ActiveProfile.Name + " – kopie");
        Settings.Profiles.Add(copy);
        Hook(copy);
        ActiveProfile = copy;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (Settings.Profiles.Count <= 1)
            return;
        var answer = MessageBox.Show($"Opravdu smazat profil „{ActiveProfile.Name}“?", "Smazat profil",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;
        var index = Settings.Profiles.IndexOf(ActiveProfile);
        Settings.Profiles.Remove(ActiveProfile);
        ActiveProfile = Settings.Profiles[Math.Min(index, Settings.Profiles.Count - 1)];
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Settings.Profiles.Any(p => p.Name == name); i++)
            name = $"{baseName} {i}";
        return name;
    }

    // ---- rámeček ----

    [RelayCommand]
    private void AddLine() => ActiveProfile.Lines.Add(new FrameLine { Offset = 2, Thickness = 2 });

    [RelayCommand]
    private void RemoveLine(FrameLine line) => ActiveProfile.Lines.Remove(line);

    // ---- výběry souborů ----

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Výstupní složka" };
        if (dialog.ShowDialog() == true)
            ActiveProfile.OutputFolder = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseNikExe()
    {
        var dialog = new OpenFileDialog { Title = "Analog Efex Pro 2.exe", Filter = "Programy|*.exe" };
        if (dialog.ShowDialog() == true)
            Settings.NikExePath = dialog.FileName;
    }

    // ---- otevření, ořez, uložení ----

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog { Title = "Otevřít fotku", Filter = ImageFilter };
        if (dialog.ShowDialog() == true)
            await OpenPathAsync(dialog.FileName, fromCommandLine: false);
    }

    private bool NotBusy() => !IsBusy;

    /// <summary>Načte původní soubor znovu a vrátí se k ořezu.</summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (CurrentPath is not null)
        {
            await OpenPathAsync(CurrentPath, _openedFromCommandLine);
            return;
        }

        // obrázek ze schránky nemá soubor; vrátí se k původnímu obrázku a ořezu
        _edited?.Dispose();
        _edited = null;
        IsCropped = false;
        Status = "Původní obrázek ze schránky – upravte výřez a klikněte na Ořez";
    }

    private bool CanReload() => HasPhoto && !IsBusy;

    /// <summary>Načte fotku do hlavního okna, kde se ručně ořízne.</summary>
    public async Task OpenPathAsync(string path, bool fromCommandLine)
    {
        if (IsBusy)
            return;
        path = LongPath.Expand(path);
        if (!File.Exists(path))
        {
            Status = $"Soubor neexistuje: {path}";
            _log.Warn(Status);
            return;
        }

        IsBusy = true;
        Status = $"Načítám {Path.GetFileName(path)}…";
        try
        {
            var loaded = await Task.Run(() => ImageIO.Load(path));
            var image = await Task.Run(() => ImageIO.ToBitmapSource(loaded));
            SetPhoto(loaded, image, path, fromCommandLine);
            Status = $"{Path.GetFileName(path)} ({loaded.Width}×{loaded.Height}) – upravte výřez a klikněte na Ořez";
            _log.Info($"Otevřeno: {path}");
        }
        catch (Exception ex)
        {
            _log.Error($"Fotku nelze otevřít: {path}", ex);
            Status = $"Fotku nelze otevřít: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetPhoto(SKBitmap loaded, BitmapSource image, string? path, bool fromCommandLine)
    {
        _photo?.Dispose();
        _edited?.Dispose();
        _photo = loaded;
        _edited = null;
        PhotoImage = image;
        CurrentPath = path;
        _openedFromCommandLine = fromCommandLine;
        HasPhoto = true;
        IsCropped = false;
    }

    /// <summary>Vloží obrázek ze schránky (nebo první zkopírovaný soubor) k ořezu.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task PasteFromClipboardAsync()
    {
        switch (ClipboardHelper.TryRead(out var image, out var file))
        {
            case ClipboardHelper.ReadStatus.Image:
                try
                {
                    var bitmap = ImageIO.FromBitmapSource(image!);
                    SetPhoto(bitmap, ImageIO.ToBitmapSource(bitmap), null, fromCommandLine: false);
                    _clipboardStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    Status = $"Obrázek ze schránky ({bitmap.Width}×{bitmap.Height}) – upravte výřez a klikněte na Ořez";
                    _log.Info("Vložen obrázek ze schránky.");
                }
                catch (Exception ex)
                {
                    _log.Error("Obrázek ze schránky nelze zpracovat.", ex);
                    Status = $"Obrázek ze schránky nelze zpracovat: {ex.Message}";
                }
                break;
            case ClipboardHelper.ReadStatus.FileList:
                await OpenPathAsync(file!, fromCommandLine: false);
                break;
            case ClipboardHelper.ReadStatus.Inaccessible:
                _log.Warn("Schránka je nepřístupná.");
                Status = ClipboardHelper.InaccessibleMessage;
                break;
            default:
                Status = "Ve schránce není obrázek.";
                break;
        }
    }

    /// <summary>Zkopíruje hotový obrázek s rámečkem a podpisem do schránky.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void CopyToClipboard()
    {
        if (TryCopyToClipboard())
            Status = "Obrázek zkopírován do schránky.";
    }

    /// <summary>Vrací false a nastaví stav, pokud se kopírování nezdařilo (např. schránku drží jiná aplikace).</summary>
    private bool TryCopyToClipboard()
    {
        try
        {
            using var framed = FrameRenderer.Render(_edited!, ActiveProfile);
            if (ClipboardHelper.TryWriteImage(ImageIO.ToBitmapSource(framed)))
                return true;

            _log.Warn("Schránka je nepřístupná, obrázek nebyl zkopírován.");
            Status = ClipboardHelper.InaccessibleMessage;
        }
        catch (Exception ex)
        {
            _log.Error("Kopírování do schránky se nezdařilo.", ex);
            Status = $"Kopírování do schránky se nezdařilo: {ex.Message}";
        }
        return false;
    }

    /// <summary>Navržená cesta pro uložení; obrázek ze schránky dostane název podle času.</summary>
    private string SuggestedOutputPath()
    {
        var source = CurrentPath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"schranka_{_clipboardStamp}.png");
        return ImagePipeline.GetOutputPath(source, ActiveProfile);
    }

    /// <summary>Ořízne, zmenší a (je-li zapnuto) otevře Analog Efex.</summary>
    [RelayCommand(CanExecute = nameof(CanCrop))]
    private async Task CropAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        IsCancellable = true;
        var profile = ActiveProfile;
        Status = profile.NikEnabled ? "Čekám na Analog Efex – upravte a klikněte na Save…" : "Ořezávám…";
        try
        {
            IExternalEditor? editor = profile.NikEnabled
                ? new NikRunner(NikPlugin.AnalogEfexPro2, Settings.NikExePath, _log)
                : null;
            var edited = await new ImagePipeline(editor, _log).PrepareAsync(_photo!, profile, CropRect, _cts.Token);
            if (edited is null)
            {
                Status = "Analog Efex zavřen bez uložení – ořez zrušen, upravte výřez a zkuste znovu.";
                return;
            }

            _edited?.Dispose();
            _edited = edited;
            IsCropped = true;
            Status = "Hotovo. Upravte rámeček a klikněte na Uložit.";
        }
        catch (OperationCanceledException)
        {
            Status = "Zrušeno.";
        }
        catch (Exception ex)
        {
            _log.Error("Ořez se nezdařil.", ex);
            Status = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
            IsCancellable = false;
        }
    }

    private bool CanCrop() => HasPhoto && !IsCropped && !IsBusy;

    /// <summary>Uloží podle profilu (výstupní složka + přípona názvu).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveTo(SuggestedOutputPath());

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveAs()
    {
        var suggested = SuggestedOutputPath();
        var dialog = new SaveFileDialog
        {
            Title = "Uložit jako",
            Filter = ActiveProfile.OutputFormat == OutputFormat.Png ? "PNG|*.png" : "JPEG|*.jpg",
            DefaultExt = ActiveProfile.OutputFormat == OutputFormat.Png ? ".png" : ".jpg",
            InitialDirectory = Path.GetDirectoryName(suggested),
            FileName = Path.GetFileName(suggested),
        };
        if (dialog.ShowDialog() == true)
            SaveTo(dialog.FileName);
    }

    private bool CanSave() => IsCropped && !IsBusy;

    private void SaveTo(string path)
    {
        try
        {
            new ImagePipeline(null, _log).SaveFramed(_edited!, ActiveProfile, path);
            var saved = $"Uloženo: {path}";
            Status = saved;
            if (Settings.CopyToClipboardOnSave)
                Status = TryCopyToClipboard() ? $"{saved} (zkopírováno i do schránky)" : $"{saved} – {Status}";
            if (_openedFromCommandLine && Settings.CloseWhenDone)
                CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error($"Uložení se nezdařilo: {path}", ex);
            Status = $"Uložení se nezdařilo: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsCancellable))]
    private void Cancel() => _cts?.Cancel();

    public void SaveSettings() => _settingsService.Save(Settings);

    // ---- náhled ----

    private void Hook(Profile profile)
    {
        profile.PropertyChanged += OnProfileChanged;
        profile.Signature.PropertyChanged += (_, _) => SchedulePreview();
        foreach (var line in profile.Lines)
            line.PropertyChanged += (_, _) => SchedulePreview();
        profile.Lines.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (FrameLine line in e.NewItems)
                    line.PropertyChanged += (_, _) => SchedulePreview();
            SchedulePreview();
        };
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Profile.Name) && sender == ActiveProfile)
            Settings.ActiveProfile = ActiveProfile.Name;
        if (e.PropertyName == nameof(Profile.NikEnabled))
            OnPropertyChanged(nameof(NikPathWarning));
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        _previewTimer?.Stop();
        _previewTimer?.Start();
    }

    private void RenderPreview()
    {
        // při ořezu se rámeček nezobrazuje
        if (ShowCrop)
            return;

        try
        {
            var profile = ActiveProfile;
            using var placeholder = _edited is null ? CreatePlaceholder(profile.TargetSize > 0 ? profile.TargetSize : 900) : null;
            using var framed = FrameRenderer.Render(_edited ?? placeholder!, profile);
            PreviewImage = ImageIO.ToBitmapSource(framed);
        }
        catch (Exception ex)
        {
            _log.Error("Náhled nelze vykreslit.", ex);
        }
    }

    /// <summary>Zástupná fotka (tmavý přechod), dokud není nic oříznuto.</summary>
    private static SKBitmap CreatePlaceholder(int size)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size),
                [SKColor.Parse("#4a3b2a"), SKColor.Parse("#c9a46a")], SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, size, size, paint);
        return bitmap;
    }

    private void AddLogLine(string line)
    {
        _logLines.Add(line);
        if (_logLines.Count > 300)
            _logLines.RemoveAt(0);
        LogText = string.Join("\n", _logLines);
    }
}
