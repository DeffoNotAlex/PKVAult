using System.Collections.ObjectModel;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

#if ANDROID
using Android.Media;
using PKHeX.Mobile.Platforms.Android;
#endif

namespace PKHeX.Mobile.Pages;

/// <summary>
/// First-run welcome wizard.
/// On dual-screen: top screen shows preview, bottom screen shows interactive controls.
/// On single-screen: both preview and controls are shown stacked.
///
/// State machine: Reel (slides 0–5) → Wizard step 0 (theme) → step 1 (emulators) → step 2 (done).
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
    //  Intro reel state
    // ─────────────────────────────────────────────────────────────────────────

    private bool _reelActive;
    private int  _reelSlideIndex;
    private CancellationTokenSource? _reelCts;

    // Sprites indexed by slide (025=Pikachu, 150=Mewtwo, 493=Arceus, 644=Zekrom, 888=Zacian, 643=Reshiram)
    private static readonly (ushort Species, byte Form)[] ReelSpecies =
    [
        (025, 0),  // Slide 0 — Pikachu
        (150, 0),  // Slide 1 — Mewtwo
        (493, 0),  // Slide 2 — Arceus
        (644, 0),  // Slide 3 — Zekrom
        (888, 0),  // Slide 4 — Zacian
        (643, 0),  // Slide 5 — Reshiram
    ];

    private static readonly (string Headline, string Subtext)[] ReelSlides =
    [
        ("Every save. Every game.",         "Gen 1 through Legends: Z-A"),
        ("Edit any Pokémon",                "Stats, moves, ribbons, and more"),
        ("Full legality checking",          "Know exactly what's legal before you trade"),
        ("Works with your emulator",        "Eden, Azahar, MelonDS, RetroArch"),
        ("Dual screen support",             "Built for the AYN Thor"),
        ("Let's get started",               ""),
    ];

    // Glow color per slide (drawn as radial gradient behind sprite)
    private static readonly SKColor[] ReelGlowColors =
    [
        SKColor.Parse("#FFD700"), // Pikachu  — yellow
        SKColor.Parse("#9B59B6"), // Mewtwo   — purple
        SKColor.Parse("#F39C12"), // Arceus   — gold
        SKColor.Parse("#3498DB"), // Zekrom   — electric blue
        SKColor.Parse("#00BCD4"), // Zacian   — cyan
        SKColor.Parse("#FF8F00"), // Reshiram — amber
    ];

    // Feature category badge label per slide (null = no badge)
    private static readonly string?[] ReelBadgeLabels =
    [
        "Save Editor",
        "Pokémon Editor",
        "Legality",
        "Emulators",
        "Dual Screen",
        null,
    ];

    // Pre-loaded bitmaps for each slide (null = not yet loaded / show placeholder)
    private readonly SKBitmap?[] _reelBitmaps = new SKBitmap?[6];

    // Current sprite float offset (for bob animation) driven by _reelFloatTimer
    private float _reelFloatY;
    private float _reelFloatPhase;
    private IDispatcherTimer? _reelFloatTimer;

    // Current rendered sprite scale/opacity (animated in on slide entrance)
    private float _reelSpriteScale   = 1.0f;
    private float _reelSpriteOpacity = 1.0f;

    // Canvas size — tracked in paint handler so grid finale can use it
    private SKSize _canvasSize;

    // ── Grid finale state ────────────────────────────────────────────────────

    // 5×4 grid of species for the closing finale animation
    private static readonly (ushort Species, byte Form)[] GridSpecies =
    [
        (025, 0), (006, 0), (150, 0), (094, 0), (133, 0),   // row 0: Pikachu, Charizard, Mewtwo, Gengar, Eevee
        (448, 0), (384, 0), (658, 0), (493, 0), (445, 0),   // row 1: Lucario, Rayquaza, Greninja, Arceus, Garchomp
        (197, 0), (700, 0), (149, 0), (282, 0), (778, 0),   // row 2: Umbreon, Sylveon, Dragonite, Gardevoir, Mimikyu
        (644, 0), (888, 0), (643, 0), (887, 0), (1007, 0),  // row 3: Zekrom, Zacian, Reshiram, Dragapult, Koraidon
    ];

    private readonly SKBitmap?[] _gridBitmaps = new SKBitmap?[20];

    private struct GridSlot
    {
        public SKPoint Start;
        public SKPoint Target;
    }
    private GridSlot[] _gridSlots = new GridSlot[20];
    private bool  _gridActive;
    private float _gridOpacity = 1f;
    private float _gridScale   = 1.2f; // zooms from 1.2 → 1.0 after sprites land
    private DateTime _gridStartTime;

