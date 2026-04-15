using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using PKHeX.Core;
using PKHeX.Mobile.Models;
using PKHeX.Mobile.Services;
using PKHeX.Mobile.Theme;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

namespace PKHeX.Mobile.Pages;

public partial class GamePage : ContentPage
{
    private const int Columns = 6;
    private const int Rows    = 5;

    private readonly FileSystemSpriteRenderer _sprites = new();
    private readonly GameStrings _strings = GameInfo.GetStrings("en");

    private SaveFile? _sav;
    private PKM[] _currentBox = [];
    private int _boxIndex;
    private int _cursorSlot;
    private int _selectedSlot = -1;   // selected outline (A-confirmed), -1 = none
    private PKM? _previewPk;          // Pokémon shown in top panel (follows cursor)
    private int  _previewSpecies = -1; // debounce WebView reloads
    private bool _loadingBox;
    private bool   _spriteWebViewReady; // true after first full HTML load
    private double _canvasW, _canvasH;  // cached after first layout (TopSelectedPanel is invisible during initial layout pass)
    private bool _showLegalityBadges;
    private bool?[] _legalityCache = [];

    // X-toggle: false = info+radar (default), true = moves+compat
    private bool _detailToggled;

    // Action menu (Start button)
    private bool _menuOpen;
    private int  _menuCursor; // 0 = Save, 1 = Export, 2 = Items

    // Move mode
    private bool _moveMode;
    private PKM? _movePk;
    private int  _moveSourceBox;
    private int  _moveSourceSlot;

    // Cursor pulse animation
    private readonly Stopwatch _cursorTimer = Stopwatch.StartNew();
    private IDispatcherTimer? _pulseTimer;

    // Bounce animation: slot that just received the cursor
    private int  _bounceSlot   = -1;
    private long _bounceStartMs;

    // Box slide direction (-1 = prev box, +1 = next box, 0 = no slide)
    private int _boxSlideDir;

#if ANDROID
    // Pokémon cry playback
    private Android.Media.MediaPlayer? _cryPlayer;
    private string _lastCrySlug = "";
#endif

    // Items tab
    private bool _itemsTabActive;
    private PlayerBag? _bag;
    private int _activePouchIndex;
    private int _itemCursor = -1;
    private bool _itemEditMode;
    private List<ItemRow> _itemRows    = [];
    private List<ItemRow> _allItemRows = [];
    private string _itemSearchText    = "";
    private readonly List<Border> _pocketTabBorders = [];

    // Search / filter
    private bool _searchMode = false;
    private record struct SearchSlot(int Box, int Slot, PKM Pk);
    private SearchSlot[] _searchResults = [];
    private PKM? CursorPk => _searchMode
        ? (_cursorSlot < _searchResults.Length ? _searchResults[_cursorSlot].Pk : null)
        : (_cursorSlot < _currentBox.Length    ? _currentBox[_cursorSlot]       : null);

    // Pre-computed grid layout (recalculated on canvas size change)
    private SKRect[] _slotRects = [];
    private float _gridSlotSize;
    private float _gridOffsetX;
    private float _gridOffsetY;
    private int _lastCanvasW;
    private int _lastCanvasH;

    // Radar animation
    private float[]                  _radarCurrent = new float[6];
    private float                    _radarVisMax  = 255f;
    private CancellationTokenSource? _radarAnimCts;

    private readonly ISecondaryDisplay _secondary;
    private bool _isPhone;
    private bool _isLandscapePhone;
    private PKM? _phoneSheetPk;
    private bool _phoneSheetVisible;

    public GamePage(ISecondaryDisplay secondary)
    {
        _secondary = secondary;
        InitializeComponent();

        // Keep the radar frosted-glass box square regardless of row height.
        RadarBorder.SizeChanged += (_, _) =>
        {
            if (RadarBorder.Width > 0)
                RadarBorder.HeightRequest = RadarBorder.Width;
        };

        // Cache canvas bounds after first layout — TopSelectedPanel is invisible
        // during the initial layout pass so Width/Height are -1 until it shows.
        PreviewCanvas.SizeChanged += (_, _) =>
        {
            if (PreviewCanvas.Width > 0) _canvasW = PreviewCanvas.Width;
            if (PreviewCanvas.Height > 0) _canvasH = PreviewCanvas.Height;
        };
    }

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived        -= OnGamepadKey;
        GamepadRouter.KeyReceived        += OnGamepadKey;
        GamepadRouter.BoxScrollRequested -= OnBoxScroll;
        GamepadRouter.BoxScrollRequested += OnBoxScroll;
#endif
        var sav = App.ActiveSave;
        if (sav is null) return;

        bool freshSave = _sav != sav;
        _sav = sav;

        _isPhone = !_secondary.IsAvailable;

        // Always reset so WebView reloads cleanly after any navigation away.
        // Also reset _previewSpecies so the scale/toggle changes from Settings
        // take effect even when returning to the same Pokémon.
        _spriteWebViewReady = false;
        _previewSpecies     = -1;

        if (freshSave)
        {
            _boxIndex    = 0;
            _cursorSlot  = 0;
            DeselectSlot();

            TrainerNameLabel.Text     = sav.OT;
            SaveGameLabel.Text        = $"Pokémon {sav.Version}  ·  Gen {sav.Generation}";
            TrainerTIDLabel.Text      = sav.TrainerTID7.ToString();
            TrainerPokedexLabel.Text  = sav.BoxCount.ToString();
            TrainerPlaytimeLabel.Text = sav.PlayTimeString;

            // Set trainer circle game icon
            var iconFile = GetTrainerIconFile(sav);
            if (iconFile != null)
                TrainerGameIcon.Source = ImageSource.FromStream(
                    ct => FileSystem.OpenAppPackageFileAsync($"gameicons/{iconFile}").WaitAsync(ct));

            // Phone compact strip
            if (_isPhone)
            {
                PhoneTrainerName.Text = sav.OT;
                PhoneGameLabel.Text   = $"Pokémon {sav.Version}";
                if (iconFile != null)
                    PhoneGameIcon.Source = ImageSource.FromStream(
                        ct => FileSystem.OpenAppPackageFileAsync($"gameicons/{iconFile}").WaitAsync(ct));
            }
        }

        // If returning from bank with a withdrawn Pokémon, enter move mode
        if (App.PendingMove != null && App.PendingFromBank)
        {
            _movePk          = App.PendingMove;
            _moveSourceBox   = -1; // originated from bank
            _moveSourceSlot  = -1;
            _moveMode        = true;
            App.PendingMove  = null;
        }

        // Always reset species key so radar re-reads preferences (e.g. after returning from Settings)
        _previewSpecies = -1;
        _showLegalityBadges = Preferences.Default.Get(SettingsPage.KeyLegalityBadge, false);

        // Start cursor pulse timer (~60fps)
        if (_pulseTimer is null)
        {
            _pulseTimer = Dispatcher.CreateTimer();
            _pulseTimer.Interval = TimeSpan.FromMilliseconds(16);
            _pulseTimer.Tick += (_, _) =>
            {
                BoxCanvas.InvalidateSurface();
                _secondary.InvalidateBoxCanvas();
            };
        }
        _pulseTimer.Start();

        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        SpriteBgImage.IsVisible = true;

        _secondary.Show();

        // Apply layout for current device mode and orientation
        if (_isPhone)
            ApplyPhoneLayout(Width > Height);
        else
        {
            // Thor: top screen (Row 0 = *) + second screen collapsed (Row 1 = 0)
            RootGrid.RowDefinitions[0].Height    = GridLength.Star;
            RootGrid.RowDefinitions[1].Height    = new GridLength(0);
            RootGrid.ColumnDefinitions[0].Width  = GridLength.Star;
            RootGrid.ColumnDefinitions[1].Width  = new GridLength(0);
            TopScreenPanel.IsVisible             = true;
            PhoneTrainerStrip.IsVisible          = false;
        }

        LoadBox(_boxIndex);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CommitItems();
        // Instantly reset sheet so it's off-screen when returning
        if (_isPhone)
        {
            PhoneSheetPanel.TranslationY = 600;
            PhoneSheetScrim.Opacity = 0;
            PhoneDetailSheet.InputTransparent = true;
            _phoneSheetVisible = false;
            _phoneSheetPk = null;
        }
#if ANDROID
        GamepadRouter.KeyReceived        -= OnGamepadKey;
        GamepadRouter.BoxScrollRequested -= OnBoxScroll;
        _cryPlayer?.Stop();
        _cryPlayer?.Release();
        _cryPlayer = null;
#endif
        ThemeService.ThemeChanged -= OnThemeChanged;
        _pulseTimer?.Stop();
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (!_isPhone || Width <= 0 || Height <= 0) return;
        bool landscape = Width > Height;
        if (landscape != _isLandscapePhone)
            ApplyPhoneLayout(landscape);
    }

    /// <summary>
    /// Switches GamePage between portrait-phone and landscape-phone layouts.
    /// Portrait: trainer strip at top of box grid, bottom sheet for detail.
    /// Landscape: two-column split (TopScreenPanel left, box grid right) — mirrors Thor layout.
    /// Thor path is never touched by this method.
    /// </summary>
    private void ApplyPhoneLayout(bool landscape)
    {
        _isLandscapePhone = landscape;

        if (landscape)
        {
            // Two-column: left = TopScreenPanel (trainer + detail), right = box grid
            RootGrid.RowDefinitions[0].Height    = GridLength.Star;
            RootGrid.RowDefinitions[1].Height    = new GridLength(0);
            RootGrid.ColumnDefinitions[0].Width  = GridLength.Star;
            RootGrid.ColumnDefinitions[1].Width  = new GridLength(2, GridUnitType.Star);
            Grid.SetRow(BottomGrid, 0);
            Grid.SetColumn(BottomGrid, 1);
            TopScreenPanel.IsVisible    = true;
            PhoneTrainerStrip.IsVisible = false;

            // Dismiss bottom sheet if it was open — left panel handles detail
            if (_phoneSheetVisible)
            {
                PhoneSheetPanel.TranslationY = 600;
                PhoneSheetScrim.Opacity = 0;
                PhoneDetailSheet.InputTransparent = true;
                _phoneSheetVisible = false;
            }
        }
        else
        {
            // Portrait: trainer strip at top, box fills screen, detail via bottom sheet
            RootGrid.RowDefinitions[0].Height    = new GridLength(0);
            RootGrid.RowDefinitions[1].Height    = GridLength.Star;
            RootGrid.ColumnDefinitions[0].Width  = GridLength.Star;
            RootGrid.ColumnDefinitions[1].Width  = new GridLength(0);
            Grid.SetRow(BottomGrid, 1);
            Grid.SetColumn(BottomGrid, 0);
            TopScreenPanel.IsVisible    = false;
            PhoneTrainerStrip.IsVisible = true;
        }
    }

    private void OnThemeChanged()
    {
        SpriteBgImage.IsVisible = true;
        TopBgCanvas.InvalidateSurface();
        BoxCanvas.InvalidateSurface();
        RadarCanvas.InvalidateSurface();
    }

