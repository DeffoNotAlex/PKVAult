using System.ComponentModel;
using System.Runtime.CompilerServices;
using PKHeX.Core;
using PKHeX.Mobile.Pages;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

public partial class MainPage : ContentPage
{
    private readonly SaveDirectoryService _dirService = new();
    private readonly IFileService _fileService = new FileService();
    private List<SaveCardViewModel> _saveCards = [];
    private int _focusSection = 0; // 0 = cards, 1 = actions
    private int _cardCursor = -1;
    private int _actionCursor = 0;
    private Border[] _actionRows = [];
    private SaveEntry? _selectedSave;
    private bool _gpNavigating;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        _actionRows = [Row_Load, Row_Search, Row_Gifts, Row_Export, Row_Settings];
        _ = RefreshSavesAsync();
        UpdateHighlight();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
    }

    private async Task RefreshSavesAsync()
    {
        var entries = await _dirService.ScanAllAsync();
        _saveCards = entries.Select(e => new SaveCardViewModel(e)).ToList();
        SaveCardsList.ItemsSource = _saveCards;
        if (_saveCards.Count > 0 && _cardCursor < 0)
            _cardCursor = 0;
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        var focusedBg     = Color.FromArgb("#182845");
        var focusedStroke = Color.FromArgb("#4F80FF");
        var normalBg      = Color.FromArgb("#111827");
        var dimmedBg      = Color.FromArgb("#0D1520");

        for (int i = 0; i < _actionRows.Length; i++)
        {
            bool focused = _focusSection == 1 && i == _actionCursor;
            // Grey out Row_Load if no save selected
            bool dimmed = i == 0 && _selectedSave is null;
            _actionRows[i].BackgroundColor = focused ? focusedBg : (dimmed ? dimmedBg : normalBg);
            _actionRows[i].Stroke          = focused ? focusedStroke : Colors.Transparent;
        }
    }

    private void OnSaveCardSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_gpNavigating) return;
        if (e.CurrentSelection.Count == 0) return;
        if (e.CurrentSelection[0] is SaveCardViewModel vm)
        {
            _cardCursor = _saveCards.IndexOf(vm);
            LoadSave(vm.Entry);
        }
    }

    private void LoadSave(SaveEntry entry)
    {
        if (SaveUtil.TryGetSaveFile(entry.RawData, out var sav))
        {
            // Clear previous active indicator
            foreach (var card in _saveCards)
                card.IsLoaded = false;

            App.ActiveSave = sav;
            App.ActiveSaveFileName = entry.FileName;
            App.ActiveSaveFileUri  = entry.FileUri;
            _selectedSave = entry;

            // Mark new active card
            var active = _saveCards.FirstOrDefault(c => c.Entry == entry);
            if (active != null) active.IsLoaded = true;

            UpdateHighlight();
        }
    }

    private void ActivateAction(int row)
    {
        switch (row)
        {
            case 0:
                if (_selectedSave is not null)
                    _ = Shell.Current.GoToAsync(nameof(GamePage));
                break;
            case 1:
                if (_selectedSave is not null)
                    _ = Shell.Current.GoToAsync(nameof(DatabasePage));
                break;
            case 2:
                _ = Shell.Current.GoToAsync(nameof(MysteryGiftDBPage));
                break;
            case 3:
                if (_selectedSave is not null)
                    _ = ExportSaveAsync();
                break;
            case 4:
                _ = Shell.Current.GoToAsync(nameof(SettingsPage));
                break;
        }
    }

    private async Task ExportSaveAsync()
    {
        if (App.ActiveSave is null || _selectedSave is null) return;
        try
        {
            var data = App.ActiveSave.Write().ToArray();
            await _fileService.ExportFileAsync(data, _selectedSave.FileName);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export Error", ex.Message, "OK");
        }
    }

    private void OnLoadBoxesClicked(object sender, EventArgs e)  => ActivateAction(0);
    private void OnSearchClicked(object sender, EventArgs e)     => ActivateAction(1);
    private void OnGiftsClicked(object sender, EventArgs e)      => ActivateAction(2);
    private void OnExportClicked(object sender, EventArgs e)     => ActivateAction(3);
    private void OnSettingsClicked(object sender, EventArgs e)   => ActivateAction(4);

#if ANDROID
    private void OnGamepadKey(Android.Views.Keycode keyCode, Android.Views.KeyEventActions action)
    {
        if (action != Android.Views.KeyEventActions.Down) return;
        MainThread.BeginInvokeOnMainThread(() => HandleGamepadKey(keyCode));
    }

    private void HandleGamepadKey(Android.Views.Keycode keyCode)
    {
        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:
                MoveUp(); break;

            case Android.Views.Keycode.DpadDown:
                MoveDown(); break;

            case Android.Views.Keycode.ButtonA:
                OnAPressed(); break;

            case Android.Views.Keycode.ButtonSelect:
                ActivateAction(4); break; // Settings
        }
    }
