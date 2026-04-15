using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using PKHeX.Core;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

namespace PKHeX.Mobile.Pages;

/// <summary>
/// Box grid page rendered on the AYN Thor's second AMOLED display (bottom screen).
/// GamePage drives this page via UpdateBoxGrid / InvalidateBoxCanvas.
/// The primary (top) screen shows GamePage's Row 0 (trainer card + Pokémon detail).
/// </summary>
public partial class SecondScreenPage : ContentPage
{
    private const int Columns = 6;
    private const int Rows    = 5;

    private readonly FileSystemSpriteRenderer _sprites = new();
    private readonly Stopwatch _pulseTimer = Stopwatch.StartNew();

    // State pushed by GamePage
    private PKM[]  _box           = [];
    private int    _cursorSlot;
    private int    _selectedSlot  = -1;
    private bool   _moveMode;
    private PKM?   _movePk;
    private int    _moveSourceBox = -1;
    private int    _moveSourceSlot = -1;
    private int    _currentBoxIndex;
    private bool   _showLegalityBadges;
    private bool?[] _legalityCache = [];

    // Pre-computed grid layout
    private SKRect[] _slotRects  = [];
    private int      _lastW, _lastH;

    // Cached main-menu state for live theme repaints
    private int  _lastFocusSection;
    private int  _lastActionCursor;
    private bool _mainMenuVisible;

    // Bounce animation: box grid slot that just received the cursor
    private int  _bounceSlot    = -1;
    private long _bounceStartMs;

    // Bank grid state (Mode C — driven by BankViewPage)
    private PKM?[]   _bankSlots      = [];
    private int      _bankCursorSlot;
    private SKRect[] _bankSlotRects  = [];
    private int      _lastBankW, _lastBankH;

