using PKHeX.Core;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PKHeX.Mobile.Pages;

public partial class BankPage : ContentPage
{
    private const int Columns = 6;
    private const int Rows    = 5;

    private readonly BankService              _bank    = new();
    private readonly FileSystemSpriteRenderer _sprites = new();
    private readonly GameStrings              _strings = GameInfo.GetStrings("en");

    private PKM?[] _currentSlots = new PKM?[BankService.SlotsPerBox];
    private int    _boxIndex;
    private int    _cursorSlot;
    private int    _selectedSlot = -1;

    // Move state (withdraw: grabbed from bank; deposit: arrived from game)
    private bool _moveMode;   // true = grabbed from bank (withdraw grab)
    private PKM? _movePk;
    private int  _moveSourceSlot;

    public BankPage()
    {
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
        // Slide in the inner grid (not `this`) so hit-testing is always correct
        RootGrid.TranslationX = App.BankSlideDir < 0 ? -500 : 500;
        await RootGrid.TranslateTo(0, 0, 260, Easing.CubicInOut);

        UpdateModeBanner();
        LoadBox(_boxIndex);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived        -= OnGamepadKey;
        GamepadRouter.BoxScrollRequested -= OnBoxScroll;
#endif
    }

    // ──────────────────────────────────────────────
    //  Box loading
    // ──────────────────────────────────────────────

    private async void LoadBox(int box)
    {
        _boxIndex      = box;
        _currentSlots  = _bank.GetBoxData(box);
        BankNameLabel.Text  = box < _bank.Boxes.Count ? _bank.Boxes[box].Name : $"Bank {box + 1}";
        BoxIndexLabel.Text  = $"{box + 1} / {_bank.Boxes.Count}";

        await _sprites.PreloadBoxAsync(_currentSlots.Select(p => p ?? CreateBlankPKM()).ToArray());
        BankCanvas.InvalidateSurface();
        UpdateHoverLabel();
    }

    private static PKM CreateBlankPKM() => new PK9();

    // ──────────────────────────────────────────────
    //  Box navigation
    // ──────────────────────────────────────────────

    private void OnPrevBox(object sender, EventArgs e)
    {
        if (_boxIndex <= 0) return;
        _boxIndex--;
        DeselectSlot();
        LoadBox(_boxIndex);
    }

    private void OnNextBox(object sender, EventArgs e)
    {
        if (_boxIndex >= _bank.Boxes.Count - 1) return;
        _boxIndex++;
        DeselectSlot();
        LoadBox(_boxIndex);
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
    //  Rendering
    // ──────────────────────────────────────────────

    private void OnBankPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(10, 10, 20));
        if (_currentSlots.Length == 0) return;

        float slotSize = Math.Min((float)e.Info.Width / Columns, (float)e.Info.Height / Rows);
        float offX = ((float)e.Info.Width  - slotSize * Columns) / 2f;
        float offY = ((float)e.Info.Height - slotSize * Rows)    / 2f;
        const float pad    = 4f;
        const float radius = 8f;

