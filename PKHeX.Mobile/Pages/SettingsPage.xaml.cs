using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

namespace PKHeX.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    public const string KeyLanguage      = "language";
    public const string KeyShinySprites  = "shiny_sprites";
    public const string KeyRadarAdaptive = "radar_adaptive";
    public const string KeyLegalityBadge = "legality_badge";

    private static readonly string[] LanguageCodes = ["ja", "en", "fr", "it", "de", "es", "es-419", "ko", "zh-Hans", "zh-Hant"];
    private static readonly string[] LanguageNames = ["日本語", "English", "Français", "Italiano", "Deutsch", "Español", "Español (LATAM)", "한국어", "中文 (简)", "中文 (繁)"];

    private bool _loading;
    private int _focusRow = 0;
    private Border[] _rows = [];

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var name in LanguageNames)
            LanguagePicker.Items.Add(name);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        HomeSpriteCacheService.BulkProgressChanged += OnBulkProgress;
        _loading = true;

        var lang = Preferences.Default.Get(KeyLanguage, "en");
        var idx = Array.IndexOf(LanguageCodes, lang);
        LanguagePicker.SelectedIndex = idx >= 0 ? idx : 1; // default English

        ShinySwitch.IsToggled         = Preferences.Default.Get(KeyShinySprites, true);
        RadarAdaptiveSwitch.IsToggled = Preferences.Default.Get(KeyRadarAdaptive, false);
        LegalitySwitch.IsToggled      = Preferences.Default.Get(KeyLegalityBadge, false);
        ThemeSwitch.IsToggled         = ThemeService.Current == PkTheme.Light;

        _loading = false;

        BuildRows();
        UpdateHighlight();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
        HomeSpriteCacheService.BulkProgressChanged -= OnBulkProgress;
    }

    private void BuildRows()
    {
        _rows = [Row_Language, Row_Shiny, Row_Radar, Row_Folders, Row_Legality, Row_Theme, Row_Sprites];
    }

    private void UpdateHighlight()
    {
        var focusedBg     = Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#EEF2FF" : "#182845");
        var focusedStroke = Color.FromArgb("#4F80FF");
        var normalBg      = Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#FFFFFF" : "#111827");

        for (int i = 0; i < _rows.Length; i++)
        {
            bool focused = i == _focusRow;
            _rows[i].BackgroundColor = focused ? focusedBg : normalBg;
            _rows[i].Stroke          = focused ? focusedStroke : Colors.Transparent;
        }
    }

    private void MoveFocus(int delta)
    {
        _focusRow = Math.Clamp(_focusRow + delta, 0, _rows.Length - 1);
        UpdateHighlight();
        ScrollRowIntoView(_focusRow);
    }

    private void ScrollRowIntoView(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Length) return;
        var row = _rows[rowIndex];
        // ScrollToAsync with the element handles viewport visibility automatically
        _ = RootScroll.ScrollToAsync(row, ScrollToPosition.MakeVisible, false);
    }

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
            case Android.Views.Keycode.ButtonB:
                _ = Shell.Current.GoToAsync(".."); break;

            case Android.Views.Keycode.DpadUp:
                MoveFocus(-1); break;

            case Android.Views.Keycode.DpadDown:
                MoveFocus(+1); break;

            case Android.Views.Keycode.DpadLeft:
                AdjustRow(-1); break;

            case Android.Views.Keycode.DpadRight:
                AdjustRow(+1); break;

            case Android.Views.Keycode.ButtonA:
                ActivateRow(); break;
        }
    }
