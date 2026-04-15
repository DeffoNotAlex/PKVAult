using System.ComponentModel;
using System.Runtime.CompilerServices;
using PKHeX.Core;
using PKHeX.Mobile.Pages;
using PKHeX.Mobile.Services;
using PKHeX.Mobile.Theme;
using SkiaSharp;
using SkiaSharp.Views.Maui;

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
    private Image[]  _partyImages = [];
    private SaveEntry? _selectedSave;
    private bool _gpNavigating;
    private int  _lastHeroDisplayIndex = -2; // -2 = never shown

    // Floating card animation
    private IDispatcherTimer? _floatTimer;
    private DateTime          _floatStart = DateTime.UtcNow;

    // Hero game colors (used by Moiré canvas + glow/stroke accents)
    private Color    _heroColorLight  = Colors.Transparent;
    private Color    _heroColorDark   = Colors.Transparent;
    // Duotone Moiré — set for B2/W2, Empty otherwise
    private SKColor  _moireBase       = SKColor.Empty;
    private SKColor  _moireAccent     = SKColor.Empty;
    // Classic gradient (used when Moiré is disabled)
    private GradientStop? _heroTopStop;
    private GradientStop? _heroMidStop;

    // Moiré renderer — cached to avoid per-frame allocations
    private static readonly string   MoireChars       = " .,-~:;=!*#";
    private static readonly string[] MoireCharStrings = MoireChars.Select(c => c.ToString()).ToArray();
    private static readonly SKTypeface MoireTypeface  =
        SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default;

    // Hero cross-fade cancellation
    private CancellationTokenSource? _heroAnimCts;

    public MainPage(ISecondaryDisplay secondary)
    {
        _secondary = secondary;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (WelcomePage.ShouldShowWelcome())
        {
            await Shell.Current.GoToAsync(nameof(WelcomePage), false);
            return;
        }

#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        _secondary.Show();
        _actionTiles = [Tile_Search, Tile_Gifts, Tile_Export, Tile_Bank, Tile_Dex];
        _partyImages  = [Party0, Party1, Party2, Party3, Party4, Party5];

        // On dual-screen: hero panel fills the entire primary display (Row 0 = *),
        // and the save list + action bar moves to the secondary screen.
        bool dual = _secondary.IsAvailable;
        BottomPanel.IsVisible = !dual;
        RootGrid.RowDefinitions[0].Height = dual ? GridLength.Star : GridLength.Auto;
        RootGrid.RowDefinitions[1].Height = dual ? new GridLength(0) : GridLength.Star;

        if (App.RescanNeeded || App.LoadedSaves.Count == 0)
        {
            // Show cached list immediately if available, then rescan in background.
            if (App.LoadedSaves.Count > 0)
                ApplySaveEntries(App.LoadedSaves);
            _ = RefreshSavesAsync();
        }
        else
        {
            // Returning from GamePage — list is identical, just restore highlights.
            RestoreAfterGamePage();
        }
        UpdateActionHighlight();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
        ThemeService.ThemeChanged -= OnThemeChanged;
        _floatTimer?.Stop();
    }

    private void OnThemeChanged()
    {
        foreach (var card in _saveCards)
            card.RefreshTheme();
        UpdateActionHighlight();
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    private async Task RefreshSavesAsync()
    {
        var entries = await _dirService.ScanAllAsync();
        App.LoadedSaves = entries;
        App.RescanNeeded = false;
        ApplySaveEntries(entries);
    }

    private void ApplySaveEntries(List<SaveEntry> entries)
    {
        _saveCards = entries.Select(e => new SaveCardViewModel(e)).ToList();
        SaveCardsList.ItemsSource = _saveCards;
        SaveCountLabel.Text = $"{_saveCards.Count} save{(_saveCards.Count != 1 ? "s" : "")}";

        // Re-clamp cursor and re-apply IsCursor on fresh card objects
        if (_saveCards.Count > 0)
        {
            int clamped = Math.Max(0, Math.Min(_cardCursor < 0 ? 0 : _cardCursor, _saveCards.Count - 1));
            _cardCursor = -1; // force SetCardCursor to treat it as new
            SetCardCursor(clamped);
        }

        // Restore active save highlight when returning from GamePage
        if (App.ActiveSaveFileUri is { Length: > 0 } uri)
        {
            var active = _saveCards.FirstOrDefault(c => c.Entry.FileUri == uri);
            if (active != null)
            {
                active.IsLoaded = true;
                _selectedSave = active.Entry;
                SetCardCursor(_saveCards.IndexOf(active));
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

    /// <summary>
    /// Lightweight return from GamePage: existing save cards stay bound,
    /// only the active-save highlight and cursor are restored.
    /// </summary>
    private void RestoreAfterGamePage()
    {
        if (_saveCards.Count == 0) return;

        StartFloatAnimation();

        // Clear any stale IsLoaded flags from the previous session
        foreach (var c in _saveCards) c.IsLoaded = false;

        if (App.ActiveSaveFileUri is { Length: > 0 } uri)
        {
            var active = _saveCards.FirstOrDefault(c => c.Entry.FileUri == uri);
            if (active != null)
            {
                active.IsLoaded = true;
                _selectedSave   = active.Entry;
                int idx = _saveCards.IndexOf(active);
                SetCardCursor(idx);
                _gpNavigating = true;
                SaveCardsList.SelectedItem = active;
                _gpNavigating = false;
                SaveCardsList.ScrollTo(idx, -1, ScrollToPosition.MakeVisible, false);
            }
        }

        UpdateHeroPreview();
        UpdateActionHighlight();
        _secondary.ShowMainMenu(_saveCards.Cast<object>().ToList(), _cardCursor);
    }

    // ── Hero preview ──────────────────────────────────────────────────────────

    private async void UpdateHeroPreview()
    {
        // Cancel any in-progress cross-fade
        _heroAnimCts?.Cancel();
        _heroAnimCts = new CancellationTokenSource();
        var cts = _heroAnimCts;

        // If a save is actively selected (pressed A), hero stays locked on it
        int displayIndex = _cardCursor;
        if (_selectedSave is not null)
        {
            int sel = _saveCards.FindIndex(c => c.Entry.FileUri == _selectedSave.FileUri);
            if (sel >= 0) displayIndex = sel;
        }

        // Nothing changed — skip the whole update (key for cursor navigation while locked)
        if (displayIndex == _lastHeroDisplayIndex) return;
        _lastHeroDisplayIndex = displayIndex;

        if (displayIndex < 0 || displayIndex >= _saveCards.Count)
        {
            if (HeroPreview.IsVisible)
            {
                await HeroCard.FadeToAsync(0, 100);
                if (cts.IsCancellationRequested) return;
                HeroPreview.IsVisible    = false;
                HeroGridCanvas.IsVisible = false;
                HeroCard.Opacity         = 1;
            }
            HeroEmptyState.IsVisible  = true;
            PartyStrip.IsVisible      = false;
            HeroPanel.Background      = null; // revert to XAML BackgroundColor
            _lastHeroDisplayIndex     = -1;
            StopFloatAnimation();
            return;
        }

        bool wasVisible = HeroPreview.IsVisible;

        // Only cross-fade when the displayed save actually changes
        if (wasVisible && _selectedSave is null)
        {
            await HeroCard.FadeToAsync(0, 100);
            if (cts.IsCancellationRequested) return;
        }
        else if (!wasVisible)
        {
            // no-op: will fade in below
        }

        var card = _saveCards[displayIndex];
        HeroEmptyState.IsVisible = false;
        HeroPreview.IsVisible    = true;
        HeroGridCanvas.IsVisible = true;

        // Game icon: real image when available, gradient badge fallback
        bool hasIcon = card.HasIcon;
        HeroIconGrad.IsVisible        = !hasIcon;
        HeroIconImgBorder.IsVisible   = hasIcon;
        HeroBgIconGrad.IsVisible      = !hasIcon;
        HeroBgIconImgBorder.IsVisible = hasIcon;

        if (hasIcon)
        {
            HeroIconImg.Source   = card.IconSource;
            HeroBgIconImg.Source = card.IconSource;
        }
        else
        {
            HeroBadgeText.Text   = card.GameShortName;
            HeroBgBadgeText.Text = card.GameShortName;
            HeroBadgeGrad0.Color = card.GameColorDark;
            HeroBadgeGrad1.Color = card.GameColorLight;
            HeroBgGrad0.Color    = card.GameColorDark;
            HeroBgGrad1.Color    = card.GameColorLight;
        }

        // Store game colors for the Moiré canvas and accent elements
        _heroColorLight = card.GameColorLight;
        _heroColorDark  = card.GameColorDark;

        bool moireEnabled = Preferences.Default.Get(Pages.SettingsPage.KeyMoireBg, true);
        HeroGridCanvas.IsVisible = moireEnabled;

        if (!moireEnabled)
        {
            // Classic breathing gradient
            if (_heroTopStop is null)
            {
                _heroTopStop = new GradientStop(card.GameColorLight.WithAlpha(55), 0f);
                _heroMidStop = new GradientStop(card.GameColorDark.WithAlpha(20), 0.6f);
                HeroPanel.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1),
                    GradientStops = [_heroTopStop, _heroMidStop, new GradientStop(Colors.Transparent, 1f)],
                };
            }
            else
            {
                _heroTopStop.Color  = card.GameColorLight.WithAlpha(55);
                _heroMidStop!.Color = card.GameColorDark.WithAlpha(20);
            }
        }
        else
        {
            // Clear gradient if switching back to Moiré
            if (_heroTopStop is not null)
            {
                HeroPanel.Background = null;
                _heroTopStop = null;
                _heroMidStop = null;
            }
        }

        // Duotone Moiré (B2/W2 only)
        if (card.MoireAccent is { } accent)
        {
            // Base: near-black for B2, near-white for W2 (derived from dark/light game color)
            var dark  = card.GameColorDark;
            bool isBlackGame = dark.Red < 0.3f && dark.Green < 0.3f && dark.Blue < 0.3f;
            _moireBase   = isBlackGame
                ? new SKColor(28, 28, 36)       // near-black base for B2
                : new SKColor(240, 240, 248);    // near-white base for W2
            _moireAccent = accent;
        }
        else
        {
            _moireBase   = SKColor.Empty;
            _moireAccent = SKColor.Empty;
        }

        // Accent glow + card stroke from game color
        HeroGlow0.Color = card.GameColorLight.WithAlpha(64);
        HeroCard.Stroke = new SolidColorBrush(card.GameColorLight.WithAlpha(80));

        // Trainer info
        HeroTrainerName.Text = card.TrainerName;
        HeroGameLabel.Text   = card.VersionLabel;
        HeroTID.Text         = card.Entry.TrainerID.ToString();
        HeroBoxes.Text       = card.Entry.BoxCount.ToString();
        HeroPlaytime.Text    = card.Entry.PlayTime;

        // Active pill
        HeroActivePill.IsVisible = card.IsLoaded;

        // Party strip
        _ = LoadPartyAsync(card.Entry);

        StartFloatAnimation();

        // Fade card in
        HeroCard.Opacity = 0;
        await HeroCard.FadeToAsync(1, 160, Easing.CubicOut);
    }

    private async Task LoadPartyAsync(SaveEntry entry)
    {
        PartyStrip.IsVisible = false;
        foreach (var img in _partyImages)
            img.IsVisible = false;

        var sav = await Task.Run(() =>
        {
            var data = entry.RawData.ToArray(); // copy — SwishCrypto decrypts in-place
            PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var s);
            return s;
        });
        if (sav is null || !sav.HasParty) return;

        var party = sav.PartyData.Where(p => p.Species > 0).ToArray();
        if (party.Length == 0) return;

        for (int i = 0; i < _partyImages.Length; i++)
        {
            if (i < party.Length)
            {
                var pk  = party[i];
                var url = HomeSpriteCacheService.GetHomeUrl((ushort)pk.Species, pk.Form, pk.IsShiny);
                _partyImages[i].Source    = new UriImageSource { Uri = new Uri(url), CacheValidity = TimeSpan.FromDays(30) };
                _partyImages[i].IsVisible = true;
            }
        }

        PartyStrip.IsVisible = true;
    }

    // ── Floating card animation ──────────────────────────────────────────────

    private void StartFloatAnimation()
    {
        if (_floatTimer is not null && _floatTimer.IsRunning) return;
        _floatStart = DateTime.UtcNow;
        _floatTimer ??= Dispatcher.CreateTimer();
        _floatTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 fps
        _floatTimer.Tick -= OnFloatTick;
        _floatTimer.Tick += OnFloatTick;
        _floatTimer.Start();
    }

    private void StopFloatAnimation()
    {
        _floatTimer?.Stop();
        if (HeroCard is not null)
        {
            HeroCard.TranslationY = 0;
            HeroCard.RotationX    = 0;
            HeroCard.RotationY    = 0;
        }
    }

    private void OnFloatTick(object? sender, EventArgs e)
    {
        double t = (DateTime.UtcNow - _floatStart).TotalSeconds;

        // Two independent sine waves for organic feel
        double phaseA = t * 2 * Math.PI / 3.5;   // 3.5s bob cycle
        double phaseB = t * 2 * Math.PI / 4.8 + Math.PI / 3; // offset tilt

        HeroCard.TranslationY = 5.0  * Math.Sin(phaseA);
        HeroCard.RotationX    = 3.5  * Math.Sin(phaseA);
        HeroCard.RotationY    = 2.5  * Math.Sin(phaseB);

        if (_heroTopStop is not null && _heroColorLight != Colors.Transparent)
        {
            double breath  = 0.5 + 0.5 * Math.Sin(t * Math.PI * 2 / 4.0);
            byte topAlpha  = (byte)(22 + breath * 90);
            byte midAlpha  = (byte)(8  + breath * 28);
            _heroTopStop.Color  = _heroColorLight.WithAlpha(topAlpha);
            _heroMidStop!.Color = _heroColorDark.WithAlpha(midAlpha);
        }

        HeroGridCanvas.InvalidateSurface();
    }

    // ── Moiré ASCII background ───────────────────────────────────────────────

    private void OnHeroGridPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (_heroColorLight == Colors.Transparent) return;

        float pw = e.Info.Width;
        float ph = e.Info.Height;

        // Character cell size in canvas pixels
        const float CW   = 14f;
        const float CH   = 23f;
        const float freq = 0.3f;
        const float thr  = 0.15f;
        const float orb  = 0.3f;

        float t = (float)(DateTime.UtcNow - _floatStart).TotalSeconds;

        int cols = (int)(pw / CW) + 2;
        int rows = (int)(ph / CH) + 2;

        // Three orbiting wave centers (ported from MoireCode.html)
        float nx = cols * 0.5f + MathF.Cos(t * 0.30f)       * cols * orb;
        float ny = rows * 0.5f + MathF.Sin(t * 0.40f)       * rows * orb;
        float ix = cols * 0.5f + MathF.Cos(t * 0.37f + 2f)  * cols * (orb * 0.83f);
        float iy = rows * 0.5f + MathF.Sin(t * 0.29f + 2f)  * rows * (orb * 1.17f);
        float sx = cols * 0.5f + MathF.Sin(t * 0.23f + 4f)  * cols * (orb * 1.17f);
        float sy = rows * 0.5f + MathF.Cos(t * 0.31f + 4f)  * rows * (orb * 0.83f);

        float f1 = freq;
        float f2 = freq * 1.033f;
        float f3 = freq * 0.967f;

        // Resolve game color for this theme
        bool isDark = ThemeService.Current == PkTheme.Dark;
        var mc = isDark ? _heroColorLight : _heroColorDark;
        byte cr = (byte)(mc.Red   * 255);
        byte cg = (byte)(mc.Green * 255);
        byte cb = (byte)(mc.Blue  * 255);

        // Dark theme: boost colors that are too dark to see against a dark background
        float r = cr / 255f, g = cg / 255f, b = cb / 255f;
        if (isDark)
        {
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            if (lum < 0.12f)
            {
                float boost = (0.12f - lum) / 0.12f;
                r += (1f - r) * boost;
                g += (1f - g) * boost;
                b += (1f - b) * boost;
            }
        }
        var charBase = new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));

        using var font  = new SKFont(MoireTypeface, CW);
        using var paint = new SKPaint { IsAntialias = true };

        for (int row = 0; row < rows; row++)
        {
            float fy = row * CH + CW; // baseline offset
            for (int col = 0; col < cols; col++)
            {
                float fx = col * 0.55f;

                float pb = fx - nx * 0.55f, hb = row - ny;
                float gb = fx - ix * 0.55f, qb = row - iy;
                float vb = fx - sx * 0.55f, yb = row - sy;

                float d1 = MathF.Sqrt(pb * pb + hb * hb);
                float d2 = MathF.Sqrt(gb * gb + qb * qb);
                float d3 = MathF.Sqrt(vb * vb + yb * yb);

                float C = MathF.Sin(d1 * f1 + t)
                        + MathF.Sin(d2 * f2 - t * 0.7f)
                        + MathF.Sin(d3 * f3 + t * 0.5f);
                C = (C + 3f) / 6f;

                if (C < thr || C > 1f - thr) continue;

                float w = 1f - MathF.Abs(C - 0.5f) * 2f;
                int ci = Math.Min(MoireCharStrings.Length - 1, (int)(w * MoireCharStrings.Length));
                if (MoireChars[ci] == ' ') continue;

                float alpha = isDark ? 0.30f + w * 0.70f : 0.75f + w * 0.25f;
                SKColor color;
                if (_moireAccent != SKColor.Empty)
                {
                    // Duotone: lerp from base (black/white) to accent color
                    color = new SKColor(
                        (byte)(_moireBase.Red   + (_moireAccent.Red   - _moireBase.Red)   * w),
                        (byte)(_moireBase.Green + (_moireAccent.Green - _moireBase.Green) * w),
                        (byte)(_moireBase.Blue  + (_moireAccent.Blue  - _moireBase.Blue)  * w));
                }
                else
                {
                    color = charBase;
                }
                paint.Color = color.WithAlpha((byte)(alpha * 255));
                canvas.DrawText(MoireCharStrings[ci], col * CW, fy, font, paint);
            }
        }
    }

    // ── Save loading ─────────────────────────────────────────────────────────

    private async Task LoadSaveAsync(SaveEntry entry)
    {
        try
        {
            var sav = await Task.Run(() =>
            {
                var data = entry.RawData.ToArray(); // copy — SwishCrypto decrypts in-place
                SaveUtil.TryGetSaveFile(data, out var s);
                return s;
            });

            if (sav is null)
            {
                await DisplayAlertAsync("Load Failed",
                    $"Could not parse \"{entry.FileName}\" ({entry.RawData.Length:N0} bytes). Unsupported format.",
                    "OK");
                return;
            }

            foreach (var card in _saveCards)
                card.IsLoaded = false;

            App.ActiveSave = sav;
            App.ActiveSaveFileName = entry.FileName;
            App.ActiveSaveFileUri = entry.FileUri;
            _selectedSave = entry;

            var active = _saveCards.FirstOrDefault(c => c.Entry == entry);
            if (active != null) active.IsLoaded = true;

            // Force UpdateHeroPreview to do a full redraw so the Active pill appears.
            // Without this reset it skips the update because the display index didn't change.
            _lastHeroDisplayIndex = -2;
            UpdateHeroPreview();
            UpdateActionHighlight();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Load Error", $"{ex.GetType().Name}: {ex.Message}", "OK");
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
                _ = Shell.Current.GoToAsync(nameof(BankViewPage));
                break;
            case 4: // Dex
                _ = Shell.Current.GoToAsync(nameof(DexPage));
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

    // ── Touch handlers ───────────────────────────────────────────────────────

    private void OnSaveCardSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_gpNavigating) return;
        if (e.CurrentSelection.Count == 0) return;
        if (e.CurrentSelection[0] is SaveCardViewModel vm)
        {
            SetCardCursor(_saveCards.IndexOf(vm));
            _ = LoadSaveAsync(vm.Entry);
        }
    }

    private void OnHeroCardTapped(object? sender, TappedEventArgs e) => ActivatePrimaryButton();
    private void OnOpenBoxesTapped(object? sender, EventArgs e) => ActivatePrimaryButton();
    private void OnSearchTapped(object? sender, EventArgs e) => ActivateTile(0);
    private void OnGiftsTapped(object? sender, EventArgs e) => ActivateTile(1);
    private void OnExportTapped(object? sender, EventArgs e) => ActivateTile(2);
    private void OnBankTapped(object? sender, EventArgs e) => ActivateTile(3);
    private void OnDexTapped(object? sender, EventArgs e) => ActivateTile(4);

    // ── Action bar highlight ─────────────────────────────────────────────────

    private void SetCardCursor(int newIndex)
    {
        if (_cardCursor >= 0 && _cardCursor < _saveCards.Count)
            _saveCards[_cardCursor].IsCursor = false;
        _cardCursor = newIndex;
        if (_cardCursor >= 0 && _cardCursor < _saveCards.Count)
            _saveCards[_cardCursor].IsCursor = true;
    }

    private void UpdateActionHighlight()
    {
        bool light = ThemeService.Current == PkTheme.Light;
        var focusBg      = Color.FromArgb(light ? "#EEF2FF" : "#182242");
        var focusStroke  = Color.FromArgb("#3B8BFF");
        var normalBg     = Color.FromArgb(light ? "#FFFFFF" : "#131B35");
        var normalStroke = Color.FromArgb(light ? "#E0E4EC" : "#0DFFFFFF");

        // Primary button — enforce pastel blue background (overrides any theme reset)
        Btn_OpenBoxes.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = [
                new GradientStop(Color.FromArgb("#EAF5FF"), 0f),
                new GradientStop(Color.FromArgb("#CCE8FF"), 1f),
            ],
        };
        bool primaryFocused = _focusSection == 1 && _actionCursor == 0;
        Btn_OpenBoxes.Stroke          = primaryFocused ? Color.FromArgb("#5AAAD0") : Colors.Transparent;
        Btn_OpenBoxes.StrokeThickness = primaryFocused ? 2.5 : 1.5;

        // Tiles
        for (int i = 0; i < _actionTiles.Length; i++)
        {
            bool focused = _focusSection == 1 && _actionCursor == i + 1;
            _actionTiles[i].BackgroundColor = focused ? focusBg : normalBg;
            _actionTiles[i].Stroke          = focused ? focusStroke : normalStroke;
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
                _ = OnAPressed(); break;
            case Android.Views.Keycode.ButtonX:
                ActivateTile(0); break; // Quick jump to Search
            case Android.Views.Keycode.ButtonL1:
                CycleZone(-1); break;
            case Android.Views.Keycode.ButtonR1:
                CycleZone(1); break;
            case Android.Views.Keycode.ButtonSelect:
            case Android.Views.Keycode.ButtonStart:
                _ = Shell.Current.GoToAsync(nameof(SettingsPage)); break;
            case (Android.Views.Keycode)107: // KEYCODE_BUTTON_THUMBR — right stick click
                _ = RefreshSavesAsync(); break;
        }
    }