#if ANDROID
    private void OnBoxScroll(int dir)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (dir < 0) OnPrevBox(this, EventArgs.Empty);
            else         OnNextBox(this, EventArgs.Empty);
        });
#endif

    // ──────────────────────────────────────────────
    //  Box loading
    // ──────────────────────────────────────────────

    private async void LoadBox(int box)
    {
        if (_sav is null || _loadingBox) return;
        _loadingBox = true;
        int slideDir = _boxSlideDir;
        _boxSlideDir = 0;
        try
        {
            // Slide out existing content (only when the canvas is visible — single-screen mode)
            bool canSlide = slideDir != 0 && BoxCanvas.Width > 10;
            if (canSlide)
                await BoxCanvas.TranslateToAsync(-slideDir * BoxCanvas.Width, 0, 120, Easing.CubicIn);

            _currentBox = _sav.GetBoxData(box);
            var boxName = _sav is IBoxDetailName named
                ? named.GetBoxName(box)
                : $"Box {box + 1}";
            BoxNameLabel.Text = boxName;
            if (_isPhone) PhoneBoxLabel.Text = boxName;

            // Update idle panel box info
            IdleBoxNameLabel.Text = boxName;
            int filled = _currentBox.Count(pk => pk.Species != 0);
            IdleBoxFillLabel.Text = $"{filled} / {_currentBox.Length} filled";

            _secondary.UpdateBoxGrid(
                _currentBox, _cursorSlot, _selectedSlot,
                _moveMode, _movePk, _moveSourceBox, _moveSourceSlot,
                _boxIndex, boxName, _legalityCache, _showLegalityBadges);
            // Clear selected outline if the slot is now empty (e.g. Pokémon was moved/deleted in editor)
            if (_selectedSlot >= 0 && (_selectedSlot >= _currentBox.Length
                || _currentBox[_selectedSlot].Species == 0))
                _selectedSlot = -1;

            _legalityCache = new bool?[_currentBox.Length];

            // Update top panel immediately — don't wait for sprite preload.
            UpdateTopPanel();
            UpdateInfoBar();
            BoxCanvas.InvalidateSurface();

            await _sprites.PreloadBoxAsync(_currentBox);
            BoxCanvas.InvalidateSurface();

            if (canSlide)
            {
                // Pre-position canvas on the incoming side, then slide to rest
                BoxCanvas.TranslationX = slideDir * BoxCanvas.Width;
                await BoxCanvas.TranslateToAsync(0, 0, 140, Easing.CubicOut);
            }
            if (_showLegalityBadges) _ = RunLegalityBadgesAsync(_currentBox);
        }
        finally { _loadingBox = false; }
    }

    // ──────────────────────────────────────────────
    //  Box navigation
    // ──────────────────────────────────────────────

    private void OnPrevBox(object sender, EventArgs e)
    {
        if (_sav is null || _boxIndex <= 0) return;
        _boxSlideDir = -1;
        _boxIndex--;
        DeselectSlot();
        LoadBox(_boxIndex);
    }

    private void OnNextBox(object sender, EventArgs e)
    {
        if (_sav is null || _boxIndex >= _sav.BoxCount - 1) return;
        _boxSlideDir = +1;
        _boxIndex++;
        DeselectSlot();
        LoadBox(_boxIndex);
    }

    // ──────────────────────────────────────────────
    //  Rendering — box grid (bottom screen)
    // ──────────────────────────────────────────────

    private void RecalcGridLayout(int canvasW, int canvasH)
    {
        if (canvasW == _lastCanvasW && canvasH == _lastCanvasH && _slotRects.Length == Columns * Rows)
            return;
        _lastCanvasW = canvasW;
        _lastCanvasH = canvasH;

        const float gap = 6f;
        const float padX = 14f, padY = 4f;

        float availW = canvasW - padX * 2 - gap * (Columns - 1);
        float availH = canvasH - padY * 2 - gap * (Rows - 1);
        float slotSize = MathF.Min(availW / Columns, availH / Rows);

        float gridW = slotSize * Columns + gap * (Columns - 1);
        float gridH = slotSize * Rows + gap * (Rows - 1);
        float offX = (canvasW - gridW) / 2f;
        float offY = (canvasH - gridH) / 2f;

        _gridSlotSize = slotSize;
        _gridOffsetX = offX;
        _gridOffsetY = offY;

        _slotRects = new SKRect[Columns * Rows];
        for (int i = 0; i < _slotRects.Length; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            float x = offX + col * (slotSize + gap);
            float y = offY + row * (slotSize + gap);
            _slotRects[i] = new SKRect(x, y, x + slotSize, y + slotSize);
        }
    }

    private void OnBoxPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(ThemeService.CanvasBg);
        if (_currentBox.Length == 0) return;

        RecalcGridLayout(e.Info.Width, e.Info.Height);

        const float radius = 10f;

        // Pulse values
        float tBlue = (float)(_cursorTimer.Elapsed.TotalMilliseconds % 1800) / 1800f;
        float pulseBlue = 0.5f + 0.5f * MathF.Sin(tBlue * MathF.PI * 2);
        float tGreen = (float)(_cursorTimer.Elapsed.TotalMilliseconds % 1400) / 1400f;
        float pulseGreen = 0.5f + 0.5f * MathF.Sin(tGreen * MathF.PI * 2);

        for (int i = 0; i < _slotRects.Length; i++)
        {
            var rect = _slotRects[i];
            PKM pk;
            if (_searchMode)
            {
                if (i < _searchResults.Length) pk = _searchResults[i].Pk;
                else
                {
                    using var ep = new SKPaint { Color = ThemeService.SlotEmpty, IsAntialias = true };
                    canvas.DrawRoundRect(rect, radius, radius, ep);
                    using var eb = new SKPaint { Color = ThemeService.SlotBorder, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
                    canvas.DrawRoundRect(rect, radius, radius, eb);
                    continue;
                }
            }
            else
            {
                if (i >= _currentBox.Length) break;
                pk = _currentBox[i];
            }
            bool isCursor = i == _cursorSlot;
            bool isSelected = i == _selectedSlot && !_moveMode && !_searchMode;
            bool isSource = !_searchMode && _moveMode && _moveSourceBox == _boxIndex && i == _moveSourceSlot;
            bool filled = pk.Species != 0;

            // ── Slot background ──
            var bgColor = filled ? ThemeService.SlotFilled : ThemeService.SlotEmpty;
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawRoundRect(rect, radius, radius, bgPaint);

            // Slot border
            using var borderPaint = new SKPaint
            {
                Color = ThemeService.SlotBorder,
                Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true,
            };
            canvas.DrawRoundRect(rect, radius, radius, borderPaint);

            // ── Sprite ──
            if (filled)
            {
                var sprite = _sprites.GetSprite(pk);
                float spriteScale = isCursor ? 0.75f : 0.70f; // 108% when cursor'd (0.70*1.08≈0.75)
                // Bounce: slot that just received the cursor pops out briefly
                if (i == _bounceSlot)
                {
                    long bounceElapsed = _cursorTimer.ElapsedMilliseconds - _bounceStartMs;
                    if (bounceElapsed < 300)
                    {
                        float bt = bounceElapsed / 300f;
                        float decay = (1f - bt) * (1f - bt); // ease-out quad
                        spriteScale += 0.12f * decay;
                    }
                }
                byte alpha = isSource ? (byte)70 : (byte)255;
                DrawSprite(canvas, sprite, rect, spriteScale, alpha);
            }

            // Ghost of grabbed Pokémon at cursor as drop preview
            if (_moveMode && isCursor && _movePk != null && !isSource)
            {
                var ghost = _sprites.GetSprite(_movePk);
                DrawSprite(canvas, ghost, rect, 0.70f, 110);
            }

            // ── Cursor system ──
            if (_moveMode && isCursor)
                DrawGreenCursor(canvas, rect, radius, pulseGreen);
            else if (isSelected)
                DrawSelectedCursor(canvas, rect, radius);
            else if (isCursor)
                DrawBlueCursor(canvas, rect, radius, pulseBlue);

            // ── Legality badge ──
            if (!_searchMode && _showLegalityBadges && i < _legalityCache.Length && _legalityCache[i] is bool legal)
            {
                var dotColor = legal ? new SKColor(60, 220, 110, 230) : new SKColor(255, 82, 82, 230);
                using var glowPaint = new SKPaint
                {
                    Color = dotColor.WithAlpha(80), IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(4, 4),
                };
                float bx = rect.Right - 8f;
                float by = rect.Top + 8f;
                canvas.DrawCircle(bx, by, 5f, glowPaint);
                using var dotPaint = new SKPaint { Color = dotColor, IsAntialias = true };
                canvas.DrawCircle(bx, by, 3f, dotPaint);
            }

            // ── Held item indicator ──
            if (filled && pk.HeldItem > 0)
            {
                float ix = rect.Left + 8f;
                float iy = rect.Bottom - 8f;
                using var itemGlow = new SKPaint
                {
                    Color = new SKColor(240, 192, 64, 80), IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(3, 3),
                };
                canvas.DrawCircle(ix, iy, 4f, itemGlow);
                using var itemDot = new SKPaint { Color = new SKColor(240, 192, 64, 220), IsAntialias = true };
                canvas.DrawCircle(ix, iy, 2.5f, itemDot);
            }
        }
    }

    private static void DrawSprite(SKCanvas canvas, SKBitmap sprite, SKRect slotRect, float scale, byte alpha)
    {
        float inner = slotRect.Width * scale;
        float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
        float drawW, drawH;
        if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
        else { drawH = inner; drawW = inner * aspect; }
        float sx = slotRect.MidX - drawW / 2f;
        float sy = slotRect.MidY - drawH / 2f;
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH), paint);
    }

    private static void DrawBlueCursor(SKCanvas canvas, SKRect rect, float radius, float pulse)
    {
        // Layer 1: Fill + border
        using var fillPaint = new SKPaint { Color = new SKColor(59, 139, 255, 31), IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, fillPaint);
        using var strokePaint = new SKPaint
        {
            Color = SKColor.Parse("#5CA0FF"), Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f, IsAntialias = true,
        };
        canvas.DrawRoundRect(rect, radius, radius, strokePaint);

        // Layer 2: Outer glow ring
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

        // Layer 3: Inner blur glow
        var glowRect = new SKRect(rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1);
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(59, 139, 255, (byte)(46 + 31 * pulse)),
            ImageFilter = SKImageFilter.CreateBlur(6, 6),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(glowRect, 11, 11, glowPaint);
    }

    private static void DrawSelectedCursor(SKCanvas canvas, SKRect rect, float radius)
    {
        // Static (no pulse) — brighter blue-white to distinguish from navigation cursor
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
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(107, 171, 255, 77),
            StrokeWidth = 2f, IsAntialias = true,
        };
        canvas.DrawRoundRect(outerRect, 13, 13, outerPaint);

        var glowRect = new SKRect(rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1);
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(59, 139, 255, 51),
            ImageFilter = SKImageFilter.CreateBlur(7, 7),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(glowRect, 11, 11, glowPaint);
    }

    private static void DrawGreenCursor(SKCanvas canvas, SKRect rect, float radius, float pulse)
    {
        // Layer 1
        using var fillPaint = new SKPaint { Color = new SKColor(60, 220, 110, 26), IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, fillPaint);
        using var strokePaint = new SKPaint
        {
            Color = SKColor.Parse("#3CDC6E"), Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f, IsAntialias = true,
        };
        canvas.DrawRoundRect(rect, radius, radius, strokePaint);

        // Layer 2: Outer glow ring (pulsing)
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

        // Layer 3: Inner blur glow
        var glowRect = new SKRect(rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1);
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(60, 220, 110, (byte)(38 + 26 * pulse)),
            ImageFilter = SKImageFilter.CreateBlur(7, 7),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(glowRect, 11, 11, glowPaint);
    }

    // ──────────────────────────────────────────────
    //  Rendering — static sprite fallback (top-left)
    // ──────────────────────────────────────────────

    private void OnPreviewPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_previewPk is null) return;

        var sprite = _sprites.GetSprite(_previewPk);
        float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
        float w = e.Info.Width, h = e.Info.Height;
        float drawW, drawH;
        // Contain: fit within canvas bounds preserving aspect ratio
        if (w / h <= aspect) { drawW = w; drawH = w / aspect; }
        else                 { drawH = h; drawW = h * aspect; }
        float sx = (w - drawW) / 2f;
        float sy = (h - drawH) / 2f;
        canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH));
    }

    // ──────────────────────────────────────────────
    //  Rendering — hexagonal stat radar (top-right)
    // ──────────────────────────────────────────────

    // HP=red, Atk=orange, Def=yellow, Spe=teal, SpD=blue, SpA=purple
    private static readonly SKColor[] StatColors =
    [
        new SKColor(255,  80,  80),   // HP
        new SKColor(255, 150,  50),   // Atk
        new SKColor(240, 210,  50),   // Def
        new SKColor( 50, 210, 160),   // Spe
        new SKColor( 80, 140, 255),   // SpD
        new SKColor(185,  90, 255),   // SpA
    ];

    private void OnRadarPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_previewPk is null) return;

        const int n  = 6;
        float visMax = _radarVisMax;

        int[] ringValues =
        [
            (int)(visMax * 0.25f),
            (int)(visMax * 0.50f),
            (int)(visMax * 0.75f),
            (int)visMax,
        ];

        string[] labels = ["HP", "Atk", "Def", "Spe", "SpD", "SpA"];

        float margin = Math.Min(e.Info.Width, e.Info.Height) * 0.16f;
        float cx = e.Info.Width  / 2f;
        float cy = e.Info.Height / 2f;
        float r  = Math.Min(cx, cy) - margin;

        // ── Grid rings ──────────────────────────────────────────────────
        using var ringPaint      = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        float ringLabelSz        = Math.Max(10f, r * 0.09f);
        using var ringFont       = new SKFont(SKTypeface.Default, ringLabelSz);
        using var ringLabelPaint = new SKPaint { Color = ThemeService.RadarStat, IsAntialias = true };

        for (int ri = 0; ri < ringValues.Length; ri++)
        {
            float frac = ringValues[ri] / visMax;
            float rr   = r * frac;
            ringPaint.Color = ThemeService.RadarGrid.WithAlpha((byte)(40 + ri * 25));
            DrawHexPath(canvas, cx, cy, rr, n, ringPaint);

            float lx = cx;
            float ly = cy - rr - ringLabelSz * 0.25f;
            canvas.DrawText(ringValues[ri].ToString(), lx, ly, SKTextAlign.Center, ringFont, ringLabelPaint);
        }

        // ── Colored axes ────────────────────────────────────────────────
        using var axisPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            axisPaint.Color = StatColors[i].WithAlpha(70);
            canvas.DrawLine(cx, cy, cx + r * MathF.Cos(angle), cy + r * MathF.Sin(angle), axisPaint);
        }

        // ── Pre-compute vertex positions ────────────────────────────────
        float[] vx = new float[n];
        float[] vy = new float[n];
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float v  = Math.Clamp(_radarCurrent[i] / visMax, 0f, 1f);
            vx[i] = cx + r * v * MathF.Cos(angle);
            vy[i] = cy + r * v * MathF.Sin(angle);
        }

        // ── Colored wedge fills (triangle per stat from center) ─────────
        using var wedgePaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            using var wedgePath = new SKPath();
            wedgePath.MoveTo(cx, cy);
            wedgePath.LineTo(vx[i], vy[i]);
            wedgePath.LineTo(vx[j], vy[j]);
            wedgePath.Close();
            wedgePaint.Color = StatColors[i].WithAlpha(90);
            canvas.DrawPath(wedgePath, wedgePaint);
        }

        // ── Outline stroke ───────────────────────────────────────────────
        using var statPath = new SKPath();
        for (int i = 0; i < n; i++)
        {
            if (i == 0) statPath.MoveTo(vx[i], vy[i]);
            else        statPath.LineTo(vx[i], vy[i]);
        }
        statPath.Close();
        using var strokePaint = new SKPaint
        {
            Color = ThemeService.RadarLabel.WithAlpha(200),
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true,
        };
        canvas.DrawPath(statPath, strokePaint);

        // ── Colored vertex dots ──────────────────────────────────────────
        using var dotPaint = new SKPaint { IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            dotPaint.Color = StatColors[i];
            canvas.DrawCircle(vx[i], vy[i], 5f, dotPaint);
        }

        // ── Axis labels (name + value) ───────────────────────────────────
        float textR   = r + margin * 0.60f;
        float labelSz = Math.Max(11f, r * 0.11f);
        float valueSz = Math.Max(14f, r * 0.14f);
        using var labelFont  = new SKFont(SKTypeface.Default, labelSz);
        using var valueFont  = new SKFont(SKTypeface.Default, valueSz) { Embolden = true };
        using var namePaint  = new SKPaint { IsAntialias = true };
        using var valuePaint = new SKPaint { IsAntialias = true };

        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float lx = cx + textR * MathF.Cos(angle);
            float ly = cy + textR * MathF.Sin(angle);
            namePaint.Color  = StatColors[i].WithAlpha(180);
            valuePaint.Color = StatColors[i];
            canvas.DrawText(labels[i],                        lx, ly,                  SKTextAlign.Center, labelFont, namePaint);
            canvas.DrawText(((int)_radarCurrent[i]).ToString(), lx, ly + valueSz * 1.1f, SKTextAlign.Center, valueFont, valuePaint);
        }
    }

    private static float[] GetRadarStats(PKM pk)
    {
        var s = pk.GetStats(pk.PersonalInfo);
        // Clockwise from top: HP, Atk, Def, Spe, SpD, SpA
        return [(float)s[0], (float)s[1], (float)s[2], (float)s[5], (float)s[4], (float)s[3]];
    }

    private void StartRadarAnimation(float[] target)
    {
        bool adaptive = Preferences.Default.Get(SettingsPage.KeyRadarAdaptive, false);
        _radarVisMax = adaptive
            ? Math.Max(target.Max() / 0.78f, 80f)   // targets ~78% fill for highest stat
            : 255f;

        _radarAnimCts?.Cancel();
        _radarAnimCts = new CancellationTokenSource();
        var ct    = _radarAnimCts.Token;
        var start = _radarCurrent.ToArray();

        _ = Task.Run(async () =>
        {
            const int durationMs = 380;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                float raw  = (float)sw.ElapsedMilliseconds / durationMs;
                float t    = Math.Clamp(raw, 0f, 1f);
                // Ease in-out cubic
                float ease = t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;

                for (int i = 0; i < 6; i++)
                    _radarCurrent[i] = start[i] + (target[i] - start[i]) * ease;

                MainThread.BeginInvokeOnMainThread(() => RadarCanvas.InvalidateSurface());

                if (t >= 1f) break;
                await Task.Delay(16, ct).ConfigureAwait(false);
            }
        }, ct);
    }

    private static void DrawHexPath(SKCanvas canvas, float cx, float cy, float r, int n, SKPaint paint)
    {
        using var path = new SKPath();
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float x = cx + r * MathF.Cos(angle);
            float y = cy + r * MathF.Sin(angle);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        path.Close();
        canvas.DrawPath(path, paint);
    }

    // ──────────────────────────────────────────────
    //  Animated sprite (cached from Showdown CDN)
    // ──────────────────────────────────────────────

    private async void LoadAnimatedSprite(PKM pk)
    {
        if (!Preferences.Default.Get(SettingsPage.KeyAnimated3D, true))
        {
            // 3D sprites disabled — show static HOME sprite only
            PreviewCanvas.IsVisible = true;
            PreviewCanvas.InvalidateSurface();
            return;
        }
        var name = pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species]
            : pk.Species.ToString();

        var speciesSlug = ToShowdownSlug(name);
        var shiny       = pk.IsShiny;

        // Build form-specific slug (e.g. "giratina-origin", "meloetta-pirouette")
        string slug = speciesSlug;
        string? baseSlug = null;
        if (pk.Form != 0)
        {
            var formName = ShowdownParsing.GetStringFromForm(pk.Form, _strings, pk.Species, pk.Context);
            var formSuffix = ToShowdownFormSuffix(formName);
            if (formSuffix.Length > 0)
            {
                slug     = $"{speciesSlug}-{formSuffix}";
                baseSlug = speciesSlug; // fallback if CDN doesn't have this form
            }
        }

        // Fetch from disk cache or download; falls back to static canvas if unavailable
        var dataUri = await Services.SpriteCacheService.GetDataUriAsync(slug, shiny);
        // Form sprite not on CDN — fall back to base-form animated sprite
        if (dataUri is null && baseSlug is not null)
            dataUri = await Services.SpriteCacheService.GetDataUriAsync(baseSlug, shiny);
        if (dataUri is null)
        {
            // Nothing cached and download failed — show static sprite, don't break WebView state
            PreviewCanvas.IsVisible = true;
            PreviewCanvas.InvalidateSurface();
            return;
        }

        // Use cached canvas bounds — PreviewCanvas.Width is -1 during the first call
        // because TopSelectedPanel is invisible during the initial layout pass.
        // _canvasW/_canvasH are set by SizeChanged once the panel actually renders.
        int spriteW = (int)Math.Max(_canvasW * 0.8, 80);
        int spriteH = (int)Math.Max(_canvasH * 0.8, 80);

        if (!_spriteWebViewReady)
        {
            SpriteWebView.IsVisible = true;
            PreviewCanvas.IsVisible = false;
            await Task.Delay(50);
            SpriteWebView.Source    = new HtmlWebViewSource { Html = BuildSpriteShell(dataUri, spriteW, spriteH) };
            _spriteWebViewReady     = true;
        }
        else
        {
            var js = $$"""
                var s=document.getElementById('s');
                s.src='{{dataUri}}';
                s.style.width='{{spriteW}}px';
                s.style.height='{{spriteH}}px';
                """;
            await SpriteWebView.EvaluateJavaScriptAsync(js);
        }
    }

    private static string ToShowdownSlug(string speciesName)
        => Services.SpriteCacheService.ToShowdownSlug(speciesName);

    private static string ToShowdownFormSuffix(string formName)
        => Services.SpriteCacheService.ToShowdownFormSuffix(formName);

    private static string BuildSpriteShell(string src, int w, int h) => $$"""
        <!DOCTYPE html>
        <html><head>
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <style>*{margin:0;padding:0}body{background:transparent;display:flex;align-items:center;justify-content:center;width:100vw;height:100vh;overflow:hidden}</style>
        </head><body>
        <img id="s" src="{{src}}"
             style="width:{{w}}px;height:{{h}}px;object-fit:contain;image-rendering:pixelated;filter:drop-shadow(0 2px 6px rgba(0,0,0,0.6))">
        </body></html>
        """;

    // ──────────────────────────────────────────────
    //  Pokémon cry (Android)
    // ──────────────────────────────────────────────