        for (int i = 0; i < BankService.SlotsPerBox; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            float x = offX + col * slotSize;
            float y = offY + row * slotSize;
            var pk = i < _currentSlots.Length ? _currentSlots[i] : null;

            bool isCursor   = i == _cursorSlot;
            bool isSelected = i == _selectedSlot;
            bool isSource   = _moveMode && i == _moveSourceSlot;

            // Slot background
            var bgColor = isSelected
                ? new SKColor(80, 60, 20, 220)
                : isCursor
                ? new SKColor(20, 40, 90, 220)
                : new SKColor(14, 14, 36, 180);

            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawRoundRect(x + pad, y + pad, slotSize - pad * 2, slotSize - pad * 2, radius, radius, bgPaint);

            // Sprite — ghost (30% opacity) at source slot, normal elsewhere
            if (pk?.Species > 0)
            {
                var sprite  = _sprites.GetSprite(pk);
                float inner = slotSize - pad * 2;
                float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
                float drawW, drawH;
                if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
                else              { drawH = inner; drawW = inner * aspect; }
                float sx = x + pad + (inner - drawW) / 2f;
                float sy = y + pad + (inner - drawH) / 2f;

                byte alpha = isSource ? (byte)70 : (byte)255;
                using var spritePaint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
                canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH), spritePaint);
            }

            // In deposit mode: show ghost of pending Pokémon at cursor slot
            if (isCursor && App.PendingMove != null && !App.PendingFromBank && _movePk == null)
            {
                var ghost = _sprites.GetSprite(App.PendingMove);
                float inner = slotSize - pad * 2;
                float aspect = ghost.Width > 0 ? (float)ghost.Width / ghost.Height : 1f;
                float drawW, drawH;
                if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
                else              { drawH = inner; drawW = inner * aspect; }
                float sx = x + pad + (inner - drawW) / 2f;
                float sy = y + pad + (inner - drawH) / 2f;
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(110) };
                canvas.DrawBitmap(ghost, SKRect.Create(sx, sy, drawW, drawH), ghostPaint);
            }

            // In withdraw move mode: ghost of grabbed Pokémon at cursor
            if (isCursor && _moveMode && _movePk != null && !isSource)
            {
                var ghost = _sprites.GetSprite(_movePk);
                float inner = slotSize - pad * 2;
                float aspect = ghost.Width > 0 ? (float)ghost.Width / ghost.Height : 1f;
                float drawW, drawH;
                if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
                else              { drawH = inner; drawW = inner * aspect; }
                float sx = x + pad + (inner - drawW) / 2f;
                float sy = y + pad + (inner - drawH) / 2f;
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(110) };
                canvas.DrawBitmap(ghost, SKRect.Create(sx, sy, drawW, drawH), ghostPaint);
            }

            // Outlines
            if (isCursor)
            {
                var color = _moveMode
                    ? new SKColor(60, 220, 110, 230)   // green in move mode
                    : App.PendingMove != null
                    ? new SKColor(60, 200, 255, 230)   // cyan in deposit mode
                    : new SKColor(80, 160, 255, 200);  // blue default
                using var p = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };
                canvas.DrawRoundRect(x + pad, y + pad, slotSize - pad * 2, slotSize - pad * 2, radius, radius, p);
            }

            if (isSelected)
            {
                using var p = new SKPaint { Color = new SKColor(200, 170, 80, 160), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawRoundRect(x + pad, y + pad, slotSize - pad * 2, slotSize - pad * 2, radius, radius, p);
            }

            if (isSource)
            {
                using var p = new SKPaint { Color = new SKColor(220, 80, 60, 190), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
                canvas.DrawRoundRect(x + pad, y + pad, slotSize - pad * 2, slotSize - pad * 2, radius, radius, p);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Touch input
    // ──────────────────────────────────────────────

    private void OnBankTapped(object sender, TappedEventArgs e)
    {
        if (sender is not View view) return;
        var point = e.GetPosition(view);
        if (point is null) return;

        float slotSize = (float)Math.Min(view.Width / Columns, view.Height / Rows);
        float offX = (float)(view.Width  - slotSize * Columns) / 2f;
        float offY = (float)(view.Height - slotSize * Rows)    / 2f;
        int col   = (int)((point.Value.X - offX) / slotSize);
        int row   = (int)((point.Value.Y - offY) / slotSize);
        int index = row * Columns + col;
        if ((uint)index >= BankService.SlotsPerBox) return;

        _cursorSlot = index;
        HandleActivate();
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
            case Android.Views.Keycode.DpadUp:    MoveCursor(-Columns); break;
            case Android.Views.Keycode.DpadDown:  MoveCursor(+Columns); break;
            case Android.Views.Keycode.DpadLeft:  MoveCursor(-1);       break;
            case Android.Views.Keycode.DpadRight: MoveCursor(+1);       break;

            case Android.Views.Keycode.ButtonA:
                HandleActivate(); break;

            case Android.Views.Keycode.ButtonY:
                if (_moveMode) { CancelMoveMode(); break; }
                if (_cursorSlot < _currentSlots.Length && _currentSlots[_cursorSlot]?.Species > 0)
                    EnterWithdrawMode(); break;

            case Android.Views.Keycode.ButtonB:
                if (_moveMode) { CancelMoveMode(); break; }
                if (_selectedSlot >= 0) { DeselectSlot(); break; }
                App.PendingMove = null; // cancel any pending deposit
                _ = SwapToGame();
                break;

            case Android.Views.Keycode.ButtonL1: _ = SwapToGame(-1); break;
            case Android.Views.Keycode.ButtonR1: _ = SwapToGame(+1); break;

            case Android.Views.Keycode.ButtonSelect:
                _ = PromptRenameBox(); break;
        }
    }
#endif

    private void MoveCursor(int delta)
    {
        if (delta == -1 && _cursorSlot % Columns == 0)           return;
        if (delta == +1 && _cursorSlot % Columns == Columns - 1) return;
        int next = _cursorSlot + delta;
        if ((uint)next >= BankService.SlotsPerBox) return;
        _cursorSlot = next;
        BankCanvas.InvalidateSurface();
        UpdateHoverLabel();
    }

    // ──────────────────────────────────────────────
    //  Actions
    // ──────────────────────────────────────────────

    private void HandleActivate()
    {
        // Deposit mode: arriving from game with a Pokémon to place
        if (App.PendingMove != null && !App.PendingFromBank)
        {
            ExecuteDeposit();
            return;
        }

        // Withdraw move mode: drop grabbed Pokémon (swap with slot contents)
        if (_moveMode && _movePk != null)
        {
            ExecuteWithdrawDrop();
            return;
        }

        // Normal: select slot (gold outline)
        if (_cursorSlot < _currentSlots.Length && _currentSlots[_cursorSlot]?.Species > 0)
        {
            if (_cursorSlot == _selectedSlot) DeselectSlot();
            else SelectSlot(_cursorSlot);
        }
        else
        {
            DeselectSlot();
        }
    }

    private void ExecuteDeposit()
    {
        var pk = App.PendingMove!;
        _bank.Deposit(_boxIndex, _cursorSlot, pk);

        // Clear source game slot
        if (App.ActiveSave != null && App.PendingSourceBox >= 0)
        {
            App.ActiveSave.SetBoxSlotAtIndex(
                App.ActiveSave.BlankPKM,
                App.PendingSourceBox,
                App.PendingSourceSlot);
        }

        App.PendingMove = null;
        LoadBox(_boxIndex);
        UpdateModeBanner();
    }

    private void EnterWithdrawMode()
    {
        _movePk        = _currentSlots[_cursorSlot]!.Clone();
        _moveSourceSlot = _cursorSlot;
        _moveMode      = true;
        BankCanvas.InvalidateSurface();
        UpdateModeBanner();
    }

    private void ExecuteWithdrawDrop()
    {
        // Swap: dest → source, grabbed → dest
        var destPk = _currentSlots[_cursorSlot];
        if (destPk?.Species > 0)
            _bank.Deposit(_boxIndex, _moveSourceSlot, destPk);
        else
            _bank.ClearSlot(_boxIndex, _moveSourceSlot);
        _bank.Deposit(_boxIndex, _cursorSlot, _movePk!);
        CancelMoveMode();
        LoadBox(_boxIndex);
    }

    private void CancelMoveMode()
    {
        _moveMode = false;
        _movePk   = null;
        BankCanvas.InvalidateSurface();
        UpdateModeBanner();
    }

    private void SelectSlot(int slot)
    {
        _selectedSlot = slot;
        BankCanvas.InvalidateSurface();
    }

    private void DeselectSlot()
    {
        _selectedSlot = -1;
        BankCanvas.InvalidateSurface();
    }

    // ──────────────────────────────────────────────
    //  Bank ↔ Game swap
    // ──────────────────────────────────────────────

    private async Task SwapToGame(int exitDir = 0)
    {
        // If in withdraw move mode, carry Pokémon to game
        if (_moveMode && _movePk != null)
        {
            App.PendingMove       = _movePk;
            App.PendingSourceBox  = _boxIndex;
            App.PendingSourceSlot = _moveSourceSlot;
            App.PendingFromBank   = true;
            _moveMode = false;
            _movePk   = null;
        }

        // Update slide direction if caller specified one (L1/R1)
        if (exitDir != 0) App.BankSlideDir = exitDir;

#if ANDROID
        GamepadRouter.KeyReceived        -= OnGamepadKey;
        GamepadRouter.BoxScrollRequested -= OnBoxScroll;
#endif

        double exitX = App.BankSlideDir < 0 ? -500 : 500;
        await RootGrid.TranslateTo(exitX, 0, 260, Easing.CubicInOut);
        RootGrid.TranslationX = 0;
        await Navigation.PopModalAsync(animated: false);
    }

    // ──────────────────────────────────────────────
    //  UI helpers
    // ──────────────────────────────────────────────

    private void UpdateModeBanner()
    {
        if (App.PendingMove != null && !App.PendingFromBank)
        {
            var name = App.PendingMove.Species < _strings.specieslist.Length
                ? _strings.specieslist[App.PendingMove.Species] : "?";
            ModeLabel.Text      = $"Depositing  {name}  Lv.{App.PendingMove.CurrentLevel}  — press A to place";
            ModeLabel.TextColor = Color.FromArgb("#44DDAA");
            ModeBanner.BackgroundColor = Color.FromArgb("#0A1E0E");
            ModeBanner.IsVisible = true;
        }
        else if (_moveMode && _movePk != null)
        {
            var name = _movePk.Species < _strings.specieslist.Length
                ? _strings.specieslist[_movePk.Species] : "?";
            ModeLabel.Text      = $"Withdrawing  {name}  Lv.{_movePk.CurrentLevel}  — L1/R1 to carry to box  B to cancel";
            ModeLabel.TextColor = Color.FromArgb("#DDAA44");
            ModeBanner.BackgroundColor = Color.FromArgb("#1E1A0A");
            ModeBanner.IsVisible = true;
        }
        else
        {
            ModeBanner.IsVisible = false;
        }
    }

    private void UpdateHoverLabel()
    {
        var pk = _cursorSlot < _currentSlots.Length ? _currentSlots[_cursorSlot] : null;
        if (pk?.Species > 0)
        {
            var name = pk.Species < _strings.specieslist.Length ? _strings.specieslist[pk.Species] : "?";
            HoverLabel.Text = $"#{pk.Species:000} {name}  Lv.{pk.CurrentLevel}{(pk.IsShiny ? " ✦" : "")}";
        }
        else
        {
            HoverLabel.Text = "Empty slot";
        }
    }

    private async Task PromptRenameBox()
    {
        var current = _boxIndex < _bank.Boxes.Count ? _bank.Boxes[_boxIndex].Name : "";
        var name = await DisplayPromptAsync("Rename Box", "Enter a new name:", initialValue: current, maxLength: 24);
        if (name is null) return;
        _bank.RenameBox(_boxIndex, name.Trim().Length > 0 ? name.Trim() : current);
        BankNameLabel.Text = _bank.Boxes[_boxIndex].Name;
    }

    private async Task DisplayAlertAsync(string title, string message, string cancel)
        => await DisplayAlert(title, message, cancel);
}
