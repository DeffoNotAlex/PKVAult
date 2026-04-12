using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

#if ANDROID
using PKHeX.Mobile.Platforms.Android;
#endif

namespace PKHeX.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    public const string KeyLanguage      = "language";
    public const string KeyShinySprites    = "shiny_sprites";
    public const string KeyAnimated3D      = "animated_3d";
    public const string KeyRadarAdaptive   = "radar_adaptive";
    public const string KeyLegalityBadge   = "legality_badge";

    private static readonly string[] LanguageCodes = ["ja", "en", "fr", "it", "de", "es", "es-419", "ko", "zh-Hans", "zh-Hant"];
    private static readonly string[] LanguageNames = ["日本語", "English", "Français", "Italiano", "Deutsch", "Español", "Español (LATAM)", "한국어", "中文 (简)", "中文 (繁)"];

    private bool _loading;
    private int _focusRow = 0;
    private Border[] _rows = [];

    private readonly SaveDirectoryService _dirService = new();
#if ANDROID
    private readonly IDirectoryPicker _dirPicker = new AndroidDirectoryPicker();
#else
    private readonly IDirectoryPicker _dirPicker = new NullDirectoryPicker();
#endif

    // Download logs
    private readonly System.Text.StringBuilder _spriteLog = new();
    private readonly System.Text.StringBuilder _animLog   = new();
    private int _spriteLogLines;
    private int _animLogLines;
    private const int MaxLogLines = 200;

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
        GamepadRouter.KeyReceived -= OnGamepadKey;
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        HomeSpriteCacheService.BulkProgressChanged += OnBulkProgress;
        HomeSpriteCacheService.SlugCompleted       += OnSpriteSlugCompleted;
        SpriteCacheService.BulkProgressChanged     += OnAnimProgress;
        SpriteCacheService.SlugCompleted           += OnAnimSlugCompleted;
        _loading = true;

        var lang = Preferences.Default.Get(KeyLanguage, "en");
        var idx = Array.IndexOf(LanguageCodes, lang);
        LanguagePicker.SelectedIndex = idx >= 0 ? idx : 1; // default English

        ShinySwitch.IsToggled         = Preferences.Default.Get(KeyShinySprites, true);
        Animated3DSwitch.IsToggled    = Preferences.Default.Get(KeyAnimated3D, true);
        RadarAdaptiveSwitch.IsToggled = Preferences.Default.Get(KeyRadarAdaptive, false);
        LegalitySwitch.IsToggled      = Preferences.Default.Get(KeyLegalityBadge, false);
        ThemeSwitch.IsToggled         = ThemeService.Current == PkTheme.Light;

        _loading = false;

        BuildRows();
        UpdateHighlight();
        UpdateSpriteStatus();
        UpdateAnimSpriteStatus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
        HomeSpriteCacheService.BulkProgressChanged -= OnBulkProgress;
        HomeSpriteCacheService.SlugCompleted       -= OnSpriteSlugCompleted;
        SpriteCacheService.BulkProgressChanged     -= OnAnimProgress;
        SpriteCacheService.SlugCompleted           -= OnAnimSlugCompleted;
    }

    private void BuildRows()
    {
        _rows = [Row_Language, Row_Shiny, Row_Radar, Row_Folders, Row_Legality, Row_Theme, Row_Animated3D, Row_Sprites, Row_AnimSprites,
                 Row_EdenScan, Row_MelonDSScan, Row_AzaharScan, Row_RetroArchScan];
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
        switch (_focusRow)
        {
            case 0: // Language — left/right cycles through options
                int count = LanguagePicker.Items.Count;
                if (count == 0) return;
                LanguagePicker.SelectedIndex = Math.Clamp(LanguagePicker.SelectedIndex + delta, 0, count - 1);
                break;

            case 1: SetToggle(ShinySwitch,         delta); break;
            case 2: SetToggle(RadarAdaptiveSwitch, delta); break;
            case 4: SetToggle(LegalitySwitch,      delta); break;
            case 5: SetToggle(ThemeSwitch,         delta); break;
            case 6: SetToggle(Animated3DSwitch,    delta); break;
        }

        static void SetToggle(Switch sw, int delta)
        {
            // Right = on, Left = off
            if (delta > 0 && !sw.IsToggled) sw.IsToggled = true;
            else if (delta < 0 && sw.IsToggled) sw.IsToggled = false;
        }
    }

    private void ActivateRow()
    {
        switch (_focusRow)
        {
            case 0: // Language — A cycles forward (wraps)
                int count = LanguagePicker.Items.Count;
                if (count > 0)
                    LanguagePicker.SelectedIndex = (LanguagePicker.SelectedIndex + 1) % count;
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
                Animated3DSwitch.IsToggled = !Animated3DSwitch.IsToggled;
                break;
            case 7:
                StartBulkDownload();
                break;
            case 8:
                StartAnimBulkDownload();
                break;
            case 9:
                _ = FindEdenSavesAsync();
                break;
            case 10:
                _ = FindMelonDSSavesAsync();
                break;
            case 11:
                _ = FindAzaharSavesAsync();
                break;
            case 12:
                _ = FindRetroArchSavesAsync();
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

    private void OnAnimated3DSwitchToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Default.Set(KeyAnimated3D, e.Value);
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

    // ── HOME sprite download ──────────────────────────────────────────────────

    private void OnSpriteDownloadTapped(object? sender, EventArgs e) => StartBulkDownload();

    private void StartBulkDownload()
    {
        if (HomeSpriteCacheService.IsBulkDownloading) return;
        _spriteLog.Clear();
        _spriteLogLines  = 0;
        SpriteLogLabel.Text = "";
        SpriteLogPanel.IsVisible = false;
        _ = HomeSpriteCacheService.BulkDownloadAsync();
        UpdateSpriteStatus();
    }

    private void OnBulkProgress(int done, int total)
    {
        SpriteRingCanvas.InvalidateSurface();
        UpdateSpriteStatus();
    }

    private void OnSpriteSlugCompleted(string slug, bool success)
    {
        AppendLog(SpriteLogPanel, SpriteLogLabel, SpriteLogScroll,
                  _spriteLog, ref _spriteLogLines, slug, success);
    }

    private void UpdateSpriteStatus()
    {
        var (done, total, failed) = HomeSpriteCacheService.BulkProgress;
        bool running = HomeSpriteCacheService.IsBulkDownloading;

        if (running)
        {
            SpriteStatusLabel.Text = $"Downloading…  {done} / {total} sprites";
            SpriteRingLabel.Text   = $"{done}";
        }
        else if (total > 0)
        {
            string failNote = failed > 0 ? $"  ·  {failed} failed" : "";
            SpriteStatusLabel.Text = $"Done — {done} sprites cached{failNote}";
            SpriteRingLabel.Text   = "✓";
        }
        else
        {
            SpriteStatusLabel.Text = "~120 MB · caches HOME sprites for all Pokémon";
            SpriteRingLabel.Text   = "";
        }
    }

    private void OnSpriteRingPaint(object sender, SKPaintSurfaceEventArgs e)
        => PaintProgressRing(e, HomeSpriteCacheService.BulkProgress, HomeSpriteCacheService.IsBulkDownloading);

    // ── Animated sprite download ──────────────────────────────────────────────

    private void OnAnimSpriteDownloadTapped(object? sender, EventArgs e) => StartAnimBulkDownload();

    private void StartAnimBulkDownload()
    {
        if (SpriteCacheService.IsBulkDownloading) return;
        _animLog.Clear();
        _animLogLines  = 0;
        AnimLogLabel.Text = "";
        AnimLogPanel.IsVisible = false;
        _ = SpriteCacheService.BulkDownloadAllAsync();
        UpdateAnimSpriteStatus();
    }

    private void OnAnimProgress(int done, int total)
    {
        AnimRingCanvas.InvalidateSurface();
        UpdateAnimSpriteStatus();
    }

    private void OnAnimSlugCompleted(string slug, bool success)
    {
        AppendLog(AnimLogPanel, AnimLogLabel, AnimLogScroll,
                  _animLog, ref _animLogLines, slug, success);
    }

    private void UpdateAnimSpriteStatus()
    {
        var (done, total, failed) = SpriteCacheService.BulkProgress;
        bool running = SpriteCacheService.IsBulkDownloading;

        if (running)
        {
            AnimSpriteStatusLabel.Text = $"Downloading…  {done} / {total} sprites";
            AnimRingLabel.Text         = $"{done}";
        }
        else if (total > 0)
        {
            string failNote = failed > 0 ? $"  ·  {failed} failed" : "";
            AnimSpriteStatusLabel.Text = $"Done — {done} sprites cached{failNote}";
            AnimRingLabel.Text         = "✓";
        }
        else
        {
            AnimSpriteStatusLabel.Text = "~200 MB · caches animated GIFs for all Pokémon";
            AnimRingLabel.Text         = "";
        }
    }

    private void OnAnimRingPaint(object sender, SKPaintSurfaceEventArgs e)
        => PaintProgressRing(e, SpriteCacheService.BulkProgress, SpriteCacheService.IsBulkDownloading);

    // ── Shared log helper ─────────────────────────────────────────────────────

    private void AppendLog(Border panel, Label label, ScrollView scroll,
                           System.Text.StringBuilder sb, ref int lineCount,
                           string slug, bool success)
    {
        if (success) return;

        panel.IsVisible = true;

        // Trim oldest lines when cap is reached
        if (lineCount >= MaxLogLines)
        {
            var text  = sb.ToString();
            int cut   = 0;
            int found = 0;
            // Drop first MaxLogLines/4 lines
            int dropCount = MaxLogLines / 4;
            while (found < dropCount && cut < text.Length)
            {
                if (text[cut++] == '\n') found++;
            }
            sb.Clear();
            sb.Append(text.AsSpan(cut));
            lineCount -= found;
        }

        sb.Append("✗ ");
        sb.AppendLine(slug);
        lineCount++;

        label.Text = sb.ToString();
        _ = scroll.ScrollToAsync(0, double.MaxValue, false);
    }

    // ── Shared ring painter ───────────────────────────────────────────────────

    private static void PaintProgressRing(SKPaintSurfaceEventArgs e, (int Done, int Total, int Failed) progress, bool running)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var (done, total, _) = progress;
        float pct = total > 0 ? Math.Clamp((float)done / total, 0f, 1f) : 0f;

        float cx = e.Info.Width  / 2f;
        float cy = e.Info.Height / 2f;
        float r  = Math.Min(cx, cy) - 4f;

        bool light = ThemeService.Current == PkTheme.Light;

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

        var arcColor = !running && total > 0
            ? new SKColor(52, 217, 144)
            : new SKColor(59, 139, 255);

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

    // ── Emulator save finder ──────────────────────────────────────────────────

    private void OnFindEdenTapped(object? sender, EventArgs e)       => _ = FindEdenSavesAsync();
    private void OnFindMelonDSTapped(object? sender, EventArgs e)   => _ = FindMelonDSSavesAsync();
    private void OnFindAzaharTapped(object? sender, EventArgs e)    => _ = FindAzaharSavesAsync();
    private void OnFindRetroArchTapped(object? sender, EventArgs e) => _ = FindRetroArchSavesAsync();

    private async Task FindEdenSavesAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        EdenStatusLabel.Text      = "Scanning…";
        EdenStatusLabel.IsVisible = true;

        var found = await EmulatorSaveFinderService.ScanEdenAsync(uri);
        if (found.Count == 0)
        {
            EdenStatusLabel.Text = "No Pokémon saves found. Make sure you picked the eden folder.";
            return;
        }

        foreach (var (fileUri, _) in found)
            _dirService.AddFile(fileUri);

        var names = string.Join(", ", found.Select(f => f.GameName));
        EdenStatusLabel.Text = $"Added {found.Count} save{(found.Count == 1 ? "" : "s")}: {names}";
    }

    private async Task FindMelonDSSavesAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        _dirService.AddDirectory(uri);
        await DisplayAlertAsync("MelonDS", "Folder added. Your save files will appear on the home screen after a refresh.", "OK");
    }

    private async Task FindAzaharSavesAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        AzaharStatusLabel.Text      = "Scanning…";
        AzaharStatusLabel.IsVisible = true;

        var found = await EmulatorSaveFinderService.ScanAzaharAsync(uri);
        if (found.Count == 0)
        {
            AzaharStatusLabel.Text = "No Pokémon saves found. Make sure you picked the Azahar root folder (the one that contains sdmc/).";
            return;
        }

        foreach (var (fileUri, _) in found)
            _dirService.AddFile(fileUri);

        AzaharStatusLabel.Text = $"Added {found.Count} save{(found.Count == 1 ? "" : "s")}.";
    }

    private async Task FindRetroArchSavesAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        _dirService.AddDirectory(uri);
        await DisplayAlertAsync("RetroArch", "Folder added. Pokémon GBA/GBC saves (.srm) will appear on the home screen after a refresh.", "OK");
    }

    // ── Stub pickers for non-Android ─────────────────────────────────────────

    private sealed class NullDirectoryPicker : IDirectoryPicker
    {
        public Task<string?> PickDirectoryAsync() => Task.FromResult<string?>(null);
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
