using System.Collections.ObjectModel;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

#if ANDROID
using PKHeX.Mobile.Platforms.Android;
#endif

namespace PKHeX.Mobile.Pages;

/// <summary>
/// First-run welcome wizard.
/// On dual-screen: top screen shows preview, bottom screen shows interactive controls.
/// On single-screen: both preview and controls are shown stacked.
/// </summary>
public partial class WelcomePage : ContentPage
{
    private readonly ISecondaryDisplay _secondary;

    private int _step;
    private bool _isDualScreen;

    private readonly ObservableCollection<FoundSaveItem> _foundSaves = [];
    private int _foundSaveCount;

    // Theme selection — tracks which was last chosen for canvas ring
    private PkTheme _chosenTheme = ThemeService.Current;

    private readonly SaveDirectoryService _dirService = new();
#if ANDROID
    private readonly IDirectoryPicker _dirPicker = new AndroidDirectoryPicker();
#else
    private readonly IDirectoryPicker _dirPicker = new NullDirectoryPicker();
#endif

    // ─────────────────────────────────────────────────────────────────────────
    //  Static helper
    // ─────────────────────────────────────────────────────────────────────────

    public static bool ShouldShowWelcome()
        => !Preferences.Default.Get("onboarding_complete", false)
        || Preferences.Default.Get(SettingsPage.KeyAlwaysShowWelcome, false);

    // ─────────────────────────────────────────────────────────────────────────
    //  Construction
    // ─────────────────────────────────────────────────────────────────────────