    public SecondScreenPage()
    {
        InitializeComponent();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Call before discarding this instance so ThemeChanged doesn't leak.</summary>
    public void Cleanup() => ThemeService.ThemeChanged -= OnThemeChanged;

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // ContentPage.BackgroundColor and container BackgroundColor on Android don't
            // reliably re-apply from DynamicResource on an in-place dictionary update —
            // force them explicitly so backgrounds change without a restart.
            if (Application.Current?.Resources is { } res)
            {
                var pageBg     = res.TryGetValue("ThPageBg",     out var v1) && v1 is Color c1 ? c1 : Colors.Black;
                var settingsBg = res.TryGetValue("ThSettingsBg", out var v2) && v2 is Color c2 ? c2 : Colors.Black;
                var rowBg      = res.TryGetValue("ThSettingsRow", out var v3) && v3 is Color c3 ? c3 : Colors.Black;

                BackgroundColor               = pageBg;
                BoxGridPanel.BackgroundColor  = pageBg;
                MainMenuPanel.BackgroundColor = pageBg;
                BankGridPanel.BackgroundColor = pageBg;
                WelcomePanel.BackgroundColor  = settingsBg;
                ReelPanel.BackgroundColor     = settingsBg;

                // Emulator-row Borders in WelcomeStep1Panel
                WelcomeRow_Eden.BackgroundColor    = rowBg;
                WelcomeRow_Azahar.BackgroundColor  = rowBg;
                WelcomeRow_MelonDS.BackgroundColor = rowBg;
                WelcomeRow_RetroArch.BackgroundColor = rowBg;
                WelcomeRow_Manual.BackgroundColor  = rowBg;

                // Theme-card Borders in WelcomeStep0Panel
                ThemeCardDark.BackgroundColor  = rowBg;
                ThemeCardLight.BackgroundColor = rowBg;
            }

            // Force-rebind CollectionView cells so CardBackground re-renders on Android
            var src = MenuSavesList.ItemsSource;
            MenuSavesList.ItemsSource = null;
            MenuSavesList.ItemsSource = src;

            BoxCanvas.InvalidateSurface();
            BankCanvas.InvalidateSurface();
            if (_mainMenuVisible)
                ApplyMainMenuHighlight(_lastFocusSection, _lastActionCursor);
        });
    }

    // ──────────────────────────────────────────────
    //  Public API — driven by GamePage via ISecondaryDisplay
    // ──────────────────────────────────────────────

    public void UpdateBoxGrid(
        PKM[] box, int cursorSlot, int selectedSlot,
        bool moveMode, PKM? movePk, int moveSourceBox, int moveSourceSlot,
        int currentBoxIndex, string boxName, bool?[] legalityCache, bool showLegalityBadges)
    {
        int prevBoxIndex = _currentBoxIndex;
        bool wasShowingGrid = BoxGridPanel.IsVisible;

        _box              = box;
        _cursorSlot       = cursorSlot;
        _selectedSlot     = selectedSlot;
        _moveMode         = moveMode;
        _movePk           = movePk;
        _moveSourceBox    = moveSourceBox;
        _moveSourceSlot   = moveSourceSlot;
        _currentBoxIndex  = currentBoxIndex;
        _showLegalityBadges = showLegalityBadges;
        _legalityCache    = legalityCache;

        // Switch to box grid panel, hiding whichever mode was active
        if (!BoxGridPanel.IsVisible)
        {
            _mainMenuVisible         = false;
            BoxGridPanel.IsVisible   = true;
            MainMenuPanel.IsVisible  = false;
            BankGridPanel.IsVisible  = false;
        }

        BoxNameLabel.Text = boxName;

        // Slide in new box content when the box index changes
        if (wasShowingGrid && currentBoxIndex != prevBoxIndex)
        {
            int dir = currentBoxIndex > prevBoxIndex ? 1 : -1;
            double w = BoxCanvas.Width > 10 ? BoxCanvas.Width : 360;
            BoxCanvas.TranslationX = dir * w;
            _ = BoxCanvas.TranslateToAsync(0, 0, 180, Easing.CubicOut);
        }

        var pk = cursorSlot < box.Length ? box[cursorSlot] : null;
        InfoSpeciesNum.Text  = pk?.Species > 0 ? $"#{pk.Species:000}" : "";
        InfoSpeciesName.Text = pk?.Species > 0
            ? GameInfo.GetStrings("en").specieslist[pk.Species]
            : "";

        _ = _sprites.PreloadBoxAsync(box);
        BoxCanvas.InvalidateSurface();
    }

    public void UpdateCursor(int cursorSlot, int selectedSlot, bool moveMode, PKM? movePk, int currentBoxIndex)
    {
        _bounceSlot    = cursorSlot;
        _bounceStartMs = _pulseTimer.ElapsedMilliseconds;

        _cursorSlot      = cursorSlot;
        _selectedSlot    = selectedSlot;
        _moveMode        = moveMode;
        _movePk          = movePk;
        _currentBoxIndex = currentBoxIndex;

        var pk = cursorSlot < _box.Length ? _box[cursorSlot] : null;
        InfoSpeciesNum.Text  = pk?.Species > 0 ? $"#{pk.Species:000}" : "";
        InfoSpeciesName.Text = pk?.Species > 0
            ? GameInfo.GetStrings("en").specieslist[pk.Species]
            : "";
    }

    public void InvalidateBoxCanvas() => BoxCanvas.InvalidateSurface();

    // ──────────────────────────────────────────────
    //  Bank grid (Mode C — driven by BankViewPage)
    // ──────────────────────────────────────────────

    public void ShowBankGrid(PKM?[] slots, int cursorSlot, string boxName, int boxIndex, int boxCount)
    {
        _bankSlots      = slots;
        _bankCursorSlot = cursorSlot;

        _mainMenuVisible         = false;
        BoxGridPanel.IsVisible   = false;
        MainMenuPanel.IsVisible  = false;
        BankGridPanel.IsVisible  = true;

        BankGridNameLabel.Text  = boxName;
        BankGridIndexLabel.Text = $"{boxIndex + 1} / {boxCount}";

        UpdateBankSpeciesInfo();
        _ = _sprites.PreloadBoxAsync(slots.Select(p => p ?? (PKM)new PK9()).ToArray());
        BankCanvas.InvalidateSurface();
    }

    public void UpdateBankCursor(int cursorSlot)
    {
        _bankCursorSlot = cursorSlot;
        UpdateBankSpeciesInfo();
        BankCanvas.InvalidateSurface();
    }

    public void InvalidateBankCanvas() => BankCanvas.InvalidateSurface();

    private void UpdateBankSpeciesInfo()
    {
        var pk = _bankCursorSlot < _bankSlots.Length ? _bankSlots[_bankCursorSlot] : null;
        BankInfoSpeciesNum.Text  = pk?.Species > 0 ? $"#{pk.Species:000}" : "";
        BankInfoSpeciesName.Text = pk?.Species > 0
            ? GameInfo.GetStrings("en").specieslist[pk.Species]
            : "";
    }

    private void RecalcBankGridLayout(int w, int h)
    {
        if (w == _lastBankW && h == _lastBankH && _bankSlotRects.Length == Columns * Rows) return;
        _lastBankW = w; _lastBankH = h;

        const float gap = 6f, padX = 14f, padY = 4f;
        float availW = w - padX * 2 - gap * (Columns - 1);
        float availH = h - padY * 2 - gap * (Rows - 1);
        float slotSize = MathF.Min(availW / Columns, availH / Rows);

        float gridW = slotSize * Columns + gap * (Columns - 1);
        float gridH = slotSize * Rows + gap * (Rows - 1);
        float offX = (w - gridW) / 2f;
        float offY = (h - gridH) / 2f;

        _bankSlotRects = new SKRect[Columns * Rows];
        for (int i = 0; i < _bankSlotRects.Length; i++)
        {
            int col = i % Columns, row = i / Columns;
            float x = offX + col * (slotSize + gap);
            float y = offY + row * (slotSize + gap);
            _bankSlotRects[i] = new SKRect(x, y, x + slotSize, y + slotSize);
        }
    }

    private void OnBankPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(ThemeService.CanvasBg);

        RecalcBankGridLayout(e.Info.Width, e.Info.Height);

        const float radius = 10f;
        float tBlue  = (float)(_pulseTimer.Elapsed.TotalMilliseconds % 1800) / 1800f;
        float pulse  = 0.5f + 0.5f * MathF.Sin(tBlue * MathF.PI * 2);

        for (int i = 0; i < BankService.SlotsPerBox && i < _bankSlotRects.Length; i++)
        {
            var rect  = _bankSlotRects[i];
            var pk    = i < _bankSlots.Length ? _bankSlots[i] : null;
            bool isCursor = i == _bankCursorSlot;
            bool filled   = pk?.Species > 0;

            // Slot background
            var bgColor = filled ? ThemeService.SlotFilled : ThemeService.SlotEmpty;
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawRoundRect(rect, radius, radius, bgPaint);

            using var borderPaint = new SKPaint
            {
                Color = ThemeService.SlotBorder,
                Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true,
            };
            canvas.DrawRoundRect(rect, radius, radius, borderPaint);

            // Sprite
            if (filled)
                DrawSprite(canvas, _sprites.GetSprite(pk!), rect, isCursor ? 0.75f : 0.70f, 255);

            // Cursor
            if (isCursor)
                DrawBlueCursor(canvas, rect, radius, pulse);
        }
    }

    // ──────────────────────────────────────────────
    //  Main menu (save list + action bar)
    // ──────────────────────────────────────────────

    public void ShowMainMenu(IList<object> saves, int cursorIndex)
    {
        _mainMenuVisible         = true;
        BoxGridPanel.IsVisible   = false;
        BankGridPanel.IsVisible  = false;
        MainMenuPanel.IsVisible  = true;

        MenuSavesList.ItemsSource = saves;
        MenuSaveCountLabel.Text   = $"{saves.Count} save{(saves.Count != 1 ? "s" : "")}";

        if (saves.Count > 0 && cursorIndex >= 0 && cursorIndex < saves.Count)
            MenuSavesList.ScrollTo(cursorIndex, -1, ScrollToPosition.MakeVisible, false);
    }

    public void UpdateMainMenuState(int cursorIndex, int focusSection, int actionCursor)
    {
        _lastFocusSection = focusSection;
        _lastActionCursor = actionCursor;

        if (MenuSavesList.ItemsSource is IList<object> saves &&
            cursorIndex >= 0 && cursorIndex < saves.Count)
            MenuSavesList.ScrollTo(cursorIndex, -1, ScrollToPosition.MakeVisible, false);

        ApplyMainMenuHighlight(focusSection, actionCursor);
    }

    private void ApplyMainMenuHighlight(int focusSection, int actionCursor)
    {
        bool light       = Current == PkTheme.Light;
        var focusBg      = Color.FromArgb(light ? "#EEF2FF" : "#182242");
        var focusStroke  = Color.FromArgb("#3B8BFF");
        var normalBg     = Color.FromArgb(light ? "#FFFFFF" : "#131B35");
        var normalStroke = Color.FromArgb(light ? "#E0E4EC" : "#0DFFFFFF");

        bool primaryFocused = focusSection == 1 && actionCursor == 0;
        Menu_OpenBoxes.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = [
                new GradientStop(Color.FromArgb("#EAF5FF"), 0f),
                new GradientStop(Color.FromArgb("#CCE8FF"), 1f),
            ],
        };
        Menu_OpenBoxes.Stroke = primaryFocused ? Color.FromArgb("#5AAAD0") : Colors.Transparent;

        Border[] tiles = [Menu_Search, Menu_Gifts, Menu_Export, Menu_Bank];
        for (int i = 0; i < tiles.Length; i++)
        {
            bool focused = focusSection == 1 && actionCursor == i + 1;
            tiles[i].BackgroundColor = focused ? focusBg : normalBg;
            tiles[i].Stroke          = focused ? focusStroke : normalStroke;
        }
    }

    // ──────────────────────────────────────────────
    //  Box grid painting
    // ──────────────────────────────────────────────

    private void RecalcGridLayout(int w, int h)
    {
        if (w == _lastW && h == _lastH && _slotRects.Length == Columns * Rows) return;
        _lastW = w; _lastH = h;

        const float gap = 6f, padX = 14f, padY = 4f;
        float availW = w - padX * 2 - gap * (Columns - 1);
        float availH = h - padY * 2 - gap * (Rows - 1);
        float slotSize = MathF.Min(availW / Columns, availH / Rows);

        float gridW = slotSize * Columns + gap * (Columns - 1);
        float gridH = slotSize * Rows + gap * (Rows - 1);
        float offX = (w - gridW) / 2f;
        float offY = (h - gridH) / 2f;

        _slotRects = new SKRect[Columns * Rows];
        for (int i = 0; i < _slotRects.Length; i++)
        {
            int col = i % Columns, row = i / Columns;
            float x = offX + col * (slotSize + gap);
            float y = offY + row * (slotSize + gap);
            _slotRects[i] = new SKRect(x, y, x + slotSize, y + slotSize);
        }
    }

    private void OnBoxPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(ThemeService.CanvasBg);
        if (_box.Length == 0) return;

        RecalcGridLayout(e.Info.Width, e.Info.Height);

        const float radius = 10f;

        float tBlue  = (float)(_pulseTimer.Elapsed.TotalMilliseconds % 1800) / 1800f;
        float pulseBlue  = 0.5f + 0.5f * MathF.Sin(tBlue  * MathF.PI * 2);
        float tGreen = (float)(_pulseTimer.Elapsed.TotalMilliseconds % 1400) / 1400f;
        float pulseGreen = 0.5f + 0.5f * MathF.Sin(tGreen * MathF.PI * 2);

        for (int i = 0; i < _box.Length && i < _slotRects.Length; i++)
        {
            var rect = _slotRects[i];
            var pk   = _box[i];
            bool isCursor   = i == _cursorSlot;
            bool isSelected = i == _selectedSlot && !_moveMode;
            bool isSource   = _moveMode && _moveSourceBox == _currentBoxIndex && i == _moveSourceSlot;
            bool filled     = pk.Species != 0;

            // Slot background
            var bgColor = filled ? ThemeService.SlotFilled : ThemeService.SlotEmpty;
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawRoundRect(rect, radius, radius, bgPaint);

            using var borderPaint = new SKPaint
            {
                Color = ThemeService.SlotBorder,
                Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true,
            };
            canvas.DrawRoundRect(rect, radius, radius, borderPaint);

            // Sprite
            if (filled)
            {
                var sprite = _sprites.GetSprite(pk);
                float scale = isCursor ? 0.75f : 0.70f;
                // Bounce: pop out on cursor land
                if (i == _bounceSlot)
                {
                    long bounceElapsed = _pulseTimer.ElapsedMilliseconds - _bounceStartMs;
                    if (bounceElapsed < 300)
                    {
                        float bt = bounceElapsed / 300f;
                        scale += 0.12f * (1f - bt) * (1f - bt);
                    }
                }
                byte alpha = isSource ? (byte)70 : (byte)255;
                DrawSprite(canvas, sprite, rect, scale, alpha);
            }

            // Move mode ghost
            if (_moveMode && isCursor && _movePk != null && !isSource)
                DrawSprite(canvas, _sprites.GetSprite(_movePk), rect, 0.70f, 110);

            // Cursor
            if (_moveMode && isCursor)
                DrawGreenCursor(canvas, rect, radius, pulseGreen);
            else if (isSelected)
                DrawSelectedCursor(canvas, rect, radius);
            else if (isCursor)
                DrawBlueCursor(canvas, rect, radius, pulseBlue);

            // Legality badge
            if (_showLegalityBadges && i < _legalityCache.Length && _legalityCache[i] is bool legal)
            {
                var dotColor = legal ? new SKColor(60, 220, 110, 230) : new SKColor(255, 82, 82, 230);
                using var glowPaint = new SKPaint
                {
                    Color = dotColor.WithAlpha(80), IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(4, 4),
                };
                canvas.DrawCircle(rect.Right - 8f, rect.Top + 8f, 5f, glowPaint);
                using var dotPaint = new SKPaint { Color = dotColor, IsAntialias = true };
                canvas.DrawCircle(rect.Right - 8f, rect.Top + 8f, 3f, dotPaint);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Static drawing helpers (mirrors GamePage)
    // ──────────────────────────────────────────────

    private static void DrawSprite(SKCanvas canvas, SKBitmap sprite, SKRect slot, float scale, byte alpha)
    {
        float inner = slot.Width * scale;
        float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
        float drawW, drawH;
        if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
        else              { drawH = inner; drawW = inner * aspect; }
        float sx = slot.MidX - drawW / 2f, sy = slot.MidY - drawH / 2f;
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH), paint);
    }

    private static void DrawBlueCursor(SKCanvas canvas, SKRect rect, float radius, float pulse)
    {
        using var fillPaint = new SKPaint { Color = new SKColor(59, 139, 255, 31), IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, fillPaint);
        using var strokePaint = new SKPaint
        {
            Color = SKColor.Parse("#5CA0FF"), Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f, IsAntialias = true,
        };
        canvas.DrawRoundRect(rect, radius, radius, strokePaint);

        float expand = 4f;
        var outerRect = new SKRect(rect.Left - expand, rect.Top - expand, rect.Right + expand, rect.Bottom + expand);
        float outerScale = 1f + 0.02f * pulse;
        canvas.Save();
        canvas.Scale(outerScale, outerScale, rect.MidX, rect.MidY);
        using var outerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(92, 160, 255, (byte)(89 + 77 * pulse)),
            StrokeWidth = 2f, IsAntialias = true,
        };
        canvas.DrawRoundRect(outerRect, 13, 13, outerPaint);
        canvas.Restore();

        var glowRect = new SKRect(rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1);
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(59, 139, 255, (byte)(46 + 31 * pulse)),
            ImageFilter = SKImageFilter.CreateBlur(6, 6), IsAntialias = true,
        };
        canvas.DrawRoundRect(glowRect, 11, 11, glowPaint);
    }

    private static void DrawSelectedCursor(SKCanvas canvas, SKRect rect, float radius)
    {
        using var fillPaint = new SKPaint { Color = new SKColor(107, 171, 255, 30), IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, fillPaint);
        using var strokePaint = new SKPaint
        {
            Color = new SKColor(160, 200, 255), Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f, IsAntialias = true,
        };
        canvas.DrawRoundRect(rect, radius, radius, strokePaint);

        var outerRect = new SKRect(rect.Left - 4, rect.Top - 4, rect.Right + 4, rect.Bottom + 4);
        using var outerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke, Color = new SKColor(107, 171, 255, 77),
            StrokeWidth = 2f, IsAntialias = true,
        };
        canvas.DrawRoundRect(outerRect, 13, 13, outerPaint);

        var glowRect = new SKRect(rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1);
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(59, 139, 255, 51),
            ImageFilter = SKImageFilter.CreateBlur(7, 7), IsAntialias = true,
        };
        canvas.DrawRoundRect(glowRect, 11, 11, glowPaint);
    }

    // ──────────────────────────────────────────────
    //  Intro reel (Mode E — pre-wizard)
    // ──────────────────────────────────────────────

    // Per-slide bullet text (3 bullets per slide; empty = hidden)
    private static readonly string[][] SlideBullets =
    [
        // Slide 0 — Pikachu / Every save
        ["Gen 1 through Legends: Z-A", "3DS, Switch & GBA saves", "Import/export individual Pokémon"],
        // Slide 1 — Mewtwo / Edit any Pokémon
        ["IVs, EVs, nature, ability", "Moves, ribbons, contest stats", "Met location, OT, language"],
        // Slide 2 — Arceus / Legality
        ["Flags illegal moves & encounters", "Checks PID, EC, HOME tracker", "Know before you trade"],
        // Slide 3 — Zekrom / Emulators
        ["Eden · Azahar · MelonDS", "RetroArch supported", "Auto-detects save locations"],
        // Slide 4 — Zacian / Dual screen
        ["Box grid on bottom display", "Pokémon detail on top", "Full gamepad navigation"],
        // Slide 5 — Reshiram / Let's get started
        [],
    ];

    private Action? _reelSkipAction;

    public void ShowReelSlide(int slideIndex, string headline, string subtext, Action onSkip)
    {
        _reelSkipAction = onSkip;

        BoxGridPanel.IsVisible  = false;
        BankGridPanel.IsVisible = false;
        MainMenuPanel.IsVisible = false;
        WelcomePanel.IsVisible  = false;
        ReelPanel.IsVisible     = true;
        _mainMenuVisible        = false;

        // Reset text to invisible
        ReelHeadlineLabel.Opacity = 0;
        ReelSubtextLabel.Opacity  = 0;
        ReelBullet0.Opacity       = 0;
        ReelBullet1.Opacity       = 0;
        ReelBullet2.Opacity       = 0;

        ReelHeadlineLabel.Text = headline;
        ReelSubtextLabel.Text  = subtext;

        // Set bullet texts (prefix with bullet char)
        string[] bullets = slideIndex < SlideBullets.Length ? SlideBullets[slideIndex] : [];
        ReelBullet0.Text = bullets.Length > 0 ? $"• {bullets[0]}" : "";
        ReelBullet1.Text = bullets.Length > 1 ? $"• {bullets[1]}" : "";
        ReelBullet2.Text = bullets.Length > 2 ? $"• {bullets[2]}" : "";

        // Reset + restart progress bar
        ReelProgressBar.CancelAnimations();
        ReelProgressBar.Progress = 0;
        _ = ReelProgressBar.ProgressTo(1.0, 4000, Easing.Linear);

        // Staggered fade-in: headline → subtext → bullets
        _ = ReelHeadlineLabel.FadeToAsync(1.0, 300);
        Delay(150, () => _ = ReelSubtextLabel.FadeToAsync(1.0, 280));
        if (bullets.Length > 0) Delay(400,  () => _ = ReelBullet0.FadeToAsync(1.0, 250));
        if (bullets.Length > 1) Delay(620,  () => _ = ReelBullet1.FadeToAsync(1.0, 250));
        if (bullets.Length > 2) Delay(840,  () => _ = ReelBullet2.FadeToAsync(1.0, 250));
    }

    public void ShowReelTransition()
    {
        ReelProgressBar.CancelAnimations();
        _ = ReelHeadlineLabel.FadeToAsync(0, 200);
        _ = ReelSubtextLabel.FadeToAsync(0, 200);
        _ = ReelBullet0.FadeToAsync(0, 150);
        _ = ReelBullet1.FadeToAsync(0, 150);
        _ = ReelBullet2.FadeToAsync(0, 150);
    }

    public void HideReel()
    {
        ReelProgressBar.CancelAnimations();
        ReelPanel.IsVisible = false;
        _reelSkipAction     = null;
    }

    private void OnReelSkipTapped(object? sender, EventArgs e)
        => _reelSkipAction?.Invoke();

    private static void Delay(int ms, Action action)
        => Task.Delay(ms).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(action));

    // ──────────────────────────────────────────────
    //  Welcome wizard (Mode D)
    // ──────────────────────────────────────────────

    private Action<string>? _welcomeEvent;
    private int  _welcomeStep;
    private int  _welcomeSavesFound;
    private bool _step1NextEnabled;

    // Gamepad focus within the welcome panel
    // Step 0: 0=dark card, 1=light card
    // Step 1: 0=Eden, 1=Azahar, 2=MelonDS, 3=RetroArch, 4=Manual
    // Step 2: 0=GetStarted
    private int _welcomeFocus;

    public void ShowWelcomeStep(int step, Action<string> onEvent)
    {
        _welcomeEvent = onEvent;
        _welcomeStep  = step;
        _welcomeSavesFound = 0;
        _step1NextEnabled  = false;

        BoxGridPanel.IsVisible  = false;
        BankGridPanel.IsVisible = false;
        MainMenuPanel.IsVisible = false;
        WelcomePanel.IsVisible  = true;
        _mainMenuVisible        = false;

        ApplyWelcomeStep(step);

#if ANDROID
        GamepadRouter.KeyReceived -= OnWelcomeGamepadKey;
        GamepadRouter.KeyReceived += OnWelcomeGamepadKey;
#endif
    }

    private void ApplyWelcomeStep(int step)
    {
        _welcomeFocus = 0;

        WelcomeStep0Panel.IsVisible = step == 0;
        WelcomeStep1Panel.IsVisible = step == 1;
        WelcomeStep2Panel.IsVisible = step == 2;

        WelcomeBtn_NextStep0.IsVisible  = step == 0;
        WelcomeBtns_Step1.IsVisible     = step == 1;
        WelcomeBtn_GetStarted.IsVisible = step == 2;

        switch (step)
        {
            case 0:
                WelcomeStepHeader.Text   = "Choose your look";
                WelcomeStepSubtitle.Text = "Step 1 of 3";
                break;
            case 1:
                WelcomeStepHeader.Text   = "Connect your saves";
                WelcomeStepSubtitle.Text = "Step 2 of 3";
                ResetEmulatorStatuses();
                break;
            case 2:
                WelcomeStepHeader.Text   = "All done!";
                WelcomeStepSubtitle.Text = "Step 3 of 3";
                UpdateFoundSummary();
                break;
        }
    }

    private void ResetEmulatorStatuses()
    {
        WelcomeEdenStatus.Text      = "○";
        WelcomeEdenStatus.TextColor     = Color.FromArgb(ThemeService.Current == PkTheme.Light ? "#9CA3AF" : "#6B7280");
        WelcomeAzaharStatus.Text    = "○";
        WelcomeAzaharStatus.TextColor   = WelcomeEdenStatus.TextColor;
        WelcomeMelonDSStatus.Text   = "○";
        WelcomeMelonDSStatus.TextColor  = WelcomeEdenStatus.TextColor;
        WelcomeRetroArchStatus.Text = "○";
        WelcomeRetroArchStatus.TextColor = WelcomeEdenStatus.TextColor;
    }

    private void UpdateFoundSummary()
    {
        WelcomeFoundSummary.Text = _welcomeSavesFound > 0
            ? $"Found {_welcomeSavesFound} save{(_welcomeSavesFound == 1 ? "" : "s")} ready to edit."
            : "Ready to edit your Pokémon.";
    }

    public void NotifyWelcomeSaveFound(string gameName)
    {
        _welcomeSavesFound++;
        _step1NextEnabled = true;
        WelcomeBtn_NextStep1.Opacity = 1.0;

        // Mark the appropriate emulator row as found
        // gameName is used for display; we just increment the counter here
        UpdateFoundSummary();
    }

    public void HideWelcome()
    {
#if ANDROID
        GamepadRouter.KeyReceived -= OnWelcomeGamepadKey;
#endif
        WelcomePanel.IsVisible  = false;
        _welcomeEvent           = null;
    }

    // ──────────────────────────────────────────────
    //  Bank manage menu (Mode F)
    // ──────────────────────────────────────────────

    private Action<string>? _bankManageEvent;
    private int _bankManageCursor; // 0=Rename 1=Add 2=Remove
    private static readonly Border[] _bankManageRows = [];
    private Border[] BankManageRows => [BankManageRow_Rename, BankManageRow_Add, BankManageRow_Remove];

    public void ShowBankManageMenu(int boxIndex, string boxName, int boxCount, Action<string> onAction)
    {
        _bankManageEvent  = onAction;
        _bankManageCursor = 0;

        BoxGridPanel.IsVisible     = false;
        BankGridPanel.IsVisible    = false;
        MainMenuPanel.IsVisible    = false;
        WelcomePanel.IsVisible     = false;
        BankManagePanel.IsVisible  = true;
        _mainMenuVisible           = false;

        BankManageSubtitle.Text     = $"{boxName}  ·  {boxCount} box{(boxCount != 1 ? "es" : "")}";
        BankManageRemoveHint.Text   = boxCount <= 1
            ? "Cannot remove the last box"
            : "Delete this box (warns if not empty)";
        BankManageRow_Remove.Opacity = boxCount <= 1 ? 0.4 : 1.0;

        ApplyBankManageHighlight();

#if ANDROID
        GamepadRouter.KeyReceived -= OnBankManageGamepadKey;
        GamepadRouter.KeyReceived += OnBankManageGamepadKey;
#endif
    }

    public void HideBankManageMenu()
    {
#if ANDROID
        GamepadRouter.KeyReceived -= OnBankManageGamepadKey;
#endif
        BankManagePanel.IsVisible = false;
        _bankManageEvent          = null;
    }

    private void ApplyBankManageHighlight()
    {
        var rows = BankManageRows;
        for (int i = 0; i < rows.Length; i++)
            rows[i].Stroke = i == _bankManageCursor
                ? Color.FromArgb("#4F80FF")
                : Colors.Transparent;
    }

