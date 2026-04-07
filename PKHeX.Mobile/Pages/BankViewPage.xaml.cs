using PKHeX.Core;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

namespace PKHeX.Mobile.Pages;

public partial class BankViewPage : ContentPage
{
    private const int Columns = 6;
    private const int Rows    = 5;

    private readonly ISecondaryDisplay        _secondary;
    private readonly BankService              _bank    = new();
    private readonly FileSystemSpriteRenderer _sprites = new();
    private readonly GameStrings              _strings = GameInfo.GetStrings("en");

    private PKM?[] _currentSlots = new PKM?[BankService.SlotsPerBox];
    private int    _boxIndex;
    private int    _cursorSlot;
    private int    _detailView;   // 0 = sprite+info, 1 = stats+moves
    private PKM?   _previewPk;

    public BankViewPage(ISecondaryDisplay secondary)
    {
        _secondary = secondary;
        InitializeComponent();
    }

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived        -= OnGamepadKey;
        GamepadRouter.KeyReceived        += OnGamepadKey;
        GamepadRouter.BoxScrollRequested -= OnBoxScroll;
        GamepadRouter.BoxScrollRequested += OnBoxScroll;
#endif
        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        await LoadBoxAsync(_boxIndex, resetCursor: false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived        -= OnGamepadKey;
        GamepadRouter.BoxScrollRequested -= OnBoxScroll;
#endif
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SpriteCanvas.InvalidateSurface();
            StatsCanvas.InvalidateSurface();
        });
    }

    // ──────────────────────────────────────────────
    //  Box loading
    // ──────────────────────────────────────────────

    private async Task LoadBoxAsync(int box, bool resetCursor = true)
    {
        _boxIndex = box;
        if (resetCursor) _cursorSlot = 0;

        _currentSlots = _bank.GetBoxData(box);
        string boxName = box < _bank.Boxes.Count ? _bank.Boxes[box].Name : $"Bank {box + 1}";
        int    boxCount = _bank.Boxes.Count;

        BoxNameLabel.Text  = boxName;
        BoxIndexLabel.Text = $"{box + 1} / {boxCount}";

        await _sprites.PreloadBoxAsync(_currentSlots.Select(p => p ?? (PKM)new PK9()).ToArray());

        _secondary.ShowBankGrid(_currentSlots, _cursorSlot, boxName, box, boxCount);
        UpdateDetail();
    }

    // ──────────────────────────────────────────────
    //  Detail panel
    // ──────────────────────────────────────────────

    private void UpdateDetail()
    {
        _previewPk = _cursorSlot < _currentSlots.Length ? _currentSlots[_cursorSlot] : null;
        if (_detailView == 0)
        {
            UpdateInfoLabels();
            SpriteCanvas.InvalidateSurface();
        }
        else
        {
            StatsCanvas.InvalidateSurface();
        }
    }

    private void UpdateInfoLabels()
    {
        var pk = _previewPk;
        if (pk?.Species > 0)
        {
            SpeciesNum.Text  = $"#{pk.Species:000}";
            SpeciesName.Text = pk.Species < _strings.specieslist.Length
                ? _strings.specieslist[pk.Species] : "?";
            LevelLabel.Text  = $"Lv. {pk.CurrentLevel}";
            ShinyLabel.IsVisible = pk.IsShiny;
            OTLabel.Text = $"OT: {pk.OriginalTrainerName}  ·  ID: {pk.TID16}";

            var slot = _bank.GetSlot(_boxIndex, _cursorSlot);
            if (slot?.DepositedAt is { Length: > 0 } ts &&
                DateTime.TryParse(ts, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                DepositLabel.Text = $"Deposited {dt.ToLocalTime():MMM d, yyyy}";
            else
                DepositLabel.Text = "";
        }
        else
        {
            SpeciesNum.Text  = "";
            SpeciesName.Text = "Empty";
            LevelLabel.Text  = "";
            ShinyLabel.IsVisible = false;
            OTLabel.Text     = "";
            DepositLabel.Text = "";
        }
    }

    // ──────────────────────────────────────────────
    //  Canvas: sprite
    // ──────────────────────────────────────────────

    private void OnSpritePaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(ThemeService.CanvasBg);

        var pk = _previewPk;
        if (pk is null || pk.Species == 0) return;

        var sprite  = _sprites.GetSprite(pk);
        float maxSz = Math.Min(e.Info.Width, e.Info.Height) * 0.82f;
        float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
        float drawW, drawH;
        if (aspect >= 1f) { drawW = maxSz; drawH = maxSz / aspect; }
        else              { drawH = maxSz; drawW = maxSz * aspect; }
        float sx = (e.Info.Width  - drawW) / 2f;
        float sy = (e.Info.Height - drawH) / 2f;

        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH), paint);
    }

    // ──────────────────────────────────────────────
    //  Canvas: stats + moves
    // ──────────────────────────────────────────────

    private void OnStatsPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(ThemeService.CanvasBg);

        var pk = _previewPk;
        float w = e.Info.Width, h = e.Info.Height;
        bool light = Current == PkTheme.Light;

        float halfW = w / 2f;
        DrawStatBars(canvas, pk, new SKRect(0, 0, halfW, h), light);
        DrawMoves(canvas, pk, new SKRect(halfW, 0, w, h), light);
    }

    private static void DrawStatBars(SKCanvas canvas, PKM? pk, SKRect area, bool light)
    {
        string[] names  = ["HP", "Atk", "Def", "SpA", "SpD", "Spe"];
        int[]    values = pk is not null
            ? [pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPA, pk.Stat_SPD, pk.Stat_SPE]
            : [0, 0, 0, 0, 0, 0];
        SKColor[] colors =
        [
            SKColor.Parse("#FF5959"), SKColor.Parse("#FF9F43"),
            SKColor.Parse("#FFC542"), SKColor.Parse("#A78BFA"),
            SKColor.Parse("#34D990"), SKColor.Parse("#4FC3F7"),
        ];

        int maxVal = Math.Max(1, values.Max());
        float rowH    = area.Height / 6f;
        float padL    = area.Left + 14f;
        float barMaxW = area.Width - 80f;
        var textColor = light ? new SKColor(13, 17, 23) : new SKColor(220, 228, 255);

        using var namePaint = new SKPaint { Color = textColor, TextSize = 21f, IsAntialias = true };
        using var valPaint  = new SKPaint { Color = textColor, TextSize = 19f, IsAntialias = true };

        for (int i = 0; i < 6; i++)
        {
            float midY  = area.Top + i * rowH + rowH * 0.5f;

            canvas.DrawText(names[i], padL, midY + 8f, namePaint);

            float barX   = padL + 52f;
            float barY   = midY - 6f;
            float fill   = barMaxW * (values[i] / (float)maxVal);

            using var bgPaint = new SKPaint
            {
                Color = light ? new SKColor(205, 210, 230) : new SKColor(28, 38, 68),
                IsAntialias = true,
            };
            canvas.DrawRoundRect(barX, barY, barMaxW, 12f, 6, 6, bgPaint);

            if (fill > 0)
            {
                using var fillPaint = new SKPaint { Color = colors[i], IsAntialias = true };
                canvas.DrawRoundRect(barX, barY, fill, 12f, 6, 6, fillPaint);
            }

            canvas.DrawText(values[i].ToString(), barX + barMaxW + 7f, midY + 7f, valPaint);
        }
    }

    private void DrawMoves(SKCanvas canvas, PKM? pk, SKRect area, bool light)
    {
        string MoveName(int id) =>
            id > 0 && id < _strings.movelist.Length ? _strings.movelist[id] : "—";

        string[] moveNames = pk is not null
            ? [MoveName(pk.Move1), MoveName(pk.Move2), MoveName(pk.Move3), MoveName(pk.Move4)]
            : ["—", "—", "—", "—"];

        var dimColor  = light ? new SKColor(100, 115, 145) : new SKColor(100, 125, 175);
        var textColor = light ? new SKColor(13,  17,  23)  : new SKColor(220, 228, 255);

        float padL = area.Left + 16f;

        using var headerPaint = new SKPaint { Color = dimColor, TextSize = 16f, IsAntialias = true };
        canvas.DrawText("MOVES", padL, area.Top + 22f, headerPaint);

        float rowH = (area.Height - 28f) / 4f;
        using var movePaint = new SKPaint { Color = textColor, TextSize = 21f, IsAntialias = true };
        for (int i = 0; i < 4; i++)
        {
            float y = area.Top + 28f + (i + 0.5f) * rowH + 8f;
            canvas.DrawText(moveNames[i], padL, y, movePaint);
        }
    }

    // ──────────────────────────────────────────────
    //  Cursor movement
    // ──────────────────────────────────────────────

    private void MoveCursor(int delta)
    {
        if (delta == -1 && _cursorSlot % Columns == 0)           return;
        if (delta == +1 && _cursorSlot % Columns == Columns - 1) return;
        int next = _cursorSlot + delta;
        if ((uint)next >= BankService.SlotsPerBox) return;
        _cursorSlot = next;
        _secondary.UpdateBankCursor(_cursorSlot);
        UpdateDetail();
    }

    private void CycleDetailView()
    {
        _detailView = _detailView == 0 ? 1 : 0;
        InfoPanel.IsVisible   = _detailView == 0;
        StatsCanvas.IsVisible = _detailView == 1;
        UpdateDetail();
    }

    // ──────────────────────────────────────────────
    //  Touch handlers
    // ──────────────────────────────────────────────

    private void OnPrevBoxTapped(object sender, EventArgs e)
    {
        if (_boxIndex > 0)
            _ = LoadBoxAsync(_boxIndex - 1);
    }

    private void OnNextBoxTapped(object sender, EventArgs e)
    {
        if (_boxIndex < _bank.Boxes.Count - 1)
            _ = LoadBoxAsync(_boxIndex + 1);
    }

    // ──────────────────────────────────────────────
    //  Gamepad
    // ──────────────────────────────────────────────