    public WelcomePage(ISecondaryDisplay secondary)
    {
        _secondary = secondary;
        InitializeComponent();
        FoundSavesList.ItemsSource = _foundSaves;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Page lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _step = 0;

        _isDualScreen = _secondary.IsAvailable;
        SingleScreenControls.IsVisible = !_isDualScreen;

        if (_isDualScreen)
        {
            _secondary.Show();
            _secondary.ShowWelcomeStep(0, OnWelcomeEvent);
        }

        ApplyStep(0, animate: false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_isDualScreen)
            _secondary.HideWelcome();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Event bus from bottom screen
    // ─────────────────────────────────────────────────────────────────────────

    private void OnWelcomeEvent(string action)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            switch (action)
            {
                case "next":
                    await AdvanceStepAsync();
                    break;

                case "skip":
                    await AdvanceStepAsync();
                    break;

                case "finish":
                    await FinishAsync();
                    break;

                case "theme:dark":
                    _chosenTheme = PkTheme.Dark;
                    ThemeService.Apply(PkTheme.Dark);
                    ThemePreviewCanvas.InvalidateSurface();
                    break;

                case "theme:light":
                    _chosenTheme = PkTheme.Light;
                    ThemeService.Apply(PkTheme.Light);
                    ThemePreviewCanvas.InvalidateSurface();
                    break;

                case "eden":
                    await FindEdenAsync();
                    break;

                case "azahar":
                    await FindAzaharAsync();
                    break;

                case "melonds":
                    await FindMelonDSAsync();
                    break;

                case "retroarch":
                    await FindRetroArchAsync();
                    break;

                case "manual":
                    await FindManualAsync();
                    break;
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step transitions
    // ─────────────────────────────────────────────────────────────────────────

    private async Task AdvanceStepAsync()
    {
        if (_step >= 2) return;
        await ApplyStepAnimatedAsync(_step + 1);
    }

    private async Task ApplyStepAnimatedAsync(int newStep)
    {
        // Get current visible preview
        View currentPreview = _step switch
        {
            0 => Preview0,
            1 => Preview1,
            _ => Preview2,
        };

        // Animate out
        var outTask1 = currentPreview.FadeTo(0, 200);
        var outTask2 = currentPreview.TranslateTo(-80, 0, 200, Easing.CubicIn);
        await Task.WhenAll(outTask1, outTask2);

        _step = newStep;

        View nextPreview = _step switch
        {
            0 => Preview0,
            1 => Preview1,
            _ => Preview2,
        };

        // Prepare new preview off-screen (right)
        nextPreview.TranslationX = 80;
        nextPreview.Opacity      = 0;

        ApplyStep(newStep, animate: false);

        // Animate in
        var inTask1 = nextPreview.FadeTo(1, 250);
        var inTask2 = nextPreview.TranslateTo(0, 0, 250, Easing.CubicOut);
        await Task.WhenAll(inTask1, inTask2);
    }

    private void ApplyStep(int step, bool animate)
    {
        Preview0.IsVisible = step == 0;
        Preview1.IsVisible = step == 1;
        Preview2.IsVisible = step == 2;

        UpdateDots(step);
        UpdateStepSubtitle(step);

        if (!_isDualScreen)
            UpdateSingleScreenControls(step);

        if (_isDualScreen)
            _secondary.ShowWelcomeStep(step, OnWelcomeEvent);

        if (step == 2)
        {
            UpdateFinalLabel();
            SuccessCanvas.InvalidateSurface();
        }
    }

    private void UpdateDots(int step)
    {
        var activeFill   = new SolidColorBrush(Color.FromArgb("#3B8BFF"));
        var inactiveFill = new SolidColorBrush(Color.FromArgb(
            ThemeService.Current == PkTheme.Light ? "#9CA3AF" : "#6B7280"));

        Dot0.Fill = step == 0 ? activeFill : inactiveFill;
        Dot1.Fill = step == 1 ? activeFill : inactiveFill;
        Dot2.Fill = step == 2 ? activeFill : inactiveFill;
    }

    private void UpdateStepSubtitle(int step)
    {
        StepSubtitleLabel.Text = step switch
        {
            0 => "Step 1 of 3 — Choose your look",
            1 => "Step 2 of 3 — Connect your saves",
            _ => "Step 3 of 3 — Done!",
        };
    }

    private void UpdateFinalLabel()
    {
        FinalFoundLabel.Text = _foundSaveCount > 0
            ? $"Found {_foundSaveCount} save{(_foundSaveCount == 1 ? "" : "s")} ready to edit."
            : "Ready to edit your Pokémon!";
    }

    private void UpdateSingleScreenControls(int step)
    {
        SingleStep0Controls.IsVisible = step == 0;
        SingleStep1Controls.IsVisible = step == 1;
        SingleStep2Controls.IsVisible = step == 2;
    }

    private async Task FinishAsync()
    {
        Preferences.Default.Set("onboarding_complete", true);
        await Shell.Current.GoToAsync("..", false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SKCanvas painters
    // ─────────────────────────────────────────────────────────────────────────

    private void OnThemePreviewPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float w = e.Info.Width;
        float h = e.Info.Height;
        float gap   = w * 0.04f;
        float cardW = (w - gap * 3) / 2f;
        float cardH = h * 0.85f;
        float cardY = (h - cardH) / 2f;

        // Dark card (left)
        float leftX = gap;
        DrawThemeMockup(canvas, new SKRect(leftX, cardY, leftX + cardW, cardY + cardH),
                        darkCard: true, selected: _chosenTheme == PkTheme.Dark);

        // Light card (right)
        float rightX = gap * 2 + cardW;
        DrawThemeMockup(canvas, new SKRect(rightX, cardY, rightX + cardW, cardY + cardH),
                        darkCard: false, selected: _chosenTheme == PkTheme.Light);
    }

    private static void DrawThemeMockup(SKCanvas canvas, SKRect rect, bool darkCard, bool selected)
    {
        float radius = 16f;

        // Card background
        var bg = darkCard ? new SKColor(17, 24, 39) : new SKColor(248, 249, 250);
        using var bgPaint = new SKPaint { Color = bg, IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, bgPaint);

        // Selection ring
        var ringColor = selected ? SKColor.Parse("#4F80FF") : new SKColor(100, 100, 120, 60);
        using var ringPaint = new SKPaint
        {
            Color = ringColor, Style = SKPaintStyle.Stroke,
            StrokeWidth = selected ? 3f : 1.5f, IsAntialias = true,
        };
        canvas.DrawRoundRect(rect, radius, radius, ringPaint);

        // Header stripe
        var stripe = darkCard ? new SKColor(31, 41, 55) : new SKColor(229, 234, 243);
        float stripeH = rect.Height * 0.14f;
        using var stripePaint = new SKPaint { Color = stripe, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + stripeH),
                             radius, radius, stripePaint);
        canvas.DrawRect(new SKRect(rect.Left, rect.Top + stripeH / 2f, rect.Right, rect.Top + stripeH),
                        stripePaint);

        // Fake save cards
        float cardMargin = rect.Width * 0.08f;
        float cardInnerW = rect.Width - cardMargin * 2;
        float fakeH      = rect.Height * 0.10f;
        float fakeRadius = 5f;
        var   fakeColors = darkCard
            ? new[] { new SKColor(31, 41, 55), new SKColor(37, 50, 70), new SKColor(28, 38, 60) }
            : new[] { new SKColor(255, 255, 255), new SKColor(242, 245, 252), new SKColor(238, 242, 250) };
        var   accentColors = new[] { SKColor.Parse("#4F80FF"), SKColor.Parse("#34D990"), SKColor.Parse("#FF9F43") };

        float startY = rect.Top + stripeH + rect.Height * 0.04f;
        for (int i = 0; i < 3; i++)
        {
            float y = startY + i * (fakeH + rect.Height * 0.03f);
            var fakeRect = new SKRect(rect.Left + cardMargin, y,
                                      rect.Left + cardMargin + cardInnerW, y + fakeH);
            using var fakePaint = new SKPaint { Color = fakeColors[i], IsAntialias = true };
            canvas.DrawRoundRect(fakeRect, fakeRadius, fakeRadius, fakePaint);

            // Accent dot
            using var dotPaint = new SKPaint { Color = accentColors[i], IsAntialias = true };
            canvas.DrawCircle(fakeRect.Left + 8f, fakeRect.MidY, 3.5f, dotPaint);
        }

        // Label
        var labelColor = darkCard ? new SKColor(200, 210, 230) : new SKColor(60, 70, 90);
        using var labelPaint = new SKPaint
        {
            Color   = labelColor, IsAntialias = true,
            TextSize = rect.Width * 0.13f,
        };
        using var tf = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        labelPaint.Typeface = tf;

        string label = darkCard ? "Dark" : "Light";
        float textW  = labelPaint.MeasureText(label);
        canvas.DrawText(label, rect.MidX - textW / 2f, rect.Bottom - rect.Height * 0.06f, labelPaint);
    }

    private void OnSuccessPreviewPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float cx = e.Info.Width  / 2f;
        float cy = e.Info.Height / 2f;
        float r  = Math.Min(cx, cy) * 0.75f;

        // Outer glow ring
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(52, 217, 144, 50),
            IsAntialias   = true,
            ImageFilter   = SKImageFilter.CreateBlur(18, 18),
        };
        canvas.DrawCircle(cx, cy, r, glowPaint);

        // Circle background
        using var circlePaint = new SKPaint { Color = new SKColor(52, 217, 144, 40), IsAntialias = true };
        canvas.DrawCircle(cx, cy, r, circlePaint);

        // Circle border
        using var strokePaint = new SKPaint
        {
            Color = new SKColor(52, 217, 144), Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f, IsAntialias = true,
        };
        canvas.DrawCircle(cx, cy, r, strokePaint);

        // Checkmark
        using var checkPaint = new SKPaint
        {
            Color = new SKColor(52, 217, 144), Style = SKPaintStyle.Stroke,
            StrokeWidth = r * 0.12f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };
        float arm = r * 0.42f;
        using var path = new SKPath();
        path.MoveTo(cx - arm,         cy);
        path.LineTo(cx - arm * 0.2f, cy + arm * 0.7f);
        path.LineTo(cx + arm,         cy - arm * 0.5f);
        canvas.DrawPath(path, checkPaint);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Single-screen tap handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void OnSingleDarkTapped(object? sender, EventArgs e)
        => OnWelcomeEvent("theme:dark");

    private void OnSingleLightTapped(object? sender, EventArgs e)
        => OnWelcomeEvent("theme:light");

    private void OnSingleNextTapped(object? sender, EventArgs e)
        => OnWelcomeEvent("next");

    private void OnSingleSkipTapped(object? sender, EventArgs e)
        => OnWelcomeEvent("skip");

    private void OnSingleGetStartedTapped(object? sender, EventArgs e)
        => OnWelcomeEvent("finish");

    // ─────────────────────────────────────────────────────────────────────────
    //  Emulator scanning
    // ─────────────────────────────────────────────────────────────────────────

    private async Task FindEdenAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        var found = await EmulatorSaveFinderService.ScanEdenAsync(uri);
        if (found.Count == 0) return;

        _dirService.AddEdenRoot(uri);
        App.RescanNeeded = true;

        foreach (var (fileUri, gameName) in found)
            AddFoundSave(gameName, fileUri);
    }