#if ANDROID
    private void PlayCryForCurrentSlot()
    {
        if (_previewPk?.Species > 0)
        {
            var name = _previewPk.Species < _strings.specieslist.Length
                ? _strings.specieslist[_previewPk.Species]
                : _previewPk.Species.ToString();
            _lastCrySlug = ""; // clear debounce so R3 always re-plays
            PlayCry(name);
        }
    }

    private async void PlayCry(string speciesName)
    {
        var slug = ToShowdownSlug(speciesName);
        if (slug == _lastCrySlug) return;
        _lastCrySlug = slug;

        var cryDir  = System.IO.Path.Combine(FileSystem.CacheDirectory, "cries");
        var cryPath = System.IO.Path.Combine(cryDir, slug + ".ogg");

        if (!File.Exists(cryPath))
        {
            Directory.CreateDirectory(cryDir);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var bytes = await http.GetByteArrayAsync(
                    $"https://play.pokemonshowdown.com/audio/cries/{slug}.ogg");
                await File.WriteAllBytesAsync(cryPath, bytes);
            }
            catch { return; }
        }

        // Guard: species may have changed while we were downloading
        if (_lastCrySlug != slug) return;

        _cryPlayer?.Stop();
        _cryPlayer?.Reset();
        _cryPlayer?.Release();
        _cryPlayer = new Android.Media.MediaPlayer();
        try
        {
            _cryPlayer.SetDataSource(cryPath);
            _cryPlayer.Prepare();
            _cryPlayer.Start();
        }
        catch { _cryPlayer = null; }
    }