#if ANDROID
    private void OnGamepadKey(Android.Views.Keycode keyCode, Android.Views.KeyEventActions action)
    {
        if (action != Android.Views.KeyEventActions.Down) return;
        MainThread.BeginInvokeOnMainThread(() => HandleGamepadKey(keyCode));
    }

    private void OnBoxScroll(int dir)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (dir < 0 && _boxIndex > 0)
                _ = LoadBoxAsync(_boxIndex - 1);
            else if (dir > 0 && _boxIndex < _bank.Boxes.Count - 1)
                _ = LoadBoxAsync(_boxIndex + 1);
        });

    private void HandleGamepadKey(Android.Views.Keycode keyCode)
    {
        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:    MoveCursor(-Columns); break;
            case Android.Views.Keycode.DpadDown:  MoveCursor(+Columns); break;
            case Android.Views.Keycode.DpadLeft:  MoveCursor(-1);       break;
            case Android.Views.Keycode.DpadRight: MoveCursor(+1);       break;
            case Android.Views.Keycode.ButtonL1:
                if (_boxIndex > 0) _ = LoadBoxAsync(_boxIndex - 1);
                break;
            case Android.Views.Keycode.ButtonR1:
                if (_boxIndex < _bank.Boxes.Count - 1) _ = LoadBoxAsync(_boxIndex + 1);
                break;
            case Android.Views.Keycode.ButtonX:
                CycleDetailView(); break;
            case Android.Views.Keycode.ButtonB:
                _ = Shell.Current.GoToAsync(".."); break;
        }
    }
#endif
}
