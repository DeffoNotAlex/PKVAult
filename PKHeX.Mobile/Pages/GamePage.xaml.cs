using PKHeX.Core;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PKHeX.Mobile.Pages;

public partial class GamePage : ContentPage
{
    private const int Columns = 6;
    private const int Rows = 5;

    private readonly FileSystemSpriteRenderer _sprites = new();
    private readonly GameStrings _strings = GameInfo.GetStrings("en");

    private SaveFile? _sav;
    private PKM[] _currentBox = [];
    private int _boxIndex;
    private int _cursorSlot;
    private int _selectedSlot = -1;
    private PKM? _selectedPk;
    private bool _loadingBox;

    public GamePage()
    {
        InitializeComponent();
    }

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif

        var sav = App.ActiveSave;
        if (sav is null) return;

        bool freshSave = _sav != sav;
        _sav = sav;

        if (freshSave)
        {
            _boxIndex = 0;
            _cursorSlot = 0;
            DeselectSlot();

            TrainerNameLabel.Text = sav.OT;
            SaveGameLabel.Text = $"{sav.Version} — Gen {sav.Generation}";
            BoxCountLabel.Text = $"{sav.BoxCount} boxes · {sav.SlotCount} slots";
        }

        LoadBox(_boxIndex);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
    }

    // ──────────────────────────────────────────────
    //  Box loading
    // ──────────────────────────────────────────────

    private async void LoadBox(int box)
    {
        if (_sav is null || _loadingBox) return;
        _loadingBox = true;
        try
        {
            _currentBox = _sav.GetBoxData(box);
            BoxNameLabel.Text = _sav is IBoxDetailName named
                ? named.GetBoxName(box)
                : $"Box {box + 1}";

            // Keep selected slot in sync if returning from editor
            if (_selectedSlot >= 0 && _selectedSlot < _currentBox.Length)
            {
                var pk = _currentBox[_selectedSlot];
                if (pk.Species != 0) SelectSlot(_selectedSlot);
                else DeselectSlot();
            }

            await _sprites.PreloadBoxAsync(_currentBox);
            BoxCanvas.InvalidateSurface();
        }
        finally { _loadingBox = false; }
    }

    // ──────────────────────────────────────────────
    //  Box navigation
    // ──────────────────────────────────────────────

    private void OnPrevBox(object sender, EventArgs e)
    {
        if (_sav is null || _boxIndex <= 0) return;
        _boxIndex--;
        DeselectSlot();
        LoadBox(_boxIndex);
    }

    private void OnNextBox(object sender, EventArgs e)
    {
        if (_sav is null || _boxIndex >= _sav.BoxCount - 1) return;
        _boxIndex++;
        DeselectSlot();
        LoadBox(_boxIndex);
    }

    // ──────────────────────────────────────────────
    //  Rendering — top screen
    // ──────────────────────────────────────────────

    private void OnBoxPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(10, 10, 20));

        if (_currentBox.Length == 0) return;

        float slotW = (float)e.Info.Width / Columns;
        float slotH = (float)e.Info.Height / Rows;
        const float pad = 4f;
        const float radius = 8f;

        for (int i = 0; i < _currentBox.Length; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            float x = col * slotW;
            float y = row * slotH;
            var pk = _currentBox[i];

            bool isCursor   = i == _cursorSlot;
            bool isSelected = i == _selectedSlot;

            // Slot background
            var bgColor = isSelected
                ? new SKColor(80, 60, 20, 220)
                : isCursor
                ? new SKColor(30, 50, 100, 220)
                : new SKColor(20, 20, 40, 180);

            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawRoundRect(x + pad, y + pad, slotW - pad * 2, slotH - pad * 2, radius, radius, bgPaint);

            // Sprite
            if (pk.Species != 0)
            {
                var sprite = _sprites.GetSprite(pk);
                canvas.DrawBitmap(sprite, SKRect.Create(x + pad, y + pad, slotW - pad * 2, slotH - pad * 2));
            }

            // Cursor outline (blue)
            if (isCursor)
            {
                using var outlinePaint = new SKPaint
                {
                    Color = new SKColor(80, 160, 255, 230),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 3f,
                    IsAntialias = true,
                };
                canvas.DrawRoundRect(x + pad, y + pad, slotW - pad * 2, slotH - pad * 2, radius, radius, outlinePaint);
            }

            // Selection outline (gold)
            if (isSelected)
            {
                using var selPaint = new SKPaint
                {
                    Color = new SKColor(255, 210, 50, 255),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 4f,
                    IsAntialias = true,
                };
                canvas.DrawRoundRect(x + pad, y + pad, slotW - pad * 2, slotH - pad * 2, radius, radius, selPaint);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Rendering — bottom screen preview
    // ──────────────────────────────────────────────

    private void OnPreviewPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_selectedPk is null) return;
        var sprite = _sprites.GetSprite(_selectedPk);
        canvas.DrawBitmap(sprite, SKRect.Create(0, 0, e.Info.Width, e.Info.Height));
    }

    // ──────────────────────────────────────────────
    //  Touch input
    // ──────────────────────────────────────────────

    private void OnBoxTapped(object sender, TappedEventArgs e)
    {
        if (_sav is null || sender is not View view) return;

        var point = e.GetPosition(view);
        if (point is null) return;

        float slotW = (float)view.Width / Columns;
        float slotH = (float)view.Height / Rows;
        int col = (int)(point.Value.X / slotW);
        int row = (int)(point.Value.Y / slotH);
        int index = row * Columns + col;

        if ((uint)index >= (uint)_currentBox.Length) return;

        _cursorSlot = index;

        if (index == _selectedSlot)
        {
            // Double-tap selected slot → open editor
            _ = OpenEditor();
        }
        else if (_currentBox[index].Species != 0)
        {
            SelectSlot(index);
        }
        else
        {
            DeselectSlot();
        }
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
        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:
                MoveCursor(-Columns); break;
            case Android.Views.Keycode.DpadDown:
                MoveCursor(+Columns); break;
            case Android.Views.Keycode.DpadLeft:
                MoveCursor(-1); break;
            case Android.Views.Keycode.DpadRight:
                MoveCursor(+1); break;

            case Android.Views.Keycode.ButtonA:
                if (_selectedSlot < 0)
                    SelectSlot(_cursorSlot);
                else
                    _ = OpenEditor();
                break;

            case Android.Views.Keycode.ButtonB:
                if (_selectedSlot >= 0) DeselectSlot();
                break;

            case Android.Views.Keycode.ButtonL1:
            case Android.Views.Keycode.Button5:
                OnPrevBox(this, EventArgs.Empty); break;

            case Android.Views.Keycode.ButtonR1:
            case Android.Views.Keycode.Button6:
                OnNextBox(this, EventArgs.Empty); break;
        }
    }