#if ANDROID
    private void OnBankManageGamepadKey(Android.Views.Keycode keyCode, Android.Views.KeyEventActions action)
    {
        if (action != Android.Views.KeyEventActions.Down) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (keyCode)
            {
                case Android.Views.Keycode.DpadUp:
                    if (_bankManageCursor > 0) { _bankManageCursor--; ApplyBankManageHighlight(); }
                    break;
                case Android.Views.Keycode.DpadDown:
                    if (_bankManageCursor < 2) { _bankManageCursor++; ApplyBankManageHighlight(); }
                    break;
                case Android.Views.Keycode.ButtonA:
                    FireBankManageAction();
                    break;
                case Android.Views.Keycode.ButtonB:
                    HideBankManageMenu();
                    _bankManageEvent?.Invoke("close");
                    break;
            }
        });
    }
#endif

    private void FireBankManageAction()
    {
        var action = _bankManageCursor switch
        {
            0 => "rename",
            1 => "add",
            2 => "remove",
            _ => "close",
        };
        var evt = _bankManageEvent; // capture before Hide clears it
        HideBankManageMenu();
        evt?.Invoke(action);
    }

    // ── Mode G — Pokédex stats ─────────────────────────────────────────────────

    private static readonly string[] _genNames =
        DexService.Generations.Select(g => g.Name).ToArray();

    public void ShowDexStats(int totalCaught, int totalSpecies, int[] caughtPerGen, int[] totalPerGen)
    {
        BoxGridPanel.IsVisible    = false;
        BankGridPanel.IsVisible   = false;
        MainMenuPanel.IsVisible   = false;
        WelcomePanel.IsVisible    = false;
        BankManagePanel.IsVisible = false;
        ReelPanel.IsVisible       = false;
        DexStatsPanel.IsVisible   = true;
        _mainMenuVisible          = false;

        DexTotalLabel.Text = $"{totalCaught} / {totalSpecies} collected";

        // Progress bar fill — defer until layout so Width is known
        DexStatsPanel.SizeChanged -= OnDexPanelSizeChanged;
        DexStatsPanel.SizeChanged += OnDexPanelSizeChanged;
        _dexProgressFraction = totalSpecies > 0 ? (double)totalCaught / totalSpecies : 0;
        ApplyDexProgressWidth();

        // Rebuild per-gen rows
        DexGenList.Children.Clear();
        for (int g = 0; g < _genNames.Length && g < caughtPerGen.Length; g++)
        {
            int caught = caughtPerGen[g];
            int total  = totalPerGen[g];
            double frac = total > 0 ? (double)caught / total : 0;

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(52))),
                ColumnSpacing = 10,
            };

            row.Add(new Label
            {
                Text = _genNames[g],
                FontFamily = "NunitoBold", FontSize = 11,
                TextColor = Color.FromArgb("#8899BB"),
                VerticalOptions = LayoutOptions.Center,
            }, 0, 0);

            var barBg = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
                BackgroundColor = Color.FromArgb("#1AFFFFFF"),
                HeightRequest = 6,
                VerticalOptions = LayoutOptions.Center,
            };
            var barFill = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
                BackgroundColor = caught == total
                    ? Color.FromArgb("#34D990")
                    : Color.FromArgb("#3B8BFF"),
                HeightRequest = 6,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
            };
            var barGrid = new Grid { Children = { barBg, barFill } };
            // Set fill width after layout
            barBg.SizeChanged += (_, _) =>
                barFill.WidthRequest = barBg.Width * frac;

            row.Add(barGrid, 1, 0);

            row.Add(new Label
            {
                Text = $"{caught}/{total}",
                FontFamily = "Nunito", FontSize = 10,
                TextColor = Color.FromArgb("#8899BB"),
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions = LayoutOptions.Center,
            }, 2, 0);

            DexGenList.Children.Add(row);
        }
    }

    public void HideDexStats()
    {
        DexStatsPanel.IsVisible = false;
        DexStatsPanel.SizeChanged -= OnDexPanelSizeChanged;
    }

    private double _dexProgressFraction;

    private void OnDexPanelSizeChanged(object? sender, EventArgs e) => ApplyDexProgressWidth();

    private void ApplyDexProgressWidth()
    {
        double panelWidth = DexStatsPanel.Width - 40; // subtract Padding left+right
        if (panelWidth > 0)
            DexProgressFill.WidthRequest = panelWidth * _dexProgressFraction;
    }

    // ── Tap handlers (Step 0) ──────────────────────────────────────────────────

    private void OnDarkThemeTapped(object? sender, EventArgs e)
    {
        ThemeCardDark.Stroke  = Color.FromArgb("#4F80FF");
        ThemeCardLight.Stroke = Colors.Transparent;
        _welcomeEvent?.Invoke("theme:dark");
    }

    private void OnLightThemeTapped(object? sender, EventArgs e)
    {
        ThemeCardLight.Stroke = Color.FromArgb("#4F80FF");
        ThemeCardDark.Stroke  = Colors.Transparent;
        _welcomeEvent?.Invoke("theme:light");
    }

    private void OnWelcomeNextStep0Tapped(object? sender, EventArgs e)
        => _welcomeEvent?.Invoke("next");

    // ── Tap handlers (Step 1) ──────────────────────────────────────────────────

    private void OnWelcomeEdenTapped(object? sender, EventArgs e)
    {
        SetEmulatorStatus(WelcomeEdenStatus, "scanning");
        _welcomeEvent?.Invoke("eden");
    }

    private void OnWelcomeAzaharTapped(object? sender, EventArgs e)
    {
        SetEmulatorStatus(WelcomeAzaharStatus, "scanning");
        _welcomeEvent?.Invoke("azahar");
    }

    private void OnWelcomeMelonDSTapped(object? sender, EventArgs e)
    {
        SetEmulatorStatus(WelcomeMelonDSStatus, "scanning");
        _welcomeEvent?.Invoke("melonds");
    }

    private void OnWelcomeRetroArchTapped(object? sender, EventArgs e)
    {
        SetEmulatorStatus(WelcomeRetroArchStatus, "scanning");
        _welcomeEvent?.Invoke("retroarch");
    }

    private void OnWelcomeManualTapped(object? sender, EventArgs e)
        => _welcomeEvent?.Invoke("manual");

    private void OnWelcomeSkipTapped(object? sender, EventArgs e)
        => _welcomeEvent?.Invoke("skip");

    private void OnWelcomeNextStep1Tapped(object? sender, EventArgs e)
    {
        if (_step1NextEnabled)
            _welcomeEvent?.Invoke("next");
        else
            _welcomeEvent?.Invoke("skip");
    }

    // ── Tap handler (Step 2) ──────────────────────────────────────────────────

    private void OnWelcomeGetStartedTapped(object? sender, EventArgs e)
        => _welcomeEvent?.Invoke("finish");

    // ── Emulator status helper ────────────────────────────────────────────────

    private static void SetEmulatorStatus(Label statusLabel, string state)
    {
        switch (state)
        {
            case "scanning":
                statusLabel.Text      = "…";
                statusLabel.TextColor = Color.FromArgb("#F0C040");
                break;
            case "found":
                statusLabel.Text      = "✓";
                statusLabel.TextColor = Color.FromArgb("#34D990");
                break;
            case "none":
                statusLabel.Text      = "✗";
                statusLabel.TextColor = Color.FromArgb("#FF6B9D");
                break;
        }
    }

    // ── Gamepad for welcome panel ─────────────────────────────────────────────

