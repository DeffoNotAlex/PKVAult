using Microsoft.Maui.Controls.Shapes;
using PKHeX.Core;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PKHeX.Mobile.Pages;

public partial class DexPage : ContentPage
{
    private const int Columns      = 8;
    private const int TotalSpecies = DexService.MaxSpecies;
    private static int TotalRows   => (TotalSpecies + Columns - 1) / Columns; // 129

    private readonly ISecondaryDisplay        _secondary;
    private readonly DexService               _dex;
    private readonly FileSystemSpriteRenderer _renderer = new();
    private readonly GameStrings              _strings  = GameInfo.GetStrings("en");

    // Pre-created PKM instances per species for sprite lookups (avoids per-frame alloc)
    private static readonly PK9[] _speciesPks =
        Enumerable.Range(1, TotalSpecies)
            .Select(i => new PK9 { Species = (ushort)i })
            .ToArray();

    // Type data indexed [1..1025]
    private static readonly (int t1, int t2)[] _typeData = BuildTypeData();

    private static readonly string[] _typeNames =
    [
        "Normal","Fighting","Flying","Poison","Ground","Rock",
        "Bug","Ghost","Steel","Fire","Water","Grass",
        "Electric","Psychic","Ice","Dragon","Dark","Fairy","Stellar","???",
    ];

    private static readonly Color[] _typePillColors =
    [
        Color.FromArgb("#A8A878"), // Normal
        Color.FromArgb("#C03028"), // Fighting
        Color.FromArgb("#A890F0"), // Flying
        Color.FromArgb("#A040A0"), // Poison
        Color.FromArgb("#E0C068"), // Ground
        Color.FromArgb("#B8A038"), // Rock
        Color.FromArgb("#A8B820"), // Bug
        Color.FromArgb("#705898"), // Ghost
        Color.FromArgb("#B8B8D0"), // Steel
        Color.FromArgb("#F08030"), // Fire
        Color.FromArgb("#6890F0"), // Water
        Color.FromArgb("#78C850"), // Grass
        Color.FromArgb("#F8D030"), // Electric
        Color.FromArgb("#F85888"), // Psychic
        Color.FromArgb("#98D8D8"), // Ice
        Color.FromArgb("#7038F8"), // Dragon
        Color.FromArgb("#705848"), // Dark
        Color.FromArgb("#EE99AC"), // Fairy
        Color.FromArgb("#40B5A5"), // Stellar
        Color.FromArgb("#68A090"), // ???
    ];

    // Virtual scroll state
    private float _scrollOffsetPx;
    private int   _cursorSlot; // 0-based; species = _cursorSlot + 1

    // Cached layout (set on first paint)
    private float _slotSize;
    private float _rowHeight;
    private float _viewWidthPx;
    private float _viewHeightPx;
    private float _canvasTotalHeight;

    // Silhouette color filter matrix (keeps alpha, zeroes RGB)
    private static readonly float[] _silhouetteMatrix =
        [0,0,0,0,0, 0,0,0,0,0, 0,0,0,0,0, 0,0,0,1,0];

    // Pan state
    private float _panStartOffset;
    private float _panStartY;

    public DexPage(ISecondaryDisplay secondary, DexService dex)
    {
        _secondary = secondary;
        _dex       = dex;
        InitializeComponent();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;

        UpdateCaughtLabel();
        UpdateFooter();
        PushDexStats();
        _ = PreloadSpritesAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
        ThemeService.ThemeChanged -= OnThemeChanged;
        _secondary.HideDexStats();
    }

    private void OnThemeChanged()
        => MainThread.BeginInvokeOnMainThread(() => DexCanvas.InvalidateSurface());

    // ── Sprite preload (background, progressive) ───────────────────────────────

    private async Task PreloadSpritesAsync()
    {
        for (int start = 0; start < TotalSpecies; start += 30)
        {
            int count = Math.Min(30, TotalSpecies - start);
            var batch = _speciesPks[start..(start + count)].Cast<PKM>().ToArray();
            await _renderer.PreloadBoxAsync(batch);
            DexCanvas.InvalidateSurface();
        }
    }

    // ── Canvas painting ────────────────────────────────────────────────────────

