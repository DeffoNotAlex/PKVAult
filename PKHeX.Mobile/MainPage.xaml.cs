using System.ComponentModel;
using System.Runtime.CompilerServices;
using PKHeX.Core;
using PKHeX.Mobile.Pages;
using PKHeX.Mobile.Services;
using PKHeX.Mobile.Theme;

namespace PKHeX.Mobile;

public partial class MainPage : ContentPage
{
    private readonly ISecondaryDisplay _secondary;
    private readonly SaveDirectoryService _dirService = new();
    private readonly IFileService _fileService = new FileService();
    private List<SaveCardViewModel> _saveCards = [];

    // Focus zones: 0 = save list, 1 = action bar
    private int _focusSection;
    private int _cardCursor = -1;

    // Action bar sub-focus: 0 = primary button, 1–4 = tiles (Search, Gifts, Export, Bank)
    private int _actionCursor;

    private Border[] _actionTiles = [];
    private SaveEntry? _selectedSave;
    private bool _gpNavigating;

    public MainPage(ISecondaryDisplay secondary)
    {
        _secondary = secondary;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        _secondary.Show();
        _actionTiles = [Tile_Search, Tile_Gifts, Tile_Export, Tile_Bank];

        // On dual-screen: keep only the hero panel on the primary display;
        // the save list + action bar is rendered on the bottom secondary screen.
        bool dual = _secondary.IsAvailable;
        BottomPanel.IsVisible = !dual;

        _ = RefreshSavesAsync();
        UpdateActionHighlight();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    private async Task RefreshSavesAsync()
    {
        var entries = await _dirService.ScanAllAsync();
        _saveCards = entries.Select(e => new SaveCardViewModel(e)).ToList();
        SaveCardsList.ItemsSource = _saveCards;
        SaveCountLabel.Text = $"{_saveCards.Count} save{(_saveCards.Count != 1 ? "s" : "")}";

        if (_saveCards.Count > 0 && _cardCursor < 0)
            _cardCursor = 0;

        // Restore active save highlight when returning from GamePage
        if (App.ActiveSaveFileUri is { Length: > 0 } uri)
        {
            var active = _saveCards.FirstOrDefault(c => c.Entry.FileUri == uri);
            if (active != null)
            {
                active.IsLoaded = true;
                _selectedSave = active.Entry;
                _cardCursor = _saveCards.IndexOf(active);
                _gpNavigating = true;
                SaveCardsList.SelectedItem = active;
                _gpNavigating = false;
            }
        }

        // Show hero preview for the focused card
        UpdateHeroPreview();
        UpdateActionHighlight();
        _secondary.ShowMainMenu(_saveCards.Cast<object>().ToList(), _cardCursor);
    }

    // ── Hero preview ──────────────────────────────────────────────────────────

    private void UpdateHeroPreview()
    {
        if (_cardCursor < 0 || _cardCursor >= _saveCards.Count)
        {
            HeroPreview.IsVisible = false;
            return;
        }

        var card = _saveCards[_cardCursor];
        HeroPreview.IsVisible = true;

        // Badge
        HeroBadgeText.Text = card.GameShortName;
        HeroBadgeGrad0.Color = card.GameColorDark;
        HeroBadgeGrad1.Color = card.GameColorLight;

        // Trainer info
        HeroTrainerName.Text = card.TrainerName;
        HeroGameLabel.Text = card.VersionLabel;
        HeroTID.Text = card.Entry.TrainerID.ToString();
        HeroBoxes.Text = card.Entry.BoxCount.ToString();
        HeroPlaytime.Text = card.Entry.PlayTime;

        // Active pill
        HeroActivePill.IsVisible = card.IsLoaded;
    }

    // ── Save loading ─────────────────────────────────────────────────────────

    private void LoadSave(SaveEntry entry)
    {
        if (SaveUtil.TryGetSaveFile(entry.RawData, out var sav))
        {
            foreach (var card in _saveCards)
                card.IsLoaded = false;

            App.ActiveSave = sav;
            App.ActiveSaveFileName = entry.FileName;
            App.ActiveSaveFileUri = entry.FileUri;
            _selectedSave = entry;

            var active = _saveCards.FirstOrDefault(c => c.Entry == entry);
            if (active != null) active.IsLoaded = true;

            UpdateHeroPreview();
            UpdateActionHighlight();
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private void ActivatePrimaryButton()
    {
        if (_selectedSave is not null)
            _ = Shell.Current.GoToAsync(nameof(GamePage));
    }

    private void ActivateTile(int tile)
    {
        switch (tile)
        {
            case 0: // Search
                if (_selectedSave is not null)
                    _ = Shell.Current.GoToAsync(nameof(DatabasePage));
                break;
            case 1: // Gifts
                _ = Shell.Current.GoToAsync(nameof(MysteryGiftDBPage));
                break;
            case 2: // Export
                if (_selectedSave is not null)
                    _ = ExportSaveAsync();
                break;
            case 3: // Bank
                if (_selectedSave is not null)
                    _ = Shell.Current.GoToAsync(nameof(BankPage));
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
            await DisplayAlert("Export Error", ex.Message, "OK");
        }
    }

    // ── Touch handlers ───────────────────────────────────────────────────────

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

    private void OnOpenBoxesTapped(object? sender, EventArgs e) => ActivatePrimaryButton();
    private void OnSearchTapped(object? sender, EventArgs e) => ActivateTile(0);
    private void OnGiftsTapped(object? sender, EventArgs e) => ActivateTile(1);
    private void OnExportTapped(object? sender, EventArgs e) => ActivateTile(2);
    private void OnBankTapped(object? sender, EventArgs e) => ActivateTile(3);

    // ── Action bar highlight ─────────────────────────────────────────────────

    private void UpdateActionHighlight()
    {
        var focusBg = Color.FromArgb("#182242");
        var focusStroke = Color.FromArgb("#3B8BFF");
        var normalBg = Color.FromArgb("#131B35");
        var normalStroke = Color.FromArgb("#0DFFFFFF");

        // Primary button focus
        bool primaryFocused = _focusSection == 1 && _actionCursor == 0;
        Btn_OpenBoxes.Stroke = primaryFocused ? focusStroke : Colors.Transparent;

        // Tiles
        for (int i = 0; i < _actionTiles.Length; i++)
        {
            bool focused = _focusSection == 1 && _actionCursor == i + 1;
            _actionTiles[i].BackgroundColor = focused ? focusBg : normalBg;
            _actionTiles[i].Stroke = focused ? focusStroke : Color.FromArgb("#0DFFFFFF");
        }

        _secondary.UpdateMainMenuState(_cardCursor, _focusSection, _actionCursor);
    }

    // ── Gamepad ──────────────────────────────────────────────────────────────

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
            case Android.Views.Keycode.DpadLeft:
                MoveLeft(); break;
            case Android.Views.Keycode.DpadRight:
                MoveRight(); break;
            case Android.Views.Keycode.ButtonA:
                OnAPressed(); break;
            case Android.Views.Keycode.ButtonX:
                ActivateTile(0); break; // Quick jump to Search
            case Android.Views.Keycode.ButtonL1:
                CycleZone(-1); break;
            case Android.Views.Keycode.ButtonR1:
                CycleZone(1); break;
            case Android.Views.Keycode.ButtonSelect:
            case Android.Views.Keycode.ButtonStart:
                _ = Shell.Current.GoToAsync(nameof(SettingsPage)); break;
        }
    }
#endif

    private void CycleZone(int dir)
    {
        if (dir < 0 && _focusSection == 1)
        {
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
        else if (dir > 0 && _focusSection == 0)
        {
            _focusSection = 1;
            _actionCursor = 0;
            _gpNavigating = true;
            SaveCardsList.SelectedItem = null;
            _gpNavigating = false;
        }
        UpdateActionHighlight();
    }

    private void MoveUp()
    {
        if (_focusSection == 1)
        {
            if (_actionCursor > 0)
            {
                _actionCursor = 0;
            }
            else
            {
                CycleZone(-1);
                return;
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
            UpdateHeroPreview();
        }
        UpdateActionHighlight();
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
                UpdateHeroPreview();
            }
            else
            {
                CycleZone(1);
                return;
            }
        }
        else
        {
            if (_actionCursor == 0)
                _actionCursor = 1;
        }
        UpdateActionHighlight();
    }

    private void MoveLeft()
    {
        if (_focusSection == 1 && _actionCursor > 1)
        {
            _actionCursor--;
            UpdateActionHighlight();
        }
    }

    private void MoveRight()
    {
        if (_focusSection == 1 && _actionCursor >= 1 && _actionCursor < 4)
        {
            _actionCursor++;
            UpdateActionHighlight();
        }
    }

    private void OnAPressed()
    {
        if (_focusSection == 0)
        {
            if (_cardCursor >= 0 && _cardCursor < _saveCards.Count)
            {
                var card = _saveCards[_cardCursor];
                if (card.IsLoaded)
                {
                    ActivatePrimaryButton();
                }
                else
                {
                    LoadSave(card.Entry);
                }
            }
        }
        else
        {
            if (_actionCursor == 0)
                ActivatePrimaryButton();
            else
                ActivateTile(_actionCursor - 1);
        }
    }

    // ── SaveCardViewModel ────────────────────────────────────────────────────

    internal sealed class SaveCardViewModel : INotifyPropertyChanged
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

        public Color GameColorDark { get; }
        public Color GameColorLight { get; }

        public ImageSource? IconSource { get; }
        public bool HasIcon { get; }
        public bool HasNoIcon { get; }

        public SaveCardViewModel(SaveEntry entry)
        {
            Entry = entry;
            TrainerName = entry.TrainerName;
            VersionLabel = $"Pokémon {entry.Version}  ·  Gen {entry.Generation}";
            DetailLine = $"TID {entry.TrainerID}  ·  {entry.BoxCount} boxes  ·  {entry.PlayTime}";
            GameShortName = GetGameShortName(entry.Version);

            var (dark, light) = GameColors.Get(entry.Version);
            GameColorDark = Color.FromUint((uint)((dark.Alpha << 24) | (dark.Red << 16) | (dark.Green << 8) | dark.Blue));
            GameColorLight = Color.FromUint((uint)((light.Alpha << 24) | (light.Red << 16) | (light.Green << 8) | light.Blue));

            var iconFile = GetIconFileName(entry.Version);
            if (iconFile != null)
            {
                IconSource = ImageSource.FromStream(
                    ct => FileSystem.OpenAppPackageFileAsync($"gameicons/{iconFile}").WaitAsync(ct));
                HasIcon = true;
                HasNoIcon = false;
            }
            else
            {
                HasIcon = false;
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
            _               => null,
        };

        internal static string GetGameShortName(GameVersion v) => v switch
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
    }
}