#if ANDROID
    private void OnWelcomeGamepadKey(Android.Views.Keycode keyCode, Android.Views.KeyEventActions action)
    {
        if (action != Android.Views.KeyEventActions.Down) return;
        MainThread.BeginInvokeOnMainThread(() => HandleWelcomeGamepadKey(keyCode));
    }

    private void HandleWelcomeGamepadKey(Android.Views.Keycode keyCode)
    {
        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:
                MoveWelcomeFocus(-1);
                break;
            case Android.Views.Keycode.DpadDown:
                MoveWelcomeFocus(+1);
                break;
            case Android.Views.Keycode.DpadLeft when _welcomeStep == 0:
                MoveWelcomeFocus(-1);
                break;
            case Android.Views.Keycode.DpadRight when _welcomeStep == 0:
                MoveWelcomeFocus(+1);
                break;
            case Android.Views.Keycode.ButtonA:
                ActivateWelcomeFocus();
                break;
            case Android.Views.Keycode.ButtonB:
                // Allow backing out of the welcome wizard
                _welcomeEvent?.Invoke("skip");
                break;
        }
    }

    private void MoveWelcomeFocus(int delta)
    {
        int maxFocus = _welcomeStep switch
        {
            0 => 1, // 0=dark, 1=light
            1 => 5, // 0=Eden, 1=Azahar, 2=MelonDS, 3=RetroArch, 4=Manual (5 items but index 0–4)
            _ => 0,
        };
        _welcomeFocus = Math.Clamp(_welcomeFocus + delta, 0, maxFocus);
        ApplyWelcomeFocusHighlight();
    }

    private void ApplyWelcomeFocusHighlight()
    {
        bool light       = ThemeService.Current == PkTheme.Light;
        var focusBg      = Color.FromArgb(light ? "#EEF2FF" : "#182845");
        var focusStroke  = Color.FromArgb("#4F80FF");
        var normalBg     = Color.FromArgb(light ? "#FFFFFF" : "#111827");

        if (_welcomeStep == 0)
        {
            ThemeCardDark.BackgroundColor  = _welcomeFocus == 0 ? focusBg : normalBg;
            ThemeCardLight.BackgroundColor = _welcomeFocus == 1 ? focusBg : normalBg;
        }
        else if (_welcomeStep == 1)
        {
            Border[] rows = [WelcomeRow_Eden, WelcomeRow_Azahar, WelcomeRow_MelonDS, WelcomeRow_RetroArch, WelcomeRow_Manual];
            for (int i = 0; i < rows.Length; i++)
            {
                bool focused = i == _welcomeFocus;
                rows[i].BackgroundColor = focused ? focusBg : normalBg;
                rows[i].Stroke          = focused ? focusStroke : Colors.Transparent;
            }
        }
    }

    private void ActivateWelcomeFocus()
    {
        if (_welcomeStep == 0)
        {
            if (_welcomeFocus == 0) OnDarkThemeTapped(null, EventArgs.Empty);
            else                    OnLightThemeTapped(null, EventArgs.Empty);
        }
        else if (_welcomeStep == 1)
        {
            switch (_welcomeFocus)
            {
                case 0: OnWelcomeEdenTapped(null, EventArgs.Empty);      break;
                case 1: OnWelcomeAzaharTapped(null, EventArgs.Empty);    break;
                case 2: OnWelcomeMelonDSTapped(null, EventArgs.Empty);   break;
                case 3: OnWelcomeRetroArchTapped(null, EventArgs.Empty); break;
                case 4: OnWelcomeManualTapped(null, EventArgs.Empty);    break;
            }
        }
        else if (_welcomeStep == 2)
        {
            OnWelcomeGetStartedTapped(null, EventArgs.Empty);
        }
    }