    private async Task FindAzaharAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        var found = await EmulatorSaveFinderService.ScanAzaharAsync(uri);
        if (found.Count == 0) return;

        foreach (var (fileUri, gameName) in found)
        {
            _dirService.AddFile(fileUri);
            AddFoundSave(gameName, fileUri);
        }
        App.RescanNeeded = true;
    }

    private async Task FindMelonDSAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        _dirService.AddDirectory(uri);
        App.RescanNeeded = true;
        AddFoundSave("MelonDS folder", uri);
    }

    private async Task FindRetroArchAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        _dirService.AddDirectory(uri);
        App.RescanNeeded = true;
        AddFoundSave("RetroArch folder", uri);
    }

    private async Task FindManualAsync()
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;

        _dirService.AddDirectory(uri);
        App.RescanNeeded = true;
        AddFoundSave("Custom folder", uri);
    }

    private void AddFoundSave(string gameName, string path)
    {
        _foundSaves.Add(new FoundSaveItem(gameName, path));
        _foundSaveCount++;
        _secondary.NotifyWelcomeSaveFound(gameName);

        SaveScanHeaderLabel.Text = $"{_foundSaveCount} save location{(_foundSaveCount == 1 ? "" : "s")} added";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Stub pickers for non-Android
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class NullDirectoryPicker : IDirectoryPicker
    {
        public Task<string?> PickDirectoryAsync() => Task.FromResult<string?>(null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  View model for found saves list
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record FoundSaveItem(string GameName, string FilePath);
}