    private void OnDexPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        _viewWidthPx  = e.Info.Width;
        _viewHeightPx = e.Info.Height;

        // Proportional layout — scales cleanly to any density/screen size
        float pad  = _viewWidthPx * 0.025f;
        float gap  = _viewWidthPx * 0.012f;
        _slotSize  = (_viewWidthPx - 2 * pad - (Columns - 1) * gap) / Columns;
        _rowHeight = _slotSize + gap;
        _canvasTotalHeight = TotalRows * _rowHeight + 2 * pad;

        var canvas = e.Surface.Canvas;
        canvas.Clear(ThemeService.CanvasBg);

        // Only render visible rows (virtual scroll)
        int firstRow = Math.Max(0, (int)((_scrollOffsetPx - pad) / _rowHeight));
        int lastRow  = Math.Min(TotalRows - 1, (int)((_scrollOffsetPx + _viewHeightPx) / _rowHeight) + 1);

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                int slot = row * Columns + col;
                if (slot >= TotalSpecies) break;

                float x = pad + col * (_slotSize + gap);
                float y = pad + row * _rowHeight - _scrollOffsetPx;
                DrawSlot(canvas, SKRect.Create(x, y, _slotSize, _slotSize), (ushort)(slot + 1), slot == _cursorSlot);
            }
        }
    }

    private void DrawSlot(SKCanvas canvas, SKRect rect, ushort species, bool isCursor)
    {
        // Slot background
        var bgColor = isCursor
            ? new SKColor(40, 90, 180, 90)
            : new SKColor(255, 255, 255, 15);
        using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(rect, 8, 8, bgPaint);

        // Sprite / silhouette
        var sprite = _renderer.GetSprite(_speciesPks[species - 1]);
        if (sprite.Width > 0 && sprite.Height > 0)
        {
            float inner  = _slotSize * 0.78f;
            float aspect = (float)sprite.Width / sprite.Height;
            float drawW  = aspect >= 1f ? inner : inner * aspect;
            float drawH  = aspect >= 1f ? inner / aspect : inner;
            float sx = rect.MidX - drawW / 2f;
            float sy = rect.MidY - drawH / 2f;
            var   destRect = SKRect.Create(sx, sy, drawW, drawH);

            if (_dex.IsUnlocked(species))
            {
                canvas.DrawBitmap(sprite, destRect);
            }
            else
            {
                using var silPaint = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateColorMatrix(_silhouetteMatrix),
                };
                canvas.DrawBitmap(sprite, destRect, silPaint);
            }
        }

        // Cursor outline
        if (isCursor)
        {
            using var cp = new SKPaint
            {
                Color       = new SKColor(80, 160, 255, 200),
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = 2.5f,
                IsAntialias = true,
            };
            canvas.DrawRoundRect(rect, 8, 8, cp);
        }
    }

    // ── Pan gesture (touch scroll) ─────────────────────────────────────────────

    private void OnPan(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartOffset = _scrollOffsetPx;
                _panStartY      = 0;
                break;
            case GestureStatus.Running:
                float density = (float)DeviceDisplay.MainDisplayInfo.Density;
                SetScrollOffset(_panStartOffset - (float)e.TotalY * density);
                DexCanvas.InvalidateSurface();
                break;
        }
    }

    private void SetScrollOffset(float value)
    {
        float maxOffset = Math.Max(0, _canvasTotalHeight - _viewHeightPx);
        _scrollOffsetPx = Math.Clamp(value, 0, maxOffset);
    }

    // ── Gamepad ────────────────────────────────────────────────────────────────

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
            case Android.Views.Keycode.DpadUp:    MoveCursor(-Columns);     break;
            case Android.Views.Keycode.DpadDown:  MoveCursor(+Columns);     break;
            case Android.Views.Keycode.DpadLeft:  MoveCursor(-1);           break;
            case Android.Views.Keycode.DpadRight: MoveCursor(+1);           break;
            case Android.Views.Keycode.ButtonL1:  MoveCursor(-Columns * 5); break;
            case Android.Views.Keycode.ButtonR1:  MoveCursor(+Columns * 5); break;
            case Android.Views.Keycode.ButtonB:
                _ = Shell.Current.GoToAsync(".."); break;
            case Android.Views.Keycode.ButtonSelect:
                ScanActiveSave(); break;
        }
    }
