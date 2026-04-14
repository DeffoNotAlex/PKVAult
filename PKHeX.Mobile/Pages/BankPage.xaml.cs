using Microsoft.Maui.Controls.Shapes;
using PKHeX.Core;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using static PKHeX.Mobile.Services.ThemeService;

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

    // Picker state (A on empty slot — species browser)
    private readonly List<ushort> _pickerSpecies     = [];
    private readonly List<Border> _pickerRowBorders  = [];
    private int  _pickerCursor;
    private bool _pickerOpen;

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
        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        // Slide in the inner grid (not `this`) so hit-testing is always correct
        RootGrid.TranslationX = App.BankSlideDir < 0 ? -500 : 500;
        await RootGrid.TranslateToAsync(0, 0, 260, Easing.CubicInOut);

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
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() =>
        MainThread.BeginInvokeOnMainThread(() => BankCanvas.InvalidateSurface());

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
        canvas.Clear(ThemeService.CanvasBg);
        if (_currentSlots.Length == 0) return;

        const float gap    = 6f;
        const float padX   = 14f, padY = 4f;
        const float radius = 10f;

        float availW   = e.Info.Width  - padX * 2 - gap * (Columns - 1);
        float availH   = e.Info.Height - padY * 2 - gap * (Rows - 1);
        float slotSize = MathF.Min(availW / Columns, availH / Rows);
        float gridW    = slotSize * Columns + gap * (Columns - 1);
        float gridH    = slotSize * Rows    + gap * (Rows - 1);
        float offX     = (e.Info.Width  - gridW) / 2f;
        float offY     = (e.Info.Height - gridH) / 2f;

        for (int i = 0; i < BankService.SlotsPerBox; i++)
        {
            int   col = i % Columns, row = i / Columns;
            float x   = offX + col * (slotSize + gap);
            float y   = offY + row * (slotSize + gap);
            var   rect = new SKRect(x, y, x + slotSize, y + slotSize);
            var   pk   = i < _currentSlots.Length ? _currentSlots[i] : null;

            bool isCursor   = i == _cursorSlot;
            bool isSelected = i == _selectedSlot;
            bool isSource   = _moveMode && i == _moveSourceSlot;
            bool filled     = pk?.Species > 0;

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

            // Sprite — ghost opacity at source slot
            if (filled)
            {
                var sprite = _sprites.GetSprite(pk!);
                float scale = isCursor ? 0.75f : 0.70f;
                float inner = slotSize * scale;
                float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
                float drawW, drawH;
                if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
                else              { drawH = inner; drawW = inner * aspect; }
                float sx = rect.MidX - drawW / 2f;
                float sy = rect.MidY - drawH / 2f;
                byte alpha = isSource ? (byte)70 : (byte)255;
                using var spritePaint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
                canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH), spritePaint);
            }

            // Ghost of pending deposit at cursor
            if (isCursor && App.PendingMove != null && !App.PendingFromBank && _movePk == null)
            {
                var ghost = _sprites.GetSprite(App.PendingMove);
                float inner = slotSize * 0.70f;
                float aspect = ghost.Width > 0 ? (float)ghost.Width / ghost.Height : 1f;
                float drawW, drawH;
                if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
                else              { drawH = inner; drawW = inner * aspect; }
                float sx = rect.MidX - drawW / 2f, sy = rect.MidY - drawH / 2f;
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(110) };
                canvas.DrawBitmap(ghost, SKRect.Create(sx, sy, drawW, drawH), ghostPaint);
            }

            // Ghost of grabbed Pokémon in withdraw move mode
            if (isCursor && _moveMode && _movePk != null && !isSource)
            {
                var ghost = _sprites.GetSprite(_movePk);
                float inner = slotSize * 0.70f;
                float aspect = ghost.Width > 0 ? (float)ghost.Width / ghost.Height : 1f;
                float drawW, drawH;
                if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
                else              { drawH = inner; drawW = inner * aspect; }
                float sx = rect.MidX - drawW / 2f, sy = rect.MidY - drawH / 2f;
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(110) };
                canvas.DrawBitmap(ghost, SKRect.Create(sx, sy, drawW, drawH), ghostPaint);
            }

            // Cursor outline
            if (isCursor)
            {
                var color = _moveMode
                    ? new SKColor(60, 220, 110, 230)
                    : App.PendingMove != null
                    ? new SKColor(60, 200, 255, 230)
                    : new SKColor(80, 160, 255, 200);
                using var p = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };
                canvas.DrawRoundRect(rect, radius, radius, p);
            }

            if (isSelected)
            {
                using var p = new SKPaint { Color = new SKColor(200, 170, 80, 160), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawRoundRect(rect, radius, radius, p);
            }

            if (isSource)
            {
                using var p = new SKPaint { Color = new SKColor(220, 80, 60, 190), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
                canvas.DrawRoundRect(rect, radius, radius, p);
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
        if (_pickerOpen) { HandlePickerKey(keyCode); return; }
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

    private void HandlePickerKey(Android.Views.Keycode keyCode)
    {
        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:
                if (_pickerCursor > 0) { _pickerCursor--; UpdatePickerHighlight(); }
                break;
            case Android.Views.Keycode.DpadDown:
                if (_pickerCursor < _pickerSpecies.Count - 1) { _pickerCursor++; UpdatePickerHighlight(); }
                break;
            case Android.Views.Keycode.ButtonA:
                ConfirmPickerSelection();
                break;
            case Android.Views.Keycode.ButtonB:
                ClosePicker();
                break;
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
            // Empty slot — open picker
            DeselectSlot();
            _ = OpenPickerAsync();
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

        if (exitDir != 0) App.BankSlideDir = exitDir;

        double exitX = App.BankSlideDir < 0 ? -500 : 500;
        await RootGrid.TranslateToAsync(exitX, 0, 260, Easing.CubicInOut);
        RootGrid.TranslationX = 0;
        await Shell.Current.GoToAsync("..", false);
    }

    // ──────────────────────────────────────────────
    //  Picker (A on empty slot)
    // ──────────────────────────────────────────────

    private static int GenForSpecies(ushort s) => s switch
    {
        <= 151 => 1, <= 251 => 2, <= 386 => 3,
        <= 493 => 4, <= 649 => 5, <= 721 => 6,
        <= 809 => 7, <= 905 => 8, _      => 9,
    };

    private async Task OpenPickerAsync()
    {
        try
        {
            _pickerSpecies.Clear();
            _pickerRowBorders.Clear();
            PickerList.Children.Clear();
            _pickerCursor = 0;
            _pickerOpen = true;

            // Show overlay with loading message immediately
            PickerList.Children.Add(new Label
            {
                Text = "Loading Pokédex…",
                FontFamily = "Nunito", FontSize = 12,
                TextColor = Color.FromArgb("#88AABBCC"),
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 12),
            });
            PickerOverlay.IsVisible = true;

            // Build species list off main thread
            var species = await Task.Run(() =>
            {
                var list = new List<ushort>();
                int max = _strings.specieslist.Length - 1;
                for (ushort i = 1; i <= max; i++)
                    if (!string.IsNullOrWhiteSpace(_strings.specieslist[i]))
                        list.Add(i);
                return list;
            });
            _pickerSpecies.AddRange(species);
            PickerList.Children.Clear();

            // Build rows in batches of 30 so UI stays responsive
            int lastGen = -1;
            for (int idx = 0; idx < _pickerSpecies.Count; idx++)
            {
                var sp  = _pickerSpecies[idx];
                int gen = GenForSpecies(sp);
                if (gen != lastGen)
                {
                    lastGen = gen;
                    PickerList.Children.Add(new Label
                    {
                        Text = $"GENERATION {gen}",
                        FontFamily = "NunitoBold", FontSize = 9,
                        TextColor = Color.FromArgb("#5580AA"),
                        CharacterSpacing = 1.2,
                        Margin = new Thickness(2, idx == 0 ? 0 : 8, 0, 2),
                    });
                }
                var border = BuildPickerRow(sp, _pickerRowBorders.Count);
                _pickerRowBorders.Add(border);
                PickerList.Children.Add(border);

                if (idx % 30 == 0) await Task.Yield();
            }

            UpdatePickerHighlight();
        }
        catch (Exception ex)
        {
            PickerList.Children.Clear();
            PickerList.Children.Add(new Label
            {
                Text = $"Error: {ex.Message}",
                FontFamily = "Nunito", FontSize = 11,
                TextColor = Color.FromArgb("#FF6B6B"),
                Margin = new Thickness(8),
            });
        }
    }

    private Border BuildPickerRow(ushort species, int index)
    {
        var spriteUrl = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{species}.png";
        var name = _strings.specieslist[species];

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(new GridLength(40)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)),
            ColumnSpacing = 8,
        };

        grid.Add(new Image
        {
            Source = new UriImageSource { Uri = new Uri(spriteUrl), CacheValidity = TimeSpan.FromDays(30) },
            Aspect = Aspect.AspectFit,
            WidthRequest = 36, HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);

        grid.Add(new Label
        {
            Text = name,
            FontFamily = "NunitoBold", FontSize = 14,
            TextColor = Color.FromArgb("#EDF0FF"),
            VerticalOptions = LayoutOptions.Center,
        }, 1, 0);

        grid.Add(new Label
        {
            Text = $"#{species:000}",
            FontFamily = "Nunito", FontSize = 11,
            TextColor = Color.FromArgb("#5580AA"),
            VerticalOptions = LayoutOptions.Center,
        }, 2, 0);

        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = Color.FromArgb("#0D1A33"),
            Stroke = Color.FromArgb("#1A2A44"),
            StrokeThickness = 1,
            Padding = new Thickness(8, 5),
            Content = grid,
        };
        int captured = index;
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { _pickerCursor = captured; ConfirmPickerSelection(); }),
        });
        return border;
    }

    private void UpdatePickerHighlight()
    {
        for (int i = 0; i < _pickerRowBorders.Count; i++)
        {
            _pickerRowBorders[i].Stroke         = i == _pickerCursor ? Color.FromArgb("#4A90D9") : Color.FromArgb("#1A2A44");
            _pickerRowBorders[i].StrokeThickness = i == _pickerCursor ? 2 : 1;
        }
    }

    private void ClosePicker()
    {
        _pickerOpen = false;
        PickerOverlay.IsVisible = false;
    }

    private void ConfirmPickerSelection()
    {
        if (_pickerCursor >= _pickerSpecies.Count) return;
        var species = _pickerSpecies[_pickerCursor];

        // Create a blank PKM in the active save's format, or fall back to PK9
        PKM pk = App.ActiveSave is { } sav ? sav.BlankPKM : new PK9();
        pk.Species      = species;
        pk.CurrentLevel = 1;
        pk.Gender       = pk.GetSaneGender();

        _bank.Deposit(_boxIndex, _cursorSlot, pk);
        LoadBox(_boxIndex);
        ClosePicker();
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

}