#endif

    private void MoveCursor(int delta)
    {
        // Prevent wrapping left/right across row boundaries
        if (delta == -1 && _cursorSlot % Columns == 0) return;
        if (delta == +1 && _cursorSlot % Columns == Columns - 1) return;

        int next = _cursorSlot + delta;
        if ((uint)next >= (uint)_currentBox.Length) return;

        _cursorSlot = next;
        BoxCanvas.InvalidateSurface();
    }

    // ──────────────────────────────────────────────
    //  Selection state machine
    // ──────────────────────────────────────────────

    private void SelectSlot(int slot)
    {
        var pk = _currentBox[slot];
        if (pk.Species == 0) return;

        _selectedSlot = slot;
        _selectedPk = pk;

        var speciesName = pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species] : pk.Species.ToString();

        PkmNameLabel.Text = $"#{pk.Species:000} {speciesName}";
        PkmLevelLabel.Text = $"Lv. {pk.CurrentLevel}" + (pk.IsShiny ? "  ✦" : "");
        PkmNatureLabel.Text = (int)pk.Nature < _strings.natures.Length
            ? _strings.natures[(int)pk.Nature] : pk.Nature.ToString();

        var moveIds = new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 };
        var moveNames = moveIds
            .Where(m => m != 0)
            .Select(m => m < _strings.movelist.Length ? _strings.movelist[m] : m.ToString());
        PkmMovesLabel.Text = string.Join(" · ", moveNames);

        IdlePanel.IsVisible = false;
        SelectedPanel.IsVisible = true;

        PreviewCanvas.InvalidateSurface();
        BoxCanvas.InvalidateSurface();
    }

    private void DeselectSlot()
    {
        _selectedSlot = -1;
        _selectedPk = null;
        IdlePanel.IsVisible = true;
        SelectedPanel.IsVisible = false;
        BoxCanvas.InvalidateSurface();
    }

    // ──────────────────────────────────────────────
    //  Navigation
    // ──────────────────────────────────────────────

    private void OnEditClicked(object sender, EventArgs e) => _ = OpenEditor();
    private void OnDeselectClicked(object sender, EventArgs e) => DeselectSlot();

    private async Task OpenEditor()
    {
        if (_selectedSlot < 0) return;
        await Shell.Current.GoToAsync($"{nameof(PkmEditorPage)}?box={_boxIndex}&slot={_selectedSlot}");
    }

    private async void OnSearchClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(DatabasePage));

    private async void OnGiftsClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(MysteryGiftDBPage));

    private async void OnSettingsClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SettingsPage));

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