#if ANDROID
    // Audio — place pokemon_theme.ogg in PKHeX.Mobile/Resources/Raw/ to enable music
    private MediaPlayer? _mediaPlayer;
#endif

    // ─────────────────────────────────────────────────────────────────────────
    //  Static helper
    // ─────────────────────────────────────────────────────────────────────────

    private static bool _shownThisSession;

    public static bool ShouldShowWelcome()
        => !_shownThisSession && (
            !Preferences.Default.Get("onboarding_complete", false)
            || Preferences.Default.Get(SettingsPage.KeyAlwaysShowWelcome, false));

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
            _secondary.Show();

        // Start the reel before wizard step 0
        _ = StartReelAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopReel();
        if (_isDualScreen)
        {
            _secondary.HideReel();
            _secondary.HideWelcome();
        }
        StopAudio();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Intro reel
    // ─────────────────────────────────────────────────────────────────────────

    private async Task StartReelAsync()
    {
        _reelActive     = true;
        _reelSlideIndex = 0;
        _reelCts        = new CancellationTokenSource();
        var ct          = _reelCts.Token;

        // Show the reel overlay on the primary (top) screen
        ReelOverlay.IsVisible = true;
        ReelOverlay.Opacity   = 0;
        await ReelOverlay.FadeToAsync(1.0, 400);

        // Begin loading sprites in the background — don't block the reel
        _ = PreloadReelSpritesAsync();

        // Start background audio if the OGG file is present
        // Place PKHeX.Mobile/Resources/Raw/pokemon_theme.ogg in the project to enable music.
        StartAudio();

        // Start the float bob timer
        StartFloatTimer();

        // Run each slide
        for (int i = 0; i < ReelSlides.Length && !ct.IsCancellationRequested; i++)
        {
            _reelSlideIndex = i;
            await ShowReelSlideAsync(i, ct);
            if (ct.IsCancellationRequested) break;
        }

        if (!ct.IsCancellationRequested)
        {
            await RunGridFinaleAsync();
            await EndReelAsync();
        }
    }

    private async Task ShowReelSlideAsync(int slideIndex, CancellationToken ct)
    {
        var (headline, subtext) = ReelSlides[slideIndex];
        var bitmap = _reelBitmaps[slideIndex]; // may be null — shows placeholder

        // Update bottom screen slide text + progress bar
        if (_isDualScreen)
            _secondary.ShowReelSlide(slideIndex, headline, subtext, SkipReel);

        // Show badge on top screen
        var badgeText = ReelBadgeLabels[slideIndex];
        if (badgeText is not null)
        {
            ReelBadgeLabel.Text    = badgeText;
            ReelBadge.IsVisible    = true;
            ReelBadge.Opacity      = 0;
            _ = ReelBadge.FadeToAsync(1.0, 350);
        }
        else
        {
            ReelBadge.IsVisible = false;
        }

        // Entrance: sprite pops in from scale 0.6
        _reelSpriteScale   = 0.6f;
        _reelSpriteOpacity = 0.0f;
        ReelSpriteCanvas.InvalidateSurface();

        bool hasSprite = bitmap is not null;
        ReelPlaceholderCircle.IsVisible = !hasSprite;
        ReelPlaceholderCircle.Opacity   = hasSprite ? 0 : 0.4;

        // Animate sprite entrance (~400ms spring-out simulation via interpolation)
        var entranceStart = DateTime.UtcNow;
        const int entranceDuration = 400;
        while (!ct.IsCancellationRequested)
        {
            double elapsed = (DateTime.UtcNow - entranceStart).TotalMilliseconds;
            if (elapsed >= entranceDuration) break;
            double t = elapsed / entranceDuration;
            // SpringOut approximation: overshoot and settle
            double spring = 1.0 - Math.Pow(1.0 - t, 3) * (1.0 + 2.0 * t);
            _reelSpriteScale   = (float)(0.6 + 0.4 * spring);
            _reelSpriteOpacity = (float)Math.Min(1.0, t * 3.0);

            // Also refresh bitmap in case it loaded during entrance
            if (_reelBitmaps[slideIndex] is { } loaded && loaded != _reelSpriteCurrentBitmap)
            {
                _reelSpriteCurrentBitmap = loaded;
                ReelPlaceholderCircle.IsVisible = false;
            }

            ReelSpriteCanvas.InvalidateSurface();
            await Task.Delay(16, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;
        }

        _reelSpriteScale        = 1.0f;
        _reelSpriteOpacity      = 1.0f;
        _reelSpriteCurrentBitmap = _reelBitmaps[slideIndex];
        ReelSpriteCanvas.InvalidateSurface();

        // Hold for 4 seconds (minus entrance time), polling for sprite updates
        var holdStart = DateTime.UtcNow;
        const int holdMs = 4000;
        while (!ct.IsCancellationRequested)
        {
            double elapsed = (DateTime.UtcNow - holdStart).TotalMilliseconds;
            if (elapsed >= holdMs) break;

            // Pick up a newly loaded sprite during hold
            if (_reelBitmaps[slideIndex] is { } late && late != _reelSpriteCurrentBitmap)
            {
                _reelSpriteCurrentBitmap = late;
                ReelPlaceholderCircle.IsVisible = false;
                ReelSpriteCanvas.InvalidateSurface();
            }

            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        if (ct.IsCancellationRequested) return;

        // Exit: fade everything out before next slide
        if (_isDualScreen)
            _secondary.ShowReelTransition();
        _ = ReelBadge.FadeToAsync(0, 200);

        var exitStart = DateTime.UtcNow;
        const int exitDuration = 300;
        while (!ct.IsCancellationRequested)
        {
            double elapsed = (DateTime.UtcNow - exitStart).TotalMilliseconds;
            if (elapsed >= exitDuration) break;
            double t = elapsed / exitDuration;
            _reelSpriteOpacity = (float)(1.0 - t);
            ReelSpriteCanvas.InvalidateSurface();
            await Task.Delay(16, ct).ConfigureAwait(false);
        }

        _reelSpriteCurrentBitmap = null;
        _reelSpriteOpacity       = 0;
        ReelSpriteCanvas.InvalidateSurface();
    }

    // Current bitmap being drawn (updated per slide)
    private SKBitmap? _reelSpriteCurrentBitmap;

    private bool _reelEnding; // guard against double-call from race between auto-advance and skip

    private async Task EndReelAsync()
    {
        if (_reelEnding) return;
        _reelEnding = true;

        StopReel();

        // Fade out the reel overlay on primary screen
        await ReelOverlay.FadeToAsync(0, 300);
        ReelOverlay.IsVisible = false;

        if (_isDualScreen)
            _secondary.HideReel();

        // Now start the wizard normally
        ApplyStep(0, animate: false);

        if (_isDualScreen)
            _secondary.ShowWelcomeStep(0, OnWelcomeEvent);

        // Fade audio out over the reel-to-wizard handoff (fire-and-forget)
        _ = FadeOutAudioAsync();
    }

    private void SkipReel()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _reelCts?.Cancel();
            await EndReelAsync();
        });
    }

    private void StopReel()
    {
        _reelActive  = false;
        _gridActive  = false;
        _gridScale   = 1.0f;
        _gridOpacity = 0f;
        _reelFloatTimer?.Stop();
        _reelFloatTimer = null;
        _reelCts?.Cancel();
        _reelCts = null;
        _reelSpriteCurrentBitmap = null;
        ReelBadge.IsVisible      = false;
        ReelLogoOverlay.IsVisible = false;
    }

    private void StartFloatTimer()
    {
        _reelFloatPhase = 0;
        _reelFloatTimer = Dispatcher.CreateTimer();
        _reelFloatTimer.Interval = TimeSpan.FromMilliseconds(16);
        _reelFloatTimer.Tick += (_, _) =>
        {
            _reelFloatPhase += 0.04f; // ~2.4 rad/s = gentle bob
            _reelFloatY      = 5f * MathF.Sin(_reelFloatPhase);
            if (_reelActive || _gridActive)
                ReelSpriteCanvas.InvalidateSurface();
        };
        _reelFloatTimer.Start();
    }

    private async Task PreloadReelSpritesAsync()
    {
        // Reel sprites (shown per slide)
        for (int i = 0; i < ReelSpecies.Length; i++)
        {
            var (species, form) = ReelSpecies[i];
            _reelBitmaps[i] = await HomeSpriteCacheService.GetOrDownloadAsync(species, form, shiny: false)
                                                          .ConfigureAwait(false);
        }
        // Grid finale sprites — extra 6 (first 6 share cache entries with reel species)
        for (int i = 0; i < GridSpecies.Length; i++)
        {
            var (species, form) = GridSpecies[i];
            _gridBitmaps[i] = await HomeSpriteCacheService.GetOrDownloadAsync(species, form, shiny: false)
                                                          .ConfigureAwait(false);
        }
    }

    // ── Reel canvas painter ──────────────────────────────────────────────────

    private void OnReelSpritePaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        _canvasSize = new SKSize(e.Info.Width, e.Info.Height);

        // Grid finale takes over the canvas
        if (_gridActive)
        {
            DrawGridFinale(canvas, e.Info);
            return;
        }

        var bmp = _reelSpriteCurrentBitmap;
        if (_reelSpriteOpacity <= 0) return;

        float w  = e.Info.Width;
        float h  = e.Info.Height;
        float cx = w / 2f;
        float cy = h / 2f + _reelFloatY;

        // Pulsing radial glow behind sprite (breathes at ~0.7× the float rate)
        int safeSlide = Math.Clamp(_reelSlideIndex, 0, ReelGlowColors.Length - 1);
        float glowAlpha = (0.28f + 0.18f * MathF.Sin(_reelFloatPhase * 0.7f)) * _reelSpriteOpacity;
        var glowColor = ReelGlowColors[safeSlide];
        using (var glowShader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy),
            Math.Min(w, h) * 0.48f,
            [glowColor.WithAlpha((byte)(glowAlpha * 255)), glowColor.WithAlpha(0)],
            [0f, 1f],
            SKShaderTileMode.Clamp))
        using (var glowPaint = new SKPaint { Shader = glowShader, IsAntialias = true })
        {
            canvas.DrawCircle(cx, cy, Math.Min(w, h) * 0.48f, glowPaint);
        }

        if (bmp is null) return;

        // Size sprite to fill ~70% of the shorter dimension
        float maxSize = Math.Min(w, h) * 0.70f * _reelSpriteScale;
        float ratio   = (float)bmp.Width / bmp.Height;
        float sw, sh;
        if (ratio >= 1f) { sw = maxSize; sh = maxSize / ratio; }
        else             { sh = maxSize; sw = maxSize * ratio; }

        var dest = new SKRect(cx - sw / 2f, cy - sh / 2f, cx + sw / 2f, cy + sh / 2f);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha((byte)(_reelSpriteOpacity * 255)),
        };

        canvas.DrawBitmap(bmp, dest, paint);
    }

    // ── Grid finale drawing ──────────────────────────────────────────────────

    private void SetupGridSlots()
    {
        const int cols = 5, rows = 4;
        float w = _canvasSize.Width;
        float h = _canvasSize.Height;
        float margin = 16f;
        float slotW  = (w - margin * (cols + 1)) / cols;
        float slotH  = (h - margin * (rows + 1)) / rows;

        var rng = new Random(42); // fixed seed for deterministic start positions
        for (int i = 0; i < GridSpecies.Length; i++)
        {
            int row = i / cols, col = i % cols;
            float tx = margin + col * (slotW + margin) + slotW / 2f;
            float ty = margin + row * (slotH + margin) + slotH / 2f;

            SKPoint start = rng.Next(4) switch
            {
                0 => new SKPoint(-130f,    rng.NextSingle() * h),
                1 => new SKPoint(w + 130f, rng.NextSingle() * h),
                2 => new SKPoint(rng.NextSingle() * w, -130f),
                _ => new SKPoint(rng.NextSingle() * w, h + 130f),
            };

            _gridSlots[i] = new GridSlot { Start = start, Target = new SKPoint(tx, ty) };
        }
    }

    private void DrawGridFinale(SKCanvas canvas, SKImageInfo info)
    {
        double elapsed   = (DateTime.UtcNow - _gridStartTime).TotalMilliseconds;
        const int cols   = 5, rows = 4;
        float w          = info.Width;
        float h          = info.Height;
        float margin     = 16f;
        float slotW      = (w - margin * (cols + 1)) / cols;
        float slotH      = (h - margin * (rows + 1)) / rows;
        float spriteSize = Math.Min(slotW, slotH) * 0.82f;

        // Apply zoom-out scale transform centred on canvas
        canvas.Save();
        canvas.Scale(_gridScale, _gridScale, w / 2f, h / 2f);

        for (int i = 0; i < GridSpecies.Length; i++)
        {
            double spriteElapsed = elapsed - i * 65;
            if (spriteElapsed < 0) continue;

            // Spring-out easing for fly-in
            float t      = (float)Math.Min(1.0, spriteElapsed / 500.0);
            float spring = 1f - MathF.Pow(1f - t, 3) * (1f + 2f * t);

            var slot = _gridSlots[i];
            float x = slot.Start.X + (slot.Target.X - slot.Start.X) * spring;
            float y = slot.Start.Y + (slot.Target.Y - slot.Start.Y) * spring;

            float alpha = Math.Min(1f, (float)(spriteElapsed / 180.0)) * _gridOpacity;
            if (alpha <= 0) continue;

            var bmp = _gridBitmaps[i];
            if (bmp is not null)
            {
                float aspect = (float)bmp.Width / bmp.Height;
                float bw = aspect >= 1f ? spriteSize : spriteSize * aspect;
                float bh = aspect >= 1f ? spriteSize / aspect : spriteSize;
                var dest = new SKRect(x - bw / 2f, y - bh / 2f, x + bw / 2f, y + bh / 2f);
                using var p = new SKPaint { Color = SKColors.White.WithAlpha((byte)(alpha * 255)) };
                canvas.DrawBitmap(bmp, dest, p);
            }
            else
            {
                using var p = new SKPaint { Color = SKColors.White.WithAlpha((byte)(alpha * 45)), IsAntialias = true };
                canvas.DrawCircle(x, y, spriteSize / 2f, p);
            }
        }

        canvas.Restore();
    }

    private async Task RunGridFinaleAsync()
    {
        // Bottom screen dims during the grid
        if (_isDualScreen)
            _secondary.ShowReelTransition();

        SetupGridSlots();
        _gridActive    = true;
        _gridOpacity   = 1f;
        _gridScale     = 1.2f;
        _gridStartTime = DateTime.UtcNow;

        // ── Phase 1: wait for last sprite to land (65ms×19 + 500ms flight = ~1735ms) + brief hold
        await Task.Delay(1735 + 150).ConfigureAwait(false);

        // ── Phase 2: zoom out 1.2 → 1.0 over 500ms
        var zoomStart = DateTime.UtcNow;
        const int zoomDuration = 500;
        while (true)
        {
            double ms = (DateTime.UtcNow - zoomStart).TotalMilliseconds;
            if (ms >= zoomDuration) break;
            _gridScale = 1.2f - 0.2f * (float)(ms / zoomDuration);
            MainThread.BeginInvokeOnMainThread(() => ReelSpriteCanvas.InvalidateSurface());
            await Task.Delay(16).ConfigureAwait(false);
        }
        _gridScale = 1.0f;
        MainThread.BeginInvokeOnMainThread(() => ReelSpriteCanvas.InvalidateSurface());

        // ── Phase 3: fade in PKVault logo overlay
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            ReelLogoOverlay.IsVisible = true;
            await ReelLogoOverlay.FadeToAsync(1.0, 400);
        });

        // ── Phase 4: hold with logo visible
        await Task.Delay(900).ConfigureAwait(false);

        // ── Phase 5: fade out logo and grid simultaneously
        await MainThread.InvokeOnMainThreadAsync(() =>
            _ = ReelLogoOverlay.FadeToAsync(0, 380));

        var fadeStart = DateTime.UtcNow;
        const int fadeDuration = 380;
        while (true)
        {
            double ms = (DateTime.UtcNow - fadeStart).TotalMilliseconds;
            if (ms >= fadeDuration) break;
            _gridOpacity = 1f - (float)(ms / fadeDuration);
            MainThread.BeginInvokeOnMainThread(() => ReelSpriteCanvas.InvalidateSurface());
            await Task.Delay(16).ConfigureAwait(false);
        }

        _gridActive  = false;
        _gridOpacity = 0f;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ReelSpriteCanvas.InvalidateSurface();
            ReelLogoOverlay.IsVisible = false;
        });
    }

    // ── Tap-to-skip on primary screen ───────────────────────────────────────

    private void OnReelTapSkip(object? sender, EventArgs e) => SkipReel();

    // ─────────────────────────────────────────────────────────────────────────
    //  Audio
    // ─────────────────────────────────────────────────────────────────────────

    // Place the file at: PKHeX.Mobile/Resources/Raw/pokemon_theme.ogg
    // It will be bundled into the APK as a raw asset.
    // If the file is absent the audio block is skipped silently.

    private void StartAudio()
    {
#if ANDROID
        try
        {
            // Attempt to open the theme file from the app's raw asset bundle.
            // If it doesn't exist, AssetFileDescriptor will throw.
            var context = global::Android.App.Application.Context;
            var afd = context.Assets?.OpenFd("pokemon_theme.ogg");
            if (afd is null) return;

            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.SetDataSource(afd.FileDescriptor, afd.StartOffset, afd.Length);
            afd.Close();

            _mediaPlayer.Looping = true;
            _mediaPlayer.Prepare();
            _mediaPlayer.Start();
        }
        catch
        {
            // File not present or MediaPlayer error — skip audio silently.
            _mediaPlayer?.Release();
            _mediaPlayer = null;
        }
#endif
    }

    private void StopAudio()
    {
#if ANDROID
        try { _mediaPlayer?.Stop(); _mediaPlayer?.Release(); }
        catch { }
        _mediaPlayer = null;
#endif
    }

    private async Task FadeOutAudioAsync()
    {
#if ANDROID
        var player = _mediaPlayer;
        if (player is null) return;

        const int steps    = 20;
        const int stepMs   = 100; // 2 seconds total
        for (int i = steps; i >= 0; i--)
        {
            try { player.SetVolume(i / (float)steps, i / (float)steps); }
            catch { break; }
            await Task.Delay(stepMs).ConfigureAwait(false);
        }
        StopAudio();
#else
        await Task.CompletedTask;
#endif
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
        var outTask1 = currentPreview.FadeToAsync(0, 200);
        var outTask2 = currentPreview.TranslateToAsync(-80, 0, 200, Easing.CubicIn);
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
        var inTask1 = nextPreview.FadeToAsync(1, 250);
        var inTask2 = nextPreview.TranslateToAsync(0, 0, 250, Easing.CubicOut);
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
        _shownThisSession = true;
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
        using var labelPaint = new SKPaint { Color = labelColor, IsAntialias = true };
        using var tf   = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var font = new SKFont(tf, rect.Width * 0.13f);

        string label = darkCard ? "Dark" : "Light";
        float textW  = font.MeasureText(label);
        canvas.DrawText(label, rect.MidX - textW / 2f, rect.Bottom - rect.Height * 0.06f, font, labelPaint);
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