#endif

    private void MoveCursor(int delta)
    {
        if (delta == -1 && _cursorSlot % Columns == 0)           return;
        if (delta == +1 && _cursorSlot % Columns == Columns - 1) return;
        int next = Math.Clamp(_cursorSlot + delta, 0, TotalSpecies - 1);
        if (next == _cursorSlot) return;
        _cursorSlot = next;
        ScrollToCursor();
        UpdateFooter();
        DexCanvas.InvalidateSurface();
    }

    private void ScrollToCursor()
    {
        if (_slotSize == 0 || _viewHeightPx == 0) return;
        float pad     = _viewWidthPx * 0.025f;
        int   row     = _cursorSlot / Columns;
        float slotTop = pad + row * _rowHeight;
        float slotBot = slotTop + _slotSize;

        if (slotTop < _scrollOffsetPx)
            SetScrollOffset(slotTop - pad);
        else if (slotBot > _scrollOffsetPx + _viewHeightPx)
            SetScrollOffset(slotBot - _viewHeightPx + pad);
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    private void ScanActiveSave()
    {
        if (App.ActiveSave is null)
        {
            _ = DisplayAlert("No Save", "Load a save file first.", "OK");
            return;
        }
        int newly = _dex.ScanSave(App.ActiveSave);
        UpdateCaughtLabel();
        UpdateFooter();
        PushDexStats();
        DexCanvas.InvalidateSurface();

        string msg = newly > 0
            ? $"{newly} new Pokémon unlocked!"
            : "No new Pokémon found in this save.";
        _ = DisplayAlert("Scan Complete", msg, "OK");
    }

    // ── UI helpers ─────────────────────────────────────────────────────────────

    private void UpdateCaughtLabel()
        => CaughtLabel.Text = $"{_dex.UnlockedCount} / {TotalSpecies} collected";

    private void UpdateFooter()
    {
        ushort species  = (ushort)(_cursorSlot + 1);
        bool   unlocked = _dex.IsUnlocked(species);

        SpeciesNameLabel.Text = unlocked && species < _strings.specieslist.Length
            ? _strings.specieslist[species]
            : "???";
        SpeciesNumLabel.Text = $"#{species:000}";

        TypePills.Children.Clear();
        if (unlocked)
        {
            var (t1, t2) = _typeData[species];
            TypePills.Children.Add(MakeTypePill(t1));
            if (t2 != t1) TypePills.Children.Add(MakeTypePill(t2));
        }
    }

    private View MakeTypePill(int typeIndex)
    {
        string name  = typeIndex < _typeNames.Length      ? _typeNames[typeIndex]      : "???";
        var    color = typeIndex < _typePillColors.Length ? _typePillColors[typeIndex] : Colors.Gray;
        return new Border
        {
            StrokeShape     = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = color.WithAlpha(0.85f),
            Stroke          = Colors.Transparent,
            Padding         = new Thickness(8, 3),
            Content = new Label
            {
                Text            = name,
                FontFamily      = "NunitoBold",
                FontSize        = 10,
                TextColor       = Colors.White,
                VerticalOptions = LayoutOptions.Center,
            },
        };
    }

    private void PushDexStats()
    {
        var byGen = _dex.GetStatsByGen();
        _secondary.ShowDexStats(
            _dex.UnlockedCount,
            TotalSpecies,
            byGen.Select(g => g.caught).ToArray(),
            byGen.Select(g => g.total).ToArray());
    }

    // ── Type data ──────────────────────────────────────────────────────────────

    private static (int t1, int t2)[] BuildTypeData()
    {
        var result = new (int, int)[TotalSpecies + 1]; // [0] unused
        for (ushort i = 1; i <= TotalSpecies; i++)
        {
            try
            {
                var info = PersonalTable.SV.GetFormEntry(i, 0);
                result[i] = (info.Type1, info.Type2);
            }
            catch
            {
                result[i] = (0, 0);
            }
        }
        return result;
    }
}