#endif

    private static void DrawGreenCursor(SKCanvas canvas, SKRect rect, float radius, float pulse)
    {
        using var fillPaint = new SKPaint { Color = new SKColor(60, 220, 110, 26), IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, fillPaint);
        using var strokePaint = new SKPaint
        {
            Color = SKColor.Parse("#3CDC6E"), Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f, IsAntialias = true,
        };
        canvas.DrawRoundRect(rect, radius, radius, strokePaint);

        float expand = 4f;
        var outerRect = new SKRect(rect.Left - expand, rect.Top - expand, rect.Right + expand, rect.Bottom + expand);
        float outerScale = 1f + 0.02f * pulse;
        canvas.Save();
        canvas.Scale(outerScale, outerScale, rect.MidX, rect.MidY);
        using var outerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(60, 220, 110, (byte)(89 + 77 * pulse)),
            StrokeWidth = 2f, IsAntialias = true,
        };
        canvas.DrawRoundRect(outerRect, 13, 13, outerPaint);
        canvas.Restore();

        var glowRect = new SKRect(rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1);
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(60, 220, 110, (byte)(38 + 26 * pulse)),
            ImageFilter = SKImageFilter.CreateBlur(7, 7), IsAntialias = true,
        };
        canvas.DrawRoundRect(glowRect, 11, 11, glowPaint);
    }
}