#endif

    // ──────────────────────────────────────────────
    //  Top screen background (gradient glow)
    // ──────────────────────────────────────────────

    private void OnTopBgPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float w = e.Info.Width;
        float h = e.Info.Height;

        // Semi-transparent overlay so text stays readable over the background image.
        // Light mode uses a heavier white wash to keep contrast; dark uses a light dark tint.
        var overlayColor = ThemeService.Current == PkTheme.Light
            ? new SKColor(242, 244, 248, 80)    // ~31% white wash — image shows through
            : new SKColor(7, 12, 26, 60);        // ~24% dark tint
        using var dimPaint = new SKPaint { Color = overlayColor };
        canvas.DrawRect(0, 0, w, h, dimPaint);

        // Radial glow — game accent color at low opacity
        var gameColor = _sav != null
            ? Theme.GameColors.Get(_sav.Version).Light
            : new SKColor(59, 139, 255);
        using var glowPaint1 = new SKPaint();
        glowPaint1.Shader = SKShader.CreateRadialGradient(
            new SKPoint(w * 0.2f, h * 0.5f), Math.Min(w, h) * 0.5f,
            [gameColor.WithAlpha(20), SKColors.Transparent],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, glowPaint1);

        // Radial glow — AccentPurple
        using var glowPaint2 = new SKPaint();
        glowPaint2.Shader = SKShader.CreateRadialGradient(
            new SKPoint(w * 0.8f, h * 0.3f), Math.Min(w, h) * 0.45f,
            [new SKColor(167, 139, 250, 15), SKColors.Transparent],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, glowPaint2);
    }

    // ──────────────────────────────────────────────
    //  Type badges
    // ──────────────────────────────────────────────

    private void UpdateTypeBadges(PKM pk)
    {
        TypeBadgeRow.Children.Clear();
        var types = new List<int> { pk.PersonalInfo.Type1 };
        if (pk.PersonalInfo.Type2 != pk.PersonalInfo.Type1)
            types.Add(pk.PersonalInfo.Type2);

        foreach (var typeId in types)
        {
            var typeName = typeId < _strings.types.Length ? _strings.types[typeId] : "???";
            var color = Theme.TypeColors.Map.TryGetValue(typeName, out var c)
                ? Color.FromUint((uint)((c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue))
                : Color.FromArgb("#A8A878");

            var badge = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                BackgroundColor = color,
                Stroke = Colors.Transparent,
                Padding = new Thickness(10, 2),
                Content = new Label
                {
                    Text = typeName.ToUpperInvariant(),
                    FontFamily = "NunitoExtraBold",
                    FontSize = 9,
                    TextColor = Colors.White,
                    CharacterSpacing = 0.5,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                },
            };
            TypeBadgeRow.Children.Add(badge);
        }
    }

    // ──────────────────────────────────────────────
    //  Move rows
    // ──────────────────────────────────────────────

    private void UpdateMoveRows(PKM pk)
    {
        var moveNames = new[] { MoveName0, MoveName1, MoveName2, MoveName3 };
        var moveCats = new[] { MoveCat0, MoveCat1, MoveCat2, MoveCat3 };
        var movePPs = new[] { MovePP0, MovePP1, MovePP2, MovePP3 };
        var moveDots = new[] { MoveDot0, MoveDot1, MoveDot2, MoveDot3 };
        var moveRows = new[] { MoveRow0, MoveRow1, MoveRow2, MoveRow3 };

        int[] moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4];
        int[] pps = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];

        for (int i = 0; i < 4; i++)
        {
            if (moves[i] == 0)
            {
                moveRows[i].IsVisible = false;
                continue;
            }
            moveRows[i].IsVisible = true;

            var moveName = moves[i] < _strings.movelist.Length
                ? _strings.movelist[moves[i]] : $"Move {moves[i]}";
            moveNames[i].Text = moveName;
            movePPs[i].Text = $"PP {pps[i]}";

            // Move type dot color
            var ctx = _sav?.Context ?? EntityContext.Gen9;
            var moveType = MoveInfo.GetType((ushort)moves[i], ctx);
            var moveTypeName = moveType < _strings.types.Length
                ? _strings.types[moveType] : "Normal";
            if (Theme.TypeColors.Map.TryGetValue(moveTypeName, out var typeColor))
                moveDots[i].Fill = new SolidColorBrush(
                    Color.FromUint((uint)((typeColor.Alpha << 24) | (typeColor.Red << 16) | (typeColor.Green << 8) | typeColor.Blue)));

            // Category placeholder
            moveCats[i].Text = "";
        }
    }

    // ──────────────────────────────────────────────
    //  Trainer icon helper
    // ──────────────────────────────────────────────

    private static string? GetTrainerIconFile(SaveFile sav)
    {
        // SAV4HGSS always reports GameVersion.HGSS — never HG or SS individually.
        // Detect from the first Pokémon with a known origin game in party or box 1.
        if (sav.Version == GameVersion.HGSS)
        {
            var probe = sav.PartyData.FirstOrDefault(p => p.Species > 0 && (p.Version == GameVersion.HG || p.Version == GameVersion.SS))
                     ?? sav.GetBoxData(0).FirstOrDefault(p => p.Species > 0 && (p.Version == GameVersion.HG || p.Version == GameVersion.SS));
            if (probe?.Version == GameVersion.SS) return "soulsilver.png";
            return "heartgold.png"; // default HG if can't determine
        }
        return GetTrainerIconFile(sav.Version);
    }

    private static string? GetTrainerIconFile(GameVersion v) => v switch
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

    // ──────────────────────────────────────────────
    //  Touch input
    // ──────────────────────────────────────────────

    private void OnBoxTapped(object sender, TappedEventArgs e)
    {
        if (_sav is null || sender is not View view) return;

        var point = e.GetPosition(view);
        if (point is null) return;

        // Use density to convert dp touch coords to pixel coords
        float density = (float)DeviceDisplay.MainDisplayInfo.Density;
        float px = (float)point.Value.X * density;
        float py = (float)point.Value.Y * density;

        // Hit-test against pre-computed slot rects
        int index = -1;
        int hitMax = _searchMode ? _searchResults.Length : _currentBox.Length;
        for (int i = 0; i < _slotRects.Length && i < hitMax; i++)
        {
            if (_slotRects[i].Contains(px, py)) { index = i; break; }
        }

        if ((uint)index >= (uint)hitMax) return;

        _cursorSlot = index;

        if (_searchMode)
        {
            NavigateToSearchResult(index);
            return;
        }

        if (_moveMode)
        {
            ExecuteMove();
            return;
        }

        UpdateTopPanel();
        UpdateInfoBar();

        if (_currentBox[index].Species != 0)
        {
            if (index == _selectedSlot) _ = OpenEditor();
            else SelectSlot(index);
        }
        else
        {
            DeselectSlot();
        }
    }

    private void NavigateToSearchResult(int index)
    {
        if ((uint)index >= (uint)_searchResults.Length) return;
        var result = _searchResults[index];
        _searchMode = false;
        _searchResults = [];
        MainThread.BeginInvokeOnMainThread(() => SearchEntry.Text = "");
        _boxIndex    = result.Box;
        _cursorSlot  = result.Slot;
        _boxSlideDir = 0;
        LoadBox(result.Box);
    }

    // ──────────────────────────────────────────────
    //  Gamepad input
    // ──────────────────────────────────────────────

