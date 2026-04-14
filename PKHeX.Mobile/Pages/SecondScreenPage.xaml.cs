using System.Diagnostics;
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
        canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH), new SKSamplingOptions(SKFilterMode.Nearest), paint);
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