#endif

    private void MoveUp()
    {
        if (_focusSection == 1)
        {
            if (_actionCursor > 0)
                _actionCursor--;
            else
            {
                // Jump back to cards section
                _focusSection = 0;
                if (_saveCards.Count > 0 && _cardCursor < 0)
                    _cardCursor = 0;
                if (_saveCards.Count > 0)
                {
                    _gpNavigating = true;
                    SaveCardsList.SelectedItem = _saveCards[_cardCursor];
                    _gpNavigating = false;
                    SaveCardsList.ScrollTo(_cardCursor, -1, ScrollToPosition.MakeVisible, false);
                }
            }
        }
        else
        {
            if (_saveCards.Count == 0) return;
            _cardCursor = Math.Max(0, (_cardCursor < 0 ? 0 : _cardCursor) - 1);
            _gpNavigating = true;
            SaveCardsList.SelectedItem = _saveCards[_cardCursor];
            _gpNavigating = false;
            SaveCardsList.ScrollTo(_cardCursor, -1, ScrollToPosition.MakeVisible, false);
        }
        UpdateHighlight();
    }

    private void MoveDown()
    {
        if (_focusSection == 0)
        {
            if (_saveCards.Count > 0 && _cardCursor < _saveCards.Count - 1)
            {
                _cardCursor++;
                _gpNavigating = true;
                SaveCardsList.SelectedItem = _saveCards[_cardCursor];
                _gpNavigating = false;
                SaveCardsList.ScrollTo(_cardCursor, -1, ScrollToPosition.MakeVisible, false);
            }
            else
            {
                // Jump to actions section — clear card highlight
                _focusSection = 1;
                _actionCursor = 0;
                _gpNavigating = true;
                SaveCardsList.SelectedItem = null;
                _gpNavigating = false;
            }
        }
        else
        {
            _actionCursor = Math.Min(_actionRows.Length - 1, _actionCursor + 1);
        }
        UpdateHighlight();
    }

    private void OnAPressed()
    {
        if (_focusSection == 0)
        {
            // Load selected card
            if (_cardCursor >= 0 && _cardCursor < _saveCards.Count)
                LoadSave(_saveCards[_cardCursor].Entry);
        }
        else
        {
            ActivateAction(_actionCursor);
        }
    }

    // ── SaveCardViewModel ──────────────────────────────────────────────────

    private sealed class SaveCardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool _isLoaded;
        public bool IsLoaded
        {
            get => _isLoaded;
            set { if (_isLoaded == value) return; _isLoaded = value; OnPropertyChanged(); }
        }

        public SaveEntry Entry { get; }
        public string TrainerName { get; }
        public string VersionLabel { get; }
        public string DetailLine { get; }
        public string GameShortName { get; }
        public Color GameColor { get; }
        public ImageSource? IconSource { get; }
        public bool HasIcon { get; }
        public bool HasNoIcon { get; }

        public SaveCardViewModel(SaveEntry entry)
        {
            Entry = entry;
            TrainerName = entry.TrainerName;
            VersionLabel = $"Pokémon {entry.Version}  ·  Gen {entry.Generation}";
            DetailLine   = $"TID {entry.TrainerID}  ·  {entry.BoxCount} boxes  ·  {entry.PlayTime}";
            GameShortName = GetGameShortName(entry.Version);
            GameColor     = GetGameColor(entry.Version);

            var iconFile = GetIconFileName(entry.Version);
            if (iconFile != null)
            {
                IconSource = ImageSource.FromStream(
                    ct => FileSystem.OpenAppPackageFileAsync($"gameicons/{iconFile}").WaitAsync(ct));
                HasIcon   = true;
                HasNoIcon = false;
            }
            else
            {
                HasIcon   = false;
                HasNoIcon = true;
            }
        }

        private static string? GetIconFileName(GameVersion v) => v switch
        {
            GameVersion.RD  => "red_vc.png",
            GameVersion.BU  => "blue_vc.png",
            GameVersion.GN  => "green_vc.png",
            GameVersion.YW  => "yellow_vc.png",
            GameVersion.GD  => "gold_vc.png",
            GameVersion.SI  => "silver_vc.png",
            GameVersion.C   => "crystal.png",
            GameVersion.D   => "diamond.png",
            GameVersion.P   => "pearl.png",
            GameVersion.R   => "ruby.png",
            GameVersion.S   => "sapphire.png",
            GameVersion.E   => "emerald.png",
            GameVersion.FR  => "fire_red.png",
            GameVersion.LG  => "leaf_green.png",
            GameVersion.Pt  => "platinum.png",
            GameVersion.HG  => "heartgold.png",
            GameVersion.SS  => "soulsilver.png",
            GameVersion.B   => "black.png",
            GameVersion.W   => "white.png",
            GameVersion.B2  => "black2.png",
            GameVersion.W2  => "white2.png",
            GameVersion.X   => "x.png",
            GameVersion.Y   => "y.png",
            GameVersion.OR  => "omega_ruby.png",
            GameVersion.AS  => "alpha_sapphire.png",
            GameVersion.SN  => "sun.png",
            GameVersion.MN  => "moon.png",
            GameVersion.US  => "ultra_sun.png",
            GameVersion.UM  => "ultra_moon.png",
            GameVersion.GP  => "lets_go_pikachu.jpg",
            GameVersion.GE  => "lets_go_eevee.jpg",
            GameVersion.SW  => "sword.png",
            GameVersion.SH  => "shield.png",
            GameVersion.BD  => "brilliant_diamond.jpg",
            GameVersion.SP  => "shining_pearl.jpg",
            GameVersion.PLA => "legends_arceus.jpg",
            GameVersion.SL  => "scarlet.jpg",
            GameVersion.VL  => "violet.jpg",
            _               => null,   // GBA (FR/LG/R/S/E) — use colored badge
        };

        private static string GetGameShortName(GameVersion v) => v switch
        {
            GameVersion.RD  => "Red",
            GameVersion.BU  => "Blue",
            GameVersion.GN  => "Green",
            GameVersion.YW  => "Yel",
            GameVersion.GD  => "Gold",
            GameVersion.SI  => "Slvr",
            GameVersion.C   => "Crys",
            GameVersion.R   => "Ruby",
            GameVersion.S   => "Saph",
            GameVersion.E   => "Em",
            GameVersion.FR  => "FR",
            GameVersion.LG  => "LG",
            GameVersion.D   => "D",
            GameVersion.P   => "P",
            GameVersion.Pt  => "Pt",
            GameVersion.HG  => "HG",
            GameVersion.SS  => "SS",
            GameVersion.B   => "BLK",
            GameVersion.W   => "WHT",
            GameVersion.B2  => "BLK2",
            GameVersion.W2  => "WHT2",
            GameVersion.X   => "X",
            GameVersion.Y   => "Y",
            GameVersion.OR  => "OR",
            GameVersion.AS  => "AS",
            GameVersion.SN  => "Sun",
            GameVersion.MN  => "Moon",
            GameVersion.US  => "US",
            GameVersion.UM  => "UM",
            GameVersion.GP  => "LGP",
            GameVersion.GE  => "LGE",
            GameVersion.SW  => "Sw",
            GameVersion.SH  => "Sh",
            GameVersion.PLA => "PLA",
            GameVersion.BD  => "BD",
            GameVersion.SP  => "SP",
            GameVersion.SL  => "SL",
            GameVersion.VL  => "VL",
            _ => v.ToString()[..Math.Min(4, v.ToString().Length)],
        };

        private static Color GetGameColor(GameVersion v) => v switch
        {
            GameVersion.RD or GameVersion.FR => Color.FromArgb("#C0392B"),
            GameVersion.GN or GameVersion.LG => Color.FromArgb("#27AE60"),
            GameVersion.BU or GameVersion.GD or GameVersion.HG => Color.FromArgb("#D4AC0D"),
            GameVersion.SI or GameVersion.SS => Color.FromArgb("#85929E"),
            GameVersion.C   => Color.FromArgb("#1ABC9C"),
            GameVersion.YW  => Color.FromArgb("#E67E22"),
            GameVersion.R or GameVersion.OR => Color.FromArgb("#C0392B"),
            GameVersion.S or GameVersion.AS => Color.FromArgb("#2980B9"),
            GameVersion.E   => Color.FromArgb("#27AE60"),
            GameVersion.D or GameVersion.BD => Color.FromArgb("#5DADE2"),
            GameVersion.P or GameVersion.SP => Color.FromArgb("#F1948A"),
            GameVersion.Pt  => Color.FromArgb("#717D7E"),
            GameVersion.B or GameVersion.B2 => Color.FromArgb("#1C2833"),
            GameVersion.W or GameVersion.W2 => Color.FromArgb("#9EAAB5"),
            GameVersion.X   => Color.FromArgb("#1A5276"),
            GameVersion.Y   => Color.FromArgb("#922B21"),
            GameVersion.SN or GameVersion.US or GameVersion.GP => Color.FromArgb("#E67E22"),
            GameVersion.MN or GameVersion.UM or GameVersion.GE => Color.FromArgb("#8E44AD"),
            GameVersion.SW  => Color.FromArgb("#2471A3"),
            GameVersion.SH  => Color.FromArgb("#A93226"),
            GameVersion.PLA => Color.FromArgb("#5D6D7E"),
            GameVersion.SL  => Color.FromArgb("#C0392B"),
            GameVersion.VL  => Color.FromArgb("#7D3C98"),
            _ => Color.FromArgb("#2C3E50"),
        };
    }
}