#if ANDROID
    private void OnGamepadKey(Android.Views.Keycode keyCode, Android.Views.KeyEventActions action)
    {
        if (action != Android.Views.KeyEventActions.Down) return;
        MainThread.BeginInvokeOnMainThread(() => HandleGamepadKey(keyCode));
    }

    private void HandleGamepadKey(Android.Views.Keycode keyCode)
    {
        // Items tab intercepts all input while active
        if (_itemsTabActive) { HandleItemsKey(keyCode); return; }

        // Action menu intercepts all input while open
        if (_menuOpen)
        {
            switch (keyCode)
            {
                case Android.Views.Keycode.DpadUp:
                case Android.Views.Keycode.DpadLeft:
                    _menuCursor = Math.Max(0, _menuCursor - 1); UpdateMenuHighlight(); break;
                case Android.Views.Keycode.DpadDown:
                case Android.Views.Keycode.DpadRight:
                    _menuCursor = Math.Min(2, _menuCursor + 1); UpdateMenuHighlight(); break;
                case Android.Views.Keycode.ButtonA:
                    if      (_menuCursor == 0) OnSaveClicked(this, EventArgs.Empty);
                    else if (_menuCursor == 1) OnExportClicked(this, EventArgs.Empty);
                    else                       OnItemsMenuClicked(this, EventArgs.Empty);
                    CloseActionMenu(); break;
                case Android.Views.Keycode.ButtonB:
                case Android.Views.Keycode.ButtonStart:
                    CloseActionMenu(); break;
            }
            return;
        }

        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:    MoveCursor(-Columns); break;
            case Android.Views.Keycode.DpadDown:  MoveCursor(+Columns); break;
            case Android.Views.Keycode.DpadLeft:  MoveCursor(-1);       break;
            case Android.Views.Keycode.DpadRight: MoveCursor(+1);       break;

            case Android.Views.Keycode.ButtonA:
                if (_moveMode) { ExecuteMove(); break; }
                if (_searchMode) { NavigateToSearchResult(_cursorSlot); break; }
                if (_cursorSlot < _currentBox.Length && _currentBox[_cursorSlot].Species != 0)
                {
                    if (_cursorSlot == _selectedSlot) _ = OpenEditor();
                    else SelectSlot(_cursorSlot);
                }
                break;

            case Android.Views.Keycode.ButtonB:
                if (_moveMode) { CancelMoveMode(); break; }
                if (_selectedSlot >= 0) DeselectSlot();
                else OnMenuClicked(this, EventArgs.Empty);
                break;

            case Android.Views.Keycode.ButtonL1: _ = SwapToBank(-1); break;
            case Android.Views.Keycode.ButtonR1: _ = SwapToBank(+1); break;

            case Android.Views.Keycode.ButtonX: OnSearchClicked(this, EventArgs.Empty); break;
            case Android.Views.Keycode.ButtonY:
                if (_moveMode) { CancelMoveMode(); break; }
                if (_searchMode) break;
                if (_cursorSlot < _currentBox.Length && _currentBox[_cursorSlot].Species != 0)
                { EnterMoveMode(); break; }
                OnGiftsClicked(this, EventArgs.Empty);
                break;
            case Android.Views.Keycode.ButtonSelect: OnSettingsClicked(this, EventArgs.Empty); break;
            case Android.Views.Keycode.ButtonStart:  OpenActionMenu(); break;
            case Android.Views.Keycode.ButtonThumbr: PlayCryForCurrentSlot(); break;
        }
    }