#endif

    private void AdjustRow(int delta)
    {
        if (_focusRow == 0)
        {
            // Language picker
            int count = LanguagePicker.Items.Count;
            if (count == 0) return;
            LanguagePicker.SelectedIndex = Math.Clamp(LanguagePicker.SelectedIndex + delta, 0, count - 1);
        }
        // Row 1 (Shiny) — left/right does nothing for a toggle
    }

    private void ActivateRow()
    {
        switch (_focusRow)
        {
            case 0:
                LanguagePicker.Focus();
                break;
            case 1:
                ShinySwitch.IsToggled = !ShinySwitch.IsToggled;
                break;
            case 2:
                RadarAdaptiveSwitch.IsToggled = !RadarAdaptiveSwitch.IsToggled;
                break;
            case 3:
                _ = Shell.Current.GoToAsync(nameof(FolderManagerPage));
                break;
            case 4:
                LegalitySwitch.IsToggled = !LegalitySwitch.IsToggled;
                break;
            case 5:
                ThemeSwitch.IsToggled = !ThemeSwitch.IsToggled;
                break;
            case 6:
                StartBulkDownload();
                break;
        }
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        if (_loading || LanguagePicker.SelectedIndex < 0) return;
        var lang = LanguageCodes[LanguagePicker.SelectedIndex];
        Preferences.Default.Set(KeyLanguage, lang);
        GameInfo.CurrentLanguage = lang;
    }

    private void OnShinySwitchToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Default.Set(KeyShinySprites, e.Value);
        SpriteName.AllowShinySprite = e.Value;
    }

    private void OnRadarAdaptiveToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Default.Set(KeyRadarAdaptive, e.Value);
    }

    private void OnLegalitySwitchToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Default.Set(KeyLegalityBadge, e.Value);
    }

    private async void OnThemeSwitchToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        ThemeService.Apply(e.Value ? PkTheme.Light : PkTheme.Dark);
        UpdateHighlight();
        await DisplayAlertAsync("Theme changed", "Restart the app for full effect.", "OK");
    }

    // ── Sprite download ───────────────────────────────────────────────────────

    private void OnSpriteDownloadTapped(object? sender, EventArgs e) => StartBulkDownload();

    private void StartBulkDownload()
    {
        if (HomeSpriteCacheService.IsBulkDownloading) return;
        _ = HomeSpriteCacheService.BulkDownloadAsync();
        UpdateSpriteStatus();
    }

    private void OnBulkProgress(int done, int total)
    {
        SpriteRingCanvas.InvalidateSurface();
        UpdateSpriteStatus();
    }

    private void UpdateSpriteStatus()
    {
        var (done, total, failed) = HomeSpriteCacheService.BulkProgress;
        bool running = HomeSpriteCacheService.IsBulkDownloading;

        if (running)
        {
            SpriteStatusLabel.Text  = $"Downloading…  {done} / {total} sprites";
            SpriteRingLabel.Text    = $"{done}";
        }
        else if (total > 0)
        {
            string failNote = failed > 0 ? $"  ·  {failed} failed" : "";
            SpriteStatusLabel.Text  = $"Done — {done} sprites cached{failNote}";
            SpriteRingLabel.Text    = "✓";
        }
        else
        {
            SpriteStatusLabel.Text  = "~120 MB · caches HOME sprites for all Pokémon";
            SpriteRingLabel.Text    = "";
        }
    }

    private void OnSpriteRingPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var (done, total, _) = HomeSpriteCacheService.BulkProgress;
        bool running = HomeSpriteCacheService.IsBulkDownloading;
        float pct = total > 0 ? Math.Clamp((float)done / total, 0f, 1f) : 0f;

        float cx = e.Info.Width  / 2f;
        float cy = e.Info.Height / 2f;
        float r  = Math.Min(cx, cy) - 4f;

        bool light = ThemeService.Current == PkTheme.Light;

        // Track ring
        using var trackPaint = new SKPaint
        {
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 5f,
            Color       = light ? new SKColor(200, 210, 230) : new SKColor(40, 55, 80),
            IsAntialias = true,
        };
        canvas.DrawCircle(cx, cy, r, trackPaint);

        if (!running && total == 0)
        {
            // Idle — draw a subtle download arrow
            using var arrowPaint = new SKPaint { Color = new SKColor(100, 160, 220), IsAntialias = true, StrokeWidth = 2.5f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            float ax = cx, ay1 = cy - r * 0.45f, ay2 = cy + r * 0.2f;
            canvas.DrawLine(ax, ay1, ax, ay2, arrowPaint);
            using var path = new SKPath();
            path.MoveTo(ax - r * 0.3f, ay2 - r * 0.25f);
            path.LineTo(ax, ay2);
            path.LineTo(ax + r * 0.3f, ay2 - r * 0.25f);
            canvas.DrawPath(path, arrowPaint);
            return;
        }

        // Progress arc
        var arcColor = !running && total > 0
            ? new SKColor(52, 217, 144)   // complete — green
            : new SKColor(59, 139, 255);  // downloading — blue

        using var arcPaint = new SKPaint
        {
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 5f,
            Color       = arcColor,
            IsAntialias = true,
            StrokeCap   = SKStrokeCap.Round,
        };

        float sweep = pct * 360f;
        var rect = new SKRect(cx - r, cy - r, cx + r, cy + r);
        canvas.DrawArc(rect, -90f, sweep, false, arcPaint);
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply persisted settings on app startup.
    /// </summary>
    public static void ApplyOnStartup()
    {
        var lang = Preferences.Default.Get(KeyLanguage, "en");
        GameInfo.CurrentLanguage = lang;
        SpriteName.AllowShinySprite = Preferences.Default.Get(KeyShinySprites, true);
    }
}