#endif

    private void CycleZone(int dir)
    {
        if (dir < 0 && _focusSection == 1)
        {
            _focusSection = 0;
            if (_saveCards.Count > 0 && _cardCursor < 0)
                SetCardCursor(0);
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

    private static void Haptic() { try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { } }

    private void MoveUp()
    {
        Haptic();
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
            SetCardCursor(Math.Max(0, (_cardCursor < 0 ? 0 : _cardCursor) - 1));
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
        Haptic();
        if (_focusSection == 0)
        {
            if (_saveCards.Count > 0 && _cardCursor < _saveCards.Count - 1)
            {
                SetCardCursor(_cardCursor + 1);
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
        Haptic();
        if (_focusSection == 1 && _actionCursor > 1)
        {
            _actionCursor--;
            UpdateActionHighlight();
        }
    }

    private void MoveRight()
    {
        Haptic();
        if (_focusSection == 1 && _actionCursor >= 1 && _actionCursor < 5)
        {
            _actionCursor++;
            UpdateActionHighlight();
        }
    }

    private async Task OnAPressed()
    {
        Haptic();
        if (_focusSection == 0)
        {
            if (_cardCursor >= 0 && _cardCursor < _saveCards.Count)
            {
                var card = _saveCards[_cardCursor];
                if (card.IsLoaded)
                    ActivatePrimaryButton();
                else
                    await LoadSaveAsync(card.Entry);
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

        private static Color ColNormal    => Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#FFFFFF" : "#131B35");
        private static Color ColCursor    => Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#EEF2FF" : "#152040");
        private static Color ColLoaded    => Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#E8F0FF" : "#1C2850");
        private static Color StrokeNone   => Colors.Transparent;
        private static Color StrokeCursor => Color.FromArgb("#5CA0FF");
        private static Color StrokeLoaded => Color.FromArgb("#3B8BFF");

        private bool _isLoaded;
        public bool IsLoaded
        {
            get => _isLoaded;
            set { if (_isLoaded == value) return; _isLoaded = value; OnPropertyChanged(); RefreshCardVisuals(); }
        }

        private bool _isCursor;
        public bool IsCursor
        {
            get => _isCursor;
            set { if (_isCursor == value) return; _isCursor = value; OnPropertyChanged(); RefreshCardVisuals(); }
        }

        private Color _cardBackground = ColNormal;
        public Color CardBackground { get => _cardBackground; private set { _cardBackground = value; OnPropertyChanged(); } }

        private Color _cardStroke = StrokeNone;
        public Color CardStroke { get => _cardStroke; private set { _cardStroke = value; OnPropertyChanged(); } }

        private double _cardStrokeThickness = 1.5;
        public double CardStrokeThickness { get => _cardStrokeThickness; private set { _cardStrokeThickness = value; OnPropertyChanged(); } }

        public void RefreshTheme() => RefreshCardVisuals();

        private void RefreshCardVisuals()
        {
            if (_isLoaded)
            {
                CardBackground      = ColLoaded;
                CardStroke          = StrokeLoaded;
                CardStrokeThickness = 2;
            }
            else if (_isCursor)
            {
                CardBackground      = ColCursor;
                CardStroke          = StrokeCursor;
                CardStrokeThickness = 1.5;
            }
            else
            {
                CardBackground      = ColNormal;
                CardStroke          = StrokeNone;
                CardStrokeThickness = 1.5;
            }
            OnPropertyChanged(nameof(TextPrimary));
            OnPropertyChanged(nameof(TextSecondary));
            OnPropertyChanged(nameof(TextDim));
        }

        public Color TextPrimary   => Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#0D1117" : "#EDF0FF");
        public Color TextSecondary => Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#4A5568" : "#8892B5");
        public Color TextDim       => Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#9AA5B4" : "#3D4A6E");

        public SaveEntry Entry { get; }
        public string TrainerName { get; }
        public string VersionLabel { get; }
        public string DetailLine { get; }
        public string LastModifiedLabel { get; }
        public string GameShortName { get; }

        public Color  GameColorDark { get; }
        public Color  GameColorLight { get; }
        public SKColor? MoireAccent { get; }

        public ImageSource? IconSource { get; }
        public bool HasIcon { get; }
        public bool HasNoIcon { get; }

        public SaveCardViewModel(SaveEntry entry)
        {
            Entry = entry;
            TrainerName = entry.TrainerName;
            VersionLabel = $"Pokémon {entry.Version}  ·  Gen {entry.Generation}";
            DetailLine = $"TID {entry.TrainerID}  ·  {entry.BoxCount} boxes  ·  {entry.PlayTime}";
            LastModifiedLabel = FormatRelativeTime(entry.LastModified);
            GameShortName = GetGameShortName(entry.Version);

            var (dark, light) = GameColors.Get(entry.Version);
            MoireAccent = GameColors.GetAccent(entry.Version);
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

        private static string FormatRelativeTime(DateTimeOffset dt)
        {
            var diff = DateTimeOffset.UtcNow - dt;
            if (diff.TotalMinutes < 1)  return "just now";
            if (diff.TotalHours   < 1)  return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays    < 1)  return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays    < 7)  return $"{(int)diff.TotalDays}d ago";
            return dt.LocalDateTime.ToString("MMM d");
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
            GameVersion.HG   => "heartgold.png",
            GameVersion.SS   => "soulsilver.png",
            GameVersion.HGSS => "heartgold.png", // fallback: save has no Pokémon to probe
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
            GameVersion.ZA  => "legends_za.png",
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