#endif

    private static void Haptic() { try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { } }

    private void MoveCursor(int delta)
    {
        if (delta == -1 && _cursorSlot % Columns == 0)           return;
        if (delta == +1 && _cursorSlot % Columns == Columns - 1) return;

        int next = _cursorSlot + delta;
        int maxSlots = _searchMode ? _searchResults.Length : _currentBox.Length;
        if ((uint)next >= (uint)maxSlots) return;

        Haptic();
        _cursorSlot    = next;
        _bounceSlot    = next;
        _bounceStartMs = _cursorTimer.ElapsedMilliseconds;
        UpdateTopPanel();
        UpdateInfoBar();
        BoxCanvas.InvalidateSurface();
        _secondary.UpdateCursor(_cursorSlot, _selectedSlot, _moveMode, _movePk, _boxIndex);
    }

    private void OpenActionMenu()
    {
        _menuOpen   = true;
        _menuCursor = 0;

        // Apply theme colors in code — DynamicResource doesn't update on hidden elements
        ActionMenuPanel.BackgroundColor = ThemeColor("ThActionMenuPanel", "#0D1528");
        ActionMenuPanel.Stroke          = ThemeColor("ThActionMenuStroke", "#2A3F70");
        var textPrimary = ThemeColor("ThTextPrimary", "#EDF0FF");
        foreach (var label in new[] { SaveMenuLabel, ExportMenuLabel, ItemsMenuLabel })
            label.TextColor = textPrimary;

        ActionMenuOverlay.Opacity = 0;
        ActionMenuPanel.TranslationY = 48;
        ActionMenuOverlay.IsVisible = true;
        UpdateMenuHighlight();
        _ = ActionMenuOverlay.FadeToAsync(1, 180, Easing.CubicOut);
        _ = ActionMenuPanel.TranslateToAsync(0, 0, 180, Easing.CubicOut);
    }

    private async void CloseActionMenu()
    {
        _menuOpen = false;
        await Task.WhenAll(
            ActionMenuOverlay.FadeToAsync(0, 130, Easing.CubicIn),
            ActionMenuPanel.TranslateToAsync(0, 36, 130, Easing.CubicIn));
        ActionMenuOverlay.IsVisible = false;
        ActionMenuOverlay.Opacity   = 1;
        ActionMenuPanel.TranslationY = 0;
    }

    private static Color ThemeColor(string key, string fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c)
            return c;
        return Color.FromArgb(fallback);
    }

    private void UpdateMenuHighlight()
    {
        var normalBg  = ThemeColor("ThSettingsRow",      "#111827");
        var focusBg   = ThemeColor("ThSettingsRowFocus", "#182845");
        var accent    = ThemeColor("ThAccent",           "#3B8BFF");

        MenuItem_Save.BackgroundColor   = _menuCursor == 0 ? focusBg : normalBg;
        MenuItem_Export.BackgroundColor = _menuCursor == 1 ? focusBg : normalBg;
        MenuItem_Items.BackgroundColor  = _menuCursor == 2 ? focusBg : normalBg;
        MenuItem_Save.Stroke            = _menuCursor == 0 ? accent  : Colors.Transparent;
        MenuItem_Export.Stroke          = _menuCursor == 1 ? accent  : Colors.Transparent;
        MenuItem_Items.Stroke           = _menuCursor == 2 ? accent  : Colors.Transparent;
    }

    // ──────────────────────────────────────────────
    //  Top panel — follows cursor
    // ──────────────────────────────────────────────

    private void UpdateInfoBar()
    {
        if (_currentBox.Length == 0)
        {
            InfoSpeciesNum.Text = "";
            InfoSpeciesName.Text = "Empty box";
            return;
        }

        var pk = CursorPk;
        if (pk?.Species > 0)
        {
            var name = pk.Species < _strings.specieslist.Length
                ? _strings.specieslist[pk.Species] : pk.Species.ToString();
            InfoSpeciesNum.Text = $"#{pk.Species:000}";
            InfoSpeciesName.Text = _searchMode
                ? name
                : _moveMode
                    ? $"Moving {name}..."
                    : $"{name} · Lv.{pk.CurrentLevel}";
        }
        else
        {
            InfoSpeciesNum.Text = "";
            InfoSpeciesName.Text = _searchMode ? $"{_searchResults.Length} results" : "Empty slot";
        }
    }

    private void UpdateTopPanel()
    {
        if (!_searchMode && _currentBox.Length == 0) return;
        var pk = CursorPk;
        if (pk?.Species > 0)
            ShowPokemonPreview(pk);
        else
            ShowIdlePanel();
    }

    private void ShowPokemonPreview(PKM pk)
    {
        _previewPk = pk;

        var speciesName = pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species] : pk.Species.ToString();
        var natureName = (int)pk.Nature < _strings.natures.Length
            ? _strings.natures[(int)pk.Nature] : "";

        // Name plate
        DetailSpeciesName.Text = speciesName + (pk.IsShiny ? "  ✦" : "");
        DetailLevelNature.Text = $"Lv.{pk.CurrentLevel}  ·  {natureName}";

        // Type badges
        UpdateTypeBadges(pk);

        // Moves
        UpdateMoveRows(pk);

        // Info panel fields
        DetailAbility.Text = pk.Ability < _strings.abilitylist.Length
            ? _strings.abilitylist[pk.Ability] : "—";
        DetailItem.Text = pk.HeldItem > 0 && pk.HeldItem < _strings.itemlist.Length
            ? _strings.itemlist[pk.HeldItem] : "None";
        DetailGender.Text = pk.Gender switch { 0 => "♂ Male", 1 => "♀ Female", _ => "—" };
        DetailBall.Text = pk.Ball > 0 && pk.Ball < _strings.balllist.Length
            ? _strings.balllist[pk.Ball] : "—";

        // Apply current toggle state
        ApplyDetailToggle();

        TopIdlePanel.IsVisible     = false;
        TopSelectedPanel.IsVisible = true;

        // Only reload WebView if the species/form/shiny changed (avoid flicker on cursor move)
        int key = (pk.Species << 10) | (pk.Form << 1) | (pk.IsShiny ? 1 : 0);
        if (key != _previewSpecies)
        {
            _previewSpecies = key;
            LoadAnimatedSprite(pk);
        }
        else if (_spriteWebViewReady && Preferences.Default.Get(SettingsPage.KeyAnimated3D, true))
        {
            SpriteWebView.IsVisible = true;
            PreviewCanvas.IsVisible = false;
        }

        PreviewCanvas.InvalidateSurface();
        StartRadarAnimation(GetRadarStats(pk));
    }

    private void ShowIdlePanel()
    {
        _previewPk = null;
        _detailToggled = false; // reset toggle when cursor leaves a Pokémon
        SpriteWebView.IsVisible    = false;
        PreviewCanvas.IsVisible    = true;
        TopIdlePanel.IsVisible     = true;
        TopSelectedPanel.IsVisible = false;
    }

    // ──────────────────────────────────────────────
    //  Phone detail bottom sheet
    // ──────────────────────────────────────────────

    private async Task ShowPhoneDetailSheetAsync(PKM pk)
    {
        _phoneSheetPk = pk;
        UpdatePhoneSheetContent(pk);

        PhoneDetailSheet.InputTransparent = false;
        PhoneSheetScrim.InputTransparent  = false;

        // Parallel: fade scrim in + slide panel up
        var fadeIn = PhoneSheetScrim.FadeTo(0.55, 220, Easing.CubicOut);
        var slideUp = PhoneSheetPanel.TranslateTo(0, 0, 260, Easing.CubicOut);
        await Task.WhenAll(fadeIn, slideUp);

        _phoneSheetVisible = true;
    }

    private async Task HidePhoneDetailSheetAsync()
    {
        _phoneSheetVisible = false;
        PhoneSheetScrim.InputTransparent = true;

        var fadeOut = PhoneSheetScrim.FadeTo(0, 180, Easing.CubicIn);
        var slideDown = PhoneSheetPanel.TranslateTo(0, 600, 220, Easing.CubicIn);
        await Task.WhenAll(fadeOut, slideDown);

        PhoneDetailSheet.InputTransparent = true;
        _phoneSheetPk = null;
    }

    private void UpdatePhoneSheetContent(PKM pk)
    {
        var speciesName = pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species] : pk.Species.ToString();
        var natureName = (int)pk.Nature < _strings.natures.Length
            ? _strings.natures[(int)pk.Nature] : "";

        PhoneSheetSpecies.Text    = speciesName + (pk.IsShiny ? "  ✦" : "");
        PhoneSheetLevelNature.Text = $"Lv.{pk.CurrentLevel}  ·  {natureName}";

        // Type badges
        PhoneSheetTypeBadges.Children.Clear();
        var types = new List<int> { pk.PersonalInfo.Type1 };
        if (pk.PersonalInfo.Type2 != pk.PersonalInfo.Type1)
            types.Add(pk.PersonalInfo.Type2);
        foreach (var typeId in types)
        {
            var typeName = typeId < _strings.types.Length ? _strings.types[typeId] : "???";
            var color = Theme.TypeColors.Map.TryGetValue(typeName, out var c)
                ? Color.FromUint((uint)((c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue))
                : Color.FromArgb("#A8A878");
            PhoneSheetTypeBadges.Children.Add(new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                BackgroundColor = color,
                Stroke = Colors.Transparent,
                Padding = new Thickness(10, 2),
                Content = new Label
                {
                    Text = typeName.ToUpperInvariant(),
                    FontFamily = "NunitoExtraBold",
                    FontSize = 9,
                    TextColor = Colors.White,
                    CharacterSpacing = 0.5,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                },
            });
        }

        // Moves
        var phoneNames = new[] { PhoneMoveName0, PhoneMoveName1, PhoneMoveName2, PhoneMoveName3 };
        var phonePPs   = new[] { PhoneMovePP0, PhoneMovePP1, PhoneMovePP2, PhoneMovePP3 };
        var phoneDots  = new[] { PhoneMoveDot0, PhoneMoveDot1, PhoneMoveDot2, PhoneMoveDot3 };
        var phoneRows  = new[] { PhoneMoveRow0, PhoneMoveRow1, PhoneMoveRow2, PhoneMoveRow3 };
        int[] moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4];
        int[] pps   = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];
        var ctx = _sav?.Context ?? EntityContext.Gen9;
        for (int i = 0; i < 4; i++)
        {
            if (moves[i] == 0) { phoneRows[i].IsVisible = false; continue; }
            phoneRows[i].IsVisible = true;
            phoneNames[i].Text = moves[i] < _strings.movelist.Length
                ? _strings.movelist[moves[i]] : $"Move {moves[i]}";
            phonePPs[i].Text = $"PP {pps[i]}";
            var moveTypeName = MoveInfo.GetType((ushort)moves[i], ctx) is var mt && mt < _strings.types.Length
                ? _strings.types[mt] : "Normal";
            if (Theme.TypeColors.Map.TryGetValue(moveTypeName, out var tc))
                phoneDots[i].Fill = new SolidColorBrush(
                    Color.FromUint((uint)((tc.Alpha << 24) | (tc.Red << 16) | (tc.Green << 8) | tc.Blue)));
        }

        PhoneSheetCanvas.InvalidateSurface();
    }

    private void OnPhoneSheetSpritePaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_phoneSheetPk is null) return;
        var sprite = _sprites.GetSprite(_phoneSheetPk);
        float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
        float w = e.Info.Width, h = e.Info.Height;
        float drawW, drawH;
        if (w / h <= aspect) { drawW = w; drawH = w / aspect; }
        else                 { drawH = h; drawW = h * aspect; }
        canvas.DrawBitmap(sprite, SKRect.Create((w - drawW) / 2f, (h - drawH) / 2f, drawW, drawH));
    }

    private void OnPhoneSheetScrimTapped(object? sender, TappedEventArgs e) => DeselectSlot();

    private void OnPhoneSheetEditTapped(object? sender, TappedEventArgs e)
    {
        DeselectSlot();
        _ = OpenEditor();
    }

    // ──────────────────────────────────────────────
    //  X toggle: info+radar  ↔  moves+compat
    // ──────────────────────────────────────────────

    private void ToggleDetailView()
    {
        if (_previewPk is null) return;
        _detailToggled = !_detailToggled;
        ApplyDetailToggle();
        if (_detailToggled)
            UpdateCompatPanel(_previewPk);
    }

    private void ApplyDetailToggle()
    {
        // Left column only — moves are always shown in right column
        RadarBorder.IsVisible = !_detailToggled;
        CompatPanel.IsVisible =  _detailToggled;
    }

    private void UpdateCompatPanel(PKM pk)
    {
        // Clear existing rows (keep the header label at index 0)
        while (CompatList.Children.Count > 1)
            CompatList.Children.RemoveAt(1);

        var saves = App.LoadedSaves;
        if (saves.Count == 0)
        {
            CompatList.Children.Add(new Label
            {
                Text = "No other saves loaded",
                FontFamily = "Nunito",
                FontSize = 10,
                TextColor = Color.FromArgb("#88AABBCC"),
            });
            return;
        }

        foreach (var save in saves)
        {
            var status = GetCompatStatus(pk, save);
            var dotColor = status switch
            {
                CompatStatus.Green  => Color.FromArgb("#4ADE80"),
                CompatStatus.Yellow => Color.FromArgb("#FACC15"),
                _                   => Color.FromArgb("#F87171"),
            };
            var statusText = status switch
            {
                CompatStatus.Green  => "Direct",
                CompatStatus.Yellow => "Transfer",
                _                   => "Incompatible",
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)),
                ColumnSpacing = 6,
            };

            row.Add(new Ellipse
            {
                Fill = new SolidColorBrush(dotColor),
                WidthRequest = 8, HeightRequest = 8,
                VerticalOptions = LayoutOptions.Center,
            }, 0, 0);

            var nameStack = new VerticalStackLayout { Spacing = 0 };
            nameStack.Add(new Label
            {
                Text = save.FileName,
                FontFamily = "NunitoBold",
                FontSize = 11,
                TextColor = Color.FromArgb("#EDF0FF"),
                LineBreakMode = LineBreakMode.TailTruncation,
            });
            nameStack.Add(new Label
            {
                Text = $"{save.Version}  ·  {save.TrainerName}",
                FontFamily = "Nunito",
                FontSize = 9,
                TextColor = Color.FromArgb("#7080A0"),
            });
            row.Add(nameStack, 1, 0);

            row.Add(new Label
            {
                Text = statusText,
                FontFamily = "Nunito",
                FontSize = 9,
                TextColor = dotColor,
                VerticalOptions = LayoutOptions.Center,
            }, 2, 0);

            var rowBorder = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = Color.FromArgb("#0D1A33"),
                Stroke = Color.FromArgb("#1A2A44"),
                StrokeThickness = 1,
                Padding = new Thickness(8, 6),
                Content = row,
            };
            CompatList.Children.Add(rowBorder);
        }
    }

    private enum CompatStatus { Green, Yellow, Red }

    private static CompatStatus GetCompatStatus(PKM pk, SaveEntry save)
    {
        // Same format/generation: directly placeable
        if (save.Generation == pk.Format)
            return CompatStatus.Green;

        // Different generation: check forward-transfer path
        if (EntityConverter.IsConvertibleToFormat(pk, (byte)save.Generation))
            return CompatStatus.Yellow;

        return CompatStatus.Red;
    }

    // ──────────────────────────────────────────────
    //  Selection state machine (selected outline only)
    // ──────────────────────────────────────────────

    // ──────────────────────────────────────────────
    //  Move mode
    // ──────────────────────────────────────────────

    private void EnterMoveMode()
    {
        if (_sav is null) return;
        if (_cursorSlot >= _currentBox.Length) return;
        if (_currentBox[_cursorSlot].Species == 0) return;
        _movePk         = _currentBox[_cursorSlot].Clone();
        _moveSourceBox  = _boxIndex;
        _moveSourceSlot = _cursorSlot;
        _moveMode       = true;
        BoxCanvas.InvalidateSurface();
    }

    private void CancelMoveMode()
    {
        _moveMode = false;
        _movePk   = null;
        App.PendingSourceBox = -1; // release bank reference if cancelled
        BoxCanvas.InvalidateSurface();
    }

    private void ExecuteMove()
    {
        if (_movePk is null || _sav is null) return;

        if (_moveSourceBox == -1)
        {
            // Withdraw from bank: place in game slot, then clear the bank slot
            _sav.SetBoxSlotAtIndex(_movePk, _boxIndex, _cursorSlot);
            if (App.PendingSourceBox >= 0)
            {
                new Services.BankService().ClearSlot(App.PendingSourceBox, App.PendingSourceSlot);
                App.PendingSourceBox = -1;
            }
        }
        else
        {
            // Box-to-box swap
            var destPk = _currentBox[_cursorSlot];
            _sav.SetBoxSlotAtIndex(_movePk, _boxIndex, _cursorSlot);
            _sav.SetBoxSlotAtIndex(destPk, _moveSourceBox, _moveSourceSlot);
        }

        CancelMoveMode();
        DeselectSlot();
        _previewSpecies = -1;
        LoadBox(_boxIndex);
    }

    private async Task SwapToBank(int dir)
    {
        if (_moveMode && _movePk != null)
        {
            App.PendingMove     = _movePk;
            App.PendingFromBank = _moveSourceBox == -1;
            if (!App.PendingFromBank)
            {
                App.PendingSourceBox  = _moveSourceBox;
                App.PendingSourceSlot = _moveSourceSlot;
            }
            _moveMode = false;
            _movePk   = null;
        }

        App.BankSlideDir = dir;
        await Shell.Current.GoToAsync(nameof(BankPage), false);
    }

    private async Task RunLegalityBadgesAsync(PKM[] snapshot)
    {
        var results = await Task.Run(() =>
            snapshot.Select(pk => pk.Species == 0 ? (bool?)null
                                                   : (bool?)new PKHeX.Core.LegalityAnalysis(pk).Valid)
                    .ToArray());
        _legalityCache = results;
        MainThread.BeginInvokeOnMainThread(() => BoxCanvas.InvalidateSurface());
    }

    /// <summary>Mark slot with selected outline (does not change the top panel display).</summary>
    private void SelectSlot(int slot)
    {
        if (_currentBox[slot].Species == 0) return;
        _selectedSlot = slot;
        BoxCanvas.InvalidateSurface();
        if (_isPhone && !_isLandscapePhone) _ = ShowPhoneDetailSheetAsync(_currentBox[slot]);
    }

    /// <summary>Clear selected outline.</summary>
    private void DeselectSlot()
    {
        _selectedSlot = -1;
        BoxCanvas.InvalidateSurface();
        if (_isPhone && !_isLandscapePhone && _phoneSheetVisible) _ = HidePhoneDetailSheetAsync();
    }

    // ──────────────────────────────────────────────
    //  Navigation
    // ──────────────────────────────────────────────

    private void OnEditClicked(object sender, EventArgs e)     => _ = OpenEditor();
    private void OnDeselectClicked(object sender, EventArgs e) => DeselectSlot();

    private async Task OpenEditor()
    {
        // Always edit the slot the cursor is currently on
        if (_cursorSlot < 0 || _cursorSlot >= _currentBox.Length) return;
        if (_currentBox[_cursorSlot].Species == 0) return;
        await Shell.Current.GoToAsync($"{nameof(PkmEditorPage)}?box={_boxIndex}&slot={_cursorSlot}");
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => _ = UpdateSearchAsync(e.NewTextValue ?? "");

    private async Task UpdateSearchAsync(string query)
    {
        if (_sav is null) return;
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            _searchMode = false;
            _searchResults = [];
            _cursorSlot = 0;
            BoxCanvas.InvalidateSurface();
            return;
        }
        _searchMode = true;
        var results = new List<SearchSlot>();
        var sav = _sav;
        await Task.Run(() =>
        {
            for (int b = 0; b < sav.BoxCount; b++)
            {
                var box = sav.GetBoxData(b);
                for (int s = 0; s < box.Length; s++)
                {
                    var pk = box[s];
                    if (pk.Species == 0 || pk.Species >= _strings.specieslist.Length) continue;
                    if (_strings.specieslist[pk.Species].Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                        results.Add(new SearchSlot(b, s, pk));
                }
            }
        });
        _searchResults = [.. results];
        if (_cursorSlot >= _searchResults.Length) _cursorSlot = 0;
        UpdateTopPanel();
        UpdateInfoBar();
        BoxCanvas.InvalidateSurface();
        await _sprites.PreloadBoxAsync(_searchResults.Select(r => r.Pk).ToArray());
        BoxCanvas.InvalidateSurface();
    }

    private void OnActionOverlayTapped(object sender, TappedEventArgs e) => CloseActionMenu();
    private void OnSaveHintTapped(object sender, TappedEventArgs e)     => OpenActionMenu();

    private async void OnMenuClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnSearchClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(DatabasePage));

    private async void OnGiftsClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(MysteryGiftDBPage));

    private async void OnSettingsClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SettingsPage));

    // ──────────────────────────────────────────────
    //  Items tab
    // ──────────────────────────────────────────────

    private void OnBoxesTabTapped(object? sender, TappedEventArgs e) => SwitchToBoxesTab();
    private void OnItemsTabTapped(object? sender, TappedEventArgs e) => SwitchToItemsTab();
    private void OnItemsMenuClicked(object? sender, EventArgs e)     => SwitchToItemsTab();
    private void OnItemsCloseTapped(object? sender, TappedEventArgs e) => SwitchToBoxesTab();

    private void SwitchToItemsTab()
    {
        if (_sav is null) return;
        _itemsTabActive      = true;
        ItemsPanel.IsVisible = true;
        UpdateTabHighlight();
        BuildPocketTabs();
    }

    private void SwitchToBoxesTab()
    {
        CommitItems();
        _itemsTabActive      = false;
        _itemEditMode        = false;
        ItemsPanel.IsVisible = false;
        UpdateTabHighlight();
    }

    private void CommitItems()
    {
        if (_bag is null || _sav is null) return;
        try { _bag.CopyTo(_sav); } catch { }
    }

    private void UpdateTabHighlight()
    {
        bool light = Current == PkTheme.Light;
        var activeBg  = Color.FromArgb(light ? "#DCE8FF" : "#182242");
        var inactiveBg = Color.FromArgb(light ? "#F4F6FB" : "#0C1120");
        var activeStroke   = Color.FromArgb("#3B8BFF");
        var inactiveStroke = Color.FromArgb(light ? "#E0E4EC" : "#1AFFFFFF");
        var activeText   = Color.FromArgb(light ? "#1A3A8F" : "#90B8FF");
        var inactiveText = Color.FromArgb(light ? "#555E78" : "#778BAA");

        BoxesTab.BackgroundColor = _itemsTabActive ? inactiveBg : activeBg;
        BoxesTab.Stroke          = _itemsTabActive ? inactiveStroke : activeStroke;
        if (BoxesTab.Content is Label bLabel)
            bLabel.TextColor = _itemsTabActive ? inactiveText : activeText;

        ItemsTab.BackgroundColor = _itemsTabActive ? activeBg : inactiveBg;
        ItemsTab.Stroke          = _itemsTabActive ? activeStroke : inactiveStroke;
        if (ItemsTab.Content is Label iLabel)
            iLabel.TextColor = _itemsTabActive ? activeText : inactiveText;
    }

    private void BuildPocketTabs()
    {
        PocketTabBar.Children.Clear();
        _pocketTabBorders.Clear();
        _itemRows = [];
        _itemCursor = -1;

        _bag = _sav?.Inventory;
        if (_bag is null || _bag.Pouches.Count == 0) return;

        for (int i = 0; i < _bag.Pouches.Count; i++)
        {
            int captured = i;
            var btn = new Border
            {
                StrokeShape     = new RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 1,
                Padding         = new Thickness(12, 6),
                Margin          = new Thickness(0),
            };
            btn.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => LoadPouch(captured))
            });
            btn.Content = new Label
            {
                Text       = PouchDisplayName(_bag.Pouches[captured].Type),
                FontFamily = "NunitoBold",
                FontSize   = 11,
                VerticalOptions = LayoutOptions.Center,
            };
            PocketTabBar.Children.Add(btn);
            _pocketTabBorders.Add(btn);
        }

        LoadPouch(0);
    }

    private static string PouchDisplayName(InventoryType t) => t switch
    {
        InventoryType.Balls       => "Balls",
        InventoryType.Medicine    => "Medicine",
        InventoryType.TMHMs       => "TM/HM",
        InventoryType.Berries     => "Berries",
        InventoryType.KeyItems    => "Key Items",
        InventoryType.BattleItems => "Battle",
        InventoryType.Items       => "Items",
        InventoryType.ZCrystals   => "Z-Crystals",
        InventoryType.Candy       => "Candy",
        InventoryType.Treasure    => "Treasure",
        InventoryType.Ingredients => "Ingredients",
        InventoryType.MegaStones  => "Mega Stones",
        InventoryType.PCItems     => "PC",
        _                          => t.ToString(),
    };

    private void LoadPouch(int pouchIndex)
    {
        if (_bag is null || pouchIndex < 0 || pouchIndex >= _bag.Pouches.Count) return;

        // Deselect previous cursor
        if (_itemCursor >= 0 && _itemCursor < _itemRows.Count)
            _itemRows[_itemCursor].IsSelected = false;

        _activePouchIndex = pouchIndex;
        _itemCursor       = -1;
        _itemEditMode     = false;

        var pouch  = _bag.Pouches[pouchIndex];
        bool isKey = pouch.Type == InventoryType.KeyItems;
        var names  = _strings.itemlist;

        // Always build from the full legal item list so unowned items are visible.
        // - Gen 9 (fixed-slot): every legal ID already has a pre-populated slot in Items[];
        //   all will be found in existingByIndex with Count=0 if unowned.
        // - Gen 6-8 (free-slot, PouchDataSize > legal count): owned items occupy slots,
        //   unowned items get a lazy-assigned free slot on first increment.
        // - Gen 1-5 (cramped, PouchDataSize < legal count): same lazy-assign path.
        var validIds = _bag.Info.GetItems(pouch.Type).ToArray();
        var existingByIndex = pouch.Items
            .Where(it => it.Index > 0)
            .ToDictionary(it => it.Index, it => it);

        _allItemRows = validIds
            .Where(id => id > 0)
            .Select(id =>
            {
                string nm  = id < names.Length ? names[id] : $"#{id}";
                int    max = _bag.GetMaxCount(pouch.Type, id);
                return existingByIndex.TryGetValue(id, out var slot)
                    ? new ItemRow(nm, slot, pouch.Type, max, isKey)
                    : new ItemRow(nm, id, pouch, pouch.Type, max, isKey);
            })
            .ToList();

        // Show free-slot count for Gen 1-8 formats where slots are a limited resource.
        // Gen 9 has no concept of free slots (all items pre-allocated), so hide the indicator.
        int totalSlots = pouch.Items.Length;
        int usedSlots  = pouch.Items.Count(it => it.Index > 0);
        int freeSlots  = totalSlots - usedSlots;
        bool showSlots = totalSlots != validIds.Length; // hidden for Gen 9 exact-match pouches
        ItemsSlotInfoLabel.Text      = showSlots ? $"{freeSlots} free slot{(freeSlots == 1 ? "" : "s")}" : "";
        ItemsSlotInfoLabel.IsVisible = showSlots;

        _itemSearchText = "";
        ItemSearchEntry.Text = "";
        ApplyItemFilter();
        UpdatePocketTabHighlight();
    }

    private void ApplyItemFilter()
    {
        string q = _itemSearchText.Trim();
        _itemRows = string.IsNullOrEmpty(q)
            ? _allItemRows
            : _allItemRows
                .Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        ItemsList.ItemsSource = null;
        ItemsList.ItemsSource = _itemRows;

        // Reset cursor if it's now out of range
        if (_itemCursor >= _itemRows.Count)
        {
            _itemCursor = -1;
            _itemEditMode = false;
        }
    }

    private void OnItemSearchChanged(object sender, TextChangedEventArgs e)
    {
        _itemSearchText = e.NewTextValue ?? "";
        ApplyItemFilter();
    }

    private void UpdatePocketTabHighlight()
    {
        bool light = Current == PkTheme.Light;
        var focusBg     = Color.FromArgb(light ? "#DCE8FF" : "#182242");
        var normalBg    = Color.FromArgb(light ? "#F4F6FB" : "#0C1120");
        var focusStroke  = Color.FromArgb("#3B8BFF");
        var normalStroke = Color.FromArgb(light ? "#E0E4EC" : "#1AFFFFFF");
        var focusText    = Color.FromArgb(light ? "#1A3A8F" : "#90B8FF");
        var normalText   = Color.FromArgb(light ? "#333D55" : "#778BAA");

        for (int i = 0; i < _pocketTabBorders.Count; i++)
        {
            bool active = i == _activePouchIndex;
            _pocketTabBorders[i].BackgroundColor = active ? focusBg : normalBg;
            _pocketTabBorders[i].Stroke          = active ? focusStroke : normalStroke;
            if (_pocketTabBorders[i].Content is Label lbl)
                lbl.TextColor = active ? focusText : normalText;
        }
    }

    private void HandleItemsKey(Android.Views.Keycode keyCode)
    {
        if (_itemEditMode)
        {
            var row = _itemCursor >= 0 && _itemCursor < _itemRows.Count
                ? _itemRows[_itemCursor] : null;
            switch (keyCode)
            {
                case Android.Views.Keycode.DpadLeft:  if (row != null) row.Count--;    break;
                case Android.Views.Keycode.DpadRight: if (row != null) row.Count++;    break;
                case Android.Views.Keycode.ButtonL1:  if (row != null) row.Count -= 10; break;
                case Android.Views.Keycode.ButtonR1:  if (row != null) row.Count += 10; break;
                case Android.Views.Keycode.ButtonA:
                case Android.Views.Keycode.ButtonB:
                    _itemEditMode = false;
                    break;
            }
            return;
        }

        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:   MoveItemCursor(-1); break;
            case Android.Views.Keycode.DpadDown: MoveItemCursor(+1); break;
            case Android.Views.Keycode.ButtonL1: CyclePocket(-1);     break;
            case Android.Views.Keycode.ButtonR1: CyclePocket(+1);     break;
            case Android.Views.Keycode.ButtonA:
                if (_itemCursor >= 0 && _itemCursor < _itemRows.Count
                    && !_itemRows[_itemCursor].IsKeyItem)
                    _itemEditMode = true;
                break;
            case Android.Views.Keycode.ButtonB:
                SwitchToBoxesTab();
                break;
        }
    }

    private void MoveItemCursor(int delta)
    {
        if (_itemRows.Count == 0) return;
        if (_itemCursor >= 0 && _itemCursor < _itemRows.Count)
            _itemRows[_itemCursor].IsSelected = false;
        _itemCursor = Math.Clamp(
            _itemCursor < 0 ? (delta > 0 ? 0 : _itemRows.Count - 1) : _itemCursor + delta,
            0, _itemRows.Count - 1);
        _itemRows[_itemCursor].IsSelected = true;
        ItemsList.ScrollTo(_itemRows[_itemCursor], group: null, position: ScrollToPosition.MakeVisible, animate: false);
        Haptic();
    }

    private void CyclePocket(int delta)
    {
        if (_bag is null) return;
        int next = Math.Clamp(_activePouchIndex + delta, 0, _bag.Pouches.Count - 1);
        if (next != _activePouchIndex)
            LoadPouch(next);
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (_sav is null) return;
        if (string.IsNullOrEmpty(App.ActiveSaveFileUri))
        {
            await DisplayAlertAsync("Save failed", "No original file location found. Use Export instead.", "OK");
            return;
        }
        try
        {
            var data = _sav.Write().ToArray();
            await new FileService().WriteBackAsync(data, App.ActiveSaveFileUri);
            await DisplayAlertAsync("Saved", $"Written back to {App.ActiveSaveFileName}.", "OK");
        }
        catch (Exception ex)
        {
            bool isPermission = ex.Message.Contains("Permission") || ex.Message.Contains("permission");
            string msg = isPermission
                ? "Write permission denied. Go to Settings → Find Azahar/MelonDS/RetroArch Saves and re-grant folder access, then try again."
                : ex.Message;
            await DisplayAlertAsync("Save failed", msg, "OK");
        }
    }

    private async void OnExportClicked(object sender, EventArgs e)
    {
        if (_sav is null) return;
        try
        {
            var data = _sav.Write().ToArray();
            await new FileService().ExportFileAsync(data, App.ActiveSaveFileName);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export failed", ex.Message, "OK");
        }
    }
}
