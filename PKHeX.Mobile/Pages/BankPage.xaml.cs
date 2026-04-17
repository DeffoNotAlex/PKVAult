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

    private readonly ISecondaryDisplay        _secondary;
    private readonly SessionState             _session;
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
    private int  _moveSourceBox;
    private int  _moveSourceSlot;

    // Picker state (A on empty slot — gen → species browser, mirrors BankViewPage)
    private enum PickerState { Generation, Species }
    private PickerState           _pickerState;
    private int                   _genCursor;
    private int                   _speciesCursor;
    private readonly List<ushort> _genSpeciesList = [];
    private readonly List<Border> _genBorders     = [];
    private readonly List<Border> _speciesBorders = [];
    private bool                  _pickerOpen;

    private static readonly (int Start, int End, string Region)[] GenRanges =
    [
        (  1, 151, "Kanto"),  (152, 251, "Johto"),
        (252, 386, "Hoenn"),  (387, 493, "Sinnoh"),
        (494, 649, "Unova"),  (650, 721, "Kalos"),
        (722, 809, "Alola"),  (810, 905, "Galar"),
        (906, 9999, "Paldea"),
    ];

    private static readonly Color[] TypeColors =
    [
        Color.FromArgb("#A8A878"), Color.FromArgb("#C03028"), Color.FromArgb("#A890F0"),
        Color.FromArgb("#A040A0"), Color.FromArgb("#E0C068"), Color.FromArgb("#B8A038"),
        Color.FromArgb("#A8B820"), Color.FromArgb("#705898"), Color.FromArgb("#B8B8D0"),
        Color.FromArgb("#F08030"), Color.FromArgb("#6890F0"), Color.FromArgb("#78C850"),
        Color.FromArgb("#F8D030"), Color.FromArgb("#F85888"), Color.FromArgb("#98D8D8"),
        Color.FromArgb("#7038F8"), Color.FromArgb("#705848"), Color.FromArgb("#7038F8"),
    ];

    public BankPage(ISecondaryDisplay secondary, SessionState session)
    {
        _secondary = secondary;
        _session   = session;
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
        RootGrid.TranslationX = _session.BankSlideDir < 0 ? -500 : 500;
        await RootGrid.TranslateToAsync(0, 0, 260, Easing.CubicInOut);

        // Warn once if bank.json was corrupt on load (backup saved alongside it)
        var backupPath = BankService.TakeCorruptionWarning();
        if (backupPath is not null)
            await DisplayAlertAsync("Bank Data Corrupted",
                $"Your bank file was unreadable and has been reset. A backup was saved to:\n{backupPath}",
                "OK");

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
            bool isSource   = _moveMode && _boxIndex == _moveSourceBox && i == _moveSourceSlot;
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
            if (isCursor && _session.PendingMove != null && !_session.PendingFromBank && _movePk == null)
            {
                var ghost = _sprites.GetSprite(_session.PendingMove);
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
                    : _session.PendingMove != null
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
                _session.PendingMove = null; // cancel any pending deposit
                _ = SwapToGame();
                break;

            case Android.Views.Keycode.ButtonL1: _ = SwapToGame(-1); break;
            case Android.Views.Keycode.ButtonR1: _ = SwapToGame(+1); break;

            case Android.Views.Keycode.ButtonSelect:
                OpenBankManageMenu(); break;
        }
    }

    private void HandlePickerKey(Android.Views.Keycode keyCode)
    {
        const int cols = 3; // gen grid columns
        if (_pickerState == PickerState.Generation)
        {
            switch (keyCode)
            {
                case Android.Views.Keycode.DpadLeft:
                    if (_genCursor % cols > 0) { _genCursor--; UpdateGenHighlight(); } break;
                case Android.Views.Keycode.DpadRight:
                    if (_genCursor % cols < cols - 1 && _genCursor + 1 < GenRanges.Length)
                    { _genCursor++; UpdateGenHighlight(); } break;
                case Android.Views.Keycode.DpadUp:
                    if (_genCursor >= cols) { _genCursor -= cols; UpdateGenHighlight(); } break;
                case Android.Views.Keycode.DpadDown:
                    if (_genCursor + cols < GenRanges.Length) { _genCursor += cols; UpdateGenHighlight(); } break;
                case Android.Views.Keycode.ButtonA:
                    _ = SelectGenerationAsync(_genCursor); break;
                case Android.Views.Keycode.ButtonB:
                    ClosePicker(); break;
            }
        }
        else // Species
        {
            const int sCols = 8;
            switch (keyCode)
            {
                case Android.Views.Keycode.DpadLeft:
                    if (_speciesCursor % sCols > 0) { _speciesCursor--; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.DpadRight:
                    if (_speciesCursor % sCols < sCols - 1 && _speciesCursor + 1 < _genSpeciesList.Count)
                    { _speciesCursor++; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.DpadUp:
                    if (_speciesCursor >= sCols) { _speciesCursor -= sCols; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.DpadDown:
                    if (_speciesCursor + sCols < _genSpeciesList.Count)
                    { _speciesCursor += sCols; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.ButtonA:
                    ConfirmPickerSelection(); break;
                case Android.Views.Keycode.ButtonB:
                    ShowGenSelector(); break;
            }
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
        if (_session.PendingMove != null && !_session.PendingFromBank)
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
            OpenPicker();
        }
    }

    private void ExecuteDeposit()
    {
        var pk = _session.PendingMove!;
        _bank.Deposit(_boxIndex, _cursorSlot, pk);

        // Clear source game slot and immediately write back the save file so the
        // bank and the save on disk stay in sync. Without writeback the slot is only
        // cleared in memory; if the user never manually saves, the Pokémon appears
        // in both the save file and the bank (apparent clone).
        if (_session.ActiveSave != null && _session.PendingSourceBox >= 0)
        {
            _session.ActiveSave.SetBoxSlotAtIndex(
                _session.ActiveSave.BlankPKM,
                _session.PendingSourceBox,
                _session.PendingSourceSlot);
            _session.PendingSourceBox  = -1;
            _session.PendingSourceSlot = -1;

            if (!string.IsNullOrEmpty(_session.ActiveSaveFileUri))
            {
                var data = _session.ActiveSave.Write().ToArray();
                _ = Task.Run(async () =>
                {
                    try { await new FileService().WriteBackAsync(data, _session.ActiveSaveFileUri); }
                    catch { /* writeback failure is non-fatal; user can still manually save */ }
                });
            }
        }

        _session.PendingMove = null;
        LoadBox(_boxIndex);
        UpdateModeBanner();
    }

    private void EnterWithdrawMode()
    {
        _movePk         = _currentSlots[_cursorSlot]!.Clone();
        _moveSourceBox  = _boxIndex;
        _moveSourceSlot = _cursorSlot;
        _moveMode       = true;
        BankCanvas.InvalidateSurface();
        UpdateModeBanner();
    }

    private void ExecuteWithdrawDrop()
    {
        // Move grabbed Pokémon to the cursor slot (current box).
        // Clear or swap the original source slot, which may be in a different box.
        var destPk = _currentSlots[_cursorSlot];
        if (destPk?.Species > 0)
            _bank.Deposit(_moveSourceBox, _moveSourceSlot, destPk); // swap dest → source
        else
            _bank.ClearSlot(_moveSourceBox, _moveSourceSlot);       // vacate source
        _bank.Deposit(_boxIndex, _cursorSlot, _movePk!);            // place grabbed → dest
        CancelMoveMode();
        LoadBox(_boxIndex);
    }

    private void CancelMoveMode()
    {
        _moveMode       = false;
        _movePk         = null;
        _moveSourceBox  = -1;
        _moveSourceSlot = -1;
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
            _session.PendingMove       = _movePk;
            _session.PendingSourceBox  = _boxIndex;
            _session.PendingSourceSlot = _moveSourceSlot;
            _session.PendingFromBank   = true;
            _moveMode = false;
            _movePk   = null;
        }

        if (exitDir != 0) _session.BankSlideDir = exitDir;

        double exitX = _session.BankSlideDir < 0 ? -500 : 500;
        await RootGrid.TranslateToAsync(exitX, 0, 260, Easing.CubicInOut);
        RootGrid.TranslationX = 0;
        await Shell.Current.GoToAsync("..", false);
    }

    // ──────────────────────────────────────────────
    //  Picker (A on empty slot — gen → species, mirrors BankViewPage)
    // ──────────────────────────────────────────────

    private void OpenPicker()
    {
        _genCursor = 0;
        _pickerOpen = true;
        PickerOverlay.IsVisible = true;
        ShowGenSelector();
    }

    private void ShowGenSelector()
    {
        _pickerState = PickerState.Generation;
        PickerTitle.Text = "Choose Generation";
        PickerBackBtn.IsVisible = false;
        SpeciesScroll.IsVisible = false;
        GenSelectorGrid.IsVisible = true;

        if (_genBorders.Count == 0)
        {
            for (int i = 0; i < GenRanges.Length; i++)
            {
                var (_, _, region) = GenRanges[i];
                Application.Current!.Resources.TryGetValue("ThCardBg",         out var cardBgRes);
                Application.Current!.Resources.TryGetValue("ThTextPrimary",    out var textPrimaryRes);
                Application.Current!.Resources.TryGetValue("ThTextSecondary",  out var textSecondaryRes);
                Application.Current!.Resources.TryGetValue("ThNavBtnStroke",   out var strokeRes);
                var cardBg    = cardBgRes        is Color cb ? cb : Color.FromArgb("#1A2A44");
                var textPri   = textPrimaryRes   is Color tp ? tp : Color.FromArgb("#EDF0FF");
                var textSec   = textSecondaryRes is Color ts ? ts : Color.FromArgb("#7080A0");
                var strokeCol = strokeRes         is Color st ? st : Color.FromArgb("#334466");

                var content = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions   = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = $"GEN {i + 1}", FontFamily = "NunitoExtraBold", FontSize = 14, TextColor = textPri, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = region,         FontFamily = "Nunito",           FontSize = 10, TextColor = textSec, HorizontalOptions = LayoutOptions.Center },
                    },
                };
                var border = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = 12 },
                    BackgroundColor = cardBg, Stroke = strokeCol, StrokeThickness = 1,
                    Padding = new Thickness(8, 12), Content = content,
                };
                int captured = i;
                border.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(() => _ = SelectGenerationAsync(captured)),
                });
                _genBorders.Add(border);
                GenSelectorGrid.Add(border, i % 3, i / 3);
            }
        }
        UpdateGenHighlight();
    }

    private void UpdateGenHighlight()
    {
        Application.Current!.Resources.TryGetValue("ThNavBtnStroke", out var strokeRes);
        var inactive = strokeRes is Color c ? c : Color.FromArgb("#334466");
        for (int i = 0; i < _genBorders.Count; i++)
        {
            _genBorders[i].Stroke         = i == _genCursor ? Color.FromArgb("#AED6F1") : inactive;
            _genBorders[i].StrokeThickness = i == _genCursor ? 2 : 1;
        }
    }

    private async Task SelectGenerationAsync(int genIndex)
    {
        _pickerState   = PickerState.Species;
        _speciesCursor = 0;
        _genCursor     = genIndex;

        var (start, end, region) = GenRanges[genIndex];
        PickerTitle.Text = $"Gen {genIndex + 1}  —  {region}";
        PickerBackBtn.IsVisible   = true;
        GenSelectorGrid.IsVisible = false;
        SpeciesScroll.IsVisible   = true;
        SpeciesGrid.Children.Clear();
        _genSpeciesList.Clear();
        _speciesBorders.Clear();

        // Yield so MAUI completes a layout pass and ScrollView gets its measured width.
        // Without this, FlexLayout with Wrap="Wrap" wraps at the wrong boundary on Android,
        // causing the grid to appear visually split into two halves.
        await Task.Yield();
        if (SpeciesScroll.Width > 0)
            SpeciesGrid.WidthRequest = SpeciesScroll.Width;

        int maxSpecies = _strings.specieslist.Length - 1;
        for (ushort sp = (ushort)start; sp <= Math.Min(end, maxSpecies); sp++)
            if (!string.IsNullOrWhiteSpace(_strings.specieslist[sp]))
                _genSpeciesList.Add(sp);

        for (int idx = 0; idx < _genSpeciesList.Count; idx++)
        {
            SpeciesGrid.Children.Add(BuildSpeciesCell(_genSpeciesList[idx], idx));
            if (idx % 20 == 0) await Task.Yield();
        }
        UpdateSpeciesHighlight();
    }

    private Border BuildSpeciesCell(ushort species, int index)
    {
        var url  = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{species}.png";
        var name = _strings.specieslist[species];

        Color typeColor;
        try
        {
            var pi = PersonalTable.SV.GetFormEntry(species, 0);
            typeColor = pi.Type1 < TypeColors.Length ? TypeColors[pi.Type1] : TypeColors[0];
        }
        catch { typeColor = TypeColors[0]; }

        var cell = new Grid
        {
            RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)),
            WidthRequest = 80, HeightRequest = 90, Margin = new Thickness(3),
        };
        cell.Add(new Image { Source = new UriImageSource { Uri = new Uri(url), CacheValidity = TimeSpan.FromDays(30) }, Aspect = Aspect.AspectFit, Margin = new Thickness(4) }, 0, 0);
        cell.Add(new Border
        {
            BackgroundColor = typeColor.WithAlpha(0.75f), StrokeThickness = 0, Padding = new Thickness(2),
            Content = new Label { Text = name, FontFamily = "NunitoBold", FontSize = 9, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation },
        }, 0, 1);

        Application.Current!.Resources.TryGetValue("ThCardBg", out var cellBgRes);
        var cellBg = cellBgRes is Color cb2 ? cb2 : Color.FromArgb("#1A2A44");

        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = cellBg, Stroke = Colors.Transparent, StrokeThickness = 2,
            Padding = new Thickness(0), Content = cell,
            WidthRequest = 86, HeightRequest = 96, Margin = new Thickness(3),
        };
        int captured = index;
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { _speciesCursor = captured; ConfirmPickerSelection(); }),
        });
        _speciesBorders.Add(border);
        return border;
    }

    private void UpdateSpeciesHighlight()
    {
        for (int i = 0; i < _speciesBorders.Count; i++)
            _speciesBorders[i].Stroke = i == _speciesCursor ? Color.FromArgb("#AED6F1") : Colors.Transparent;
        ScrollSpeciesIntoView();
    }

    private void ScrollSpeciesIntoView()
    {
        const int cols  = 8;
        const int cellH = 102;
        int row        = _speciesCursor / cols;
        double topY    = row * cellH;
        double botY    = topY + cellH;
        double scrollY = SpeciesScroll.ScrollY;
        double viewH   = SpeciesScroll.Height;
        if (botY > scrollY + viewH)
            _ = SpeciesScroll.ScrollToAsync(0, botY - viewH, false);
        else if (topY < scrollY)
            _ = SpeciesScroll.ScrollToAsync(0, topY, false);
    }

    private void OnPickerBackTapped(object sender, EventArgs e) => ShowGenSelector();

    private void ClosePicker()
    {
        _pickerOpen = false;
        PickerOverlay.IsVisible = false;
    }

    private void ConfirmPickerSelection()
    {
        if (_speciesCursor >= _genSpeciesList.Count) return;
        var species = _genSpeciesList[_speciesCursor];

        PKM pk = _session.ActiveSave is { } sav ? sav.BlankPKM : new PK9();
        pk.Species      = species;
        pk.CurrentLevel = 1;
        pk.Gender       = pk.GetSaneGender();
        do { pk.PID = (uint)Random.Shared.Next(); } while (pk.IsShiny);

        _bank.Deposit(_boxIndex, _cursorSlot, pk);
        LoadBox(_boxIndex);
        ClosePicker();
    }

    // ──────────────────────────────────────────────
    //  UI helpers
    // ──────────────────────────────────────────────

    private void UpdateModeBanner()
    {
        if (_session.PendingMove != null && !_session.PendingFromBank)
        {
            var name = _session.PendingMove.Species < _strings.specieslist.Length
                ? _strings.specieslist[_session.PendingMove.Species] : "?";
            ModeLabel.Text      = $"Depositing  {name}  Lv.{_session.PendingMove.CurrentLevel}  — press A to place";
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

    // ──────────────────────────────────────────────────────────────────────────
    //  Bank box management menu
    // ──────────────────────────────────────────────────────────────────────────

    private void OpenBankManageMenu()
    {
        var name = _boxIndex < _bank.Boxes.Count ? _bank.Boxes[_boxIndex].Name : $"Bank {_boxIndex + 1}";
        _secondary.ShowBankManageMenu(_boxIndex, name, _bank.Boxes.Count, OnBankManageAction);
    }

    // Phone-accessible manage menu: tapping the box name in the header
    private async void OnBoxHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (_secondary.IsAvailable) return; // Thor uses second-screen menu
        var action = await DisplayActionSheetAsync("Manage Box", "Cancel", null,
            "Rename Box", "Add New Box", "Remove Box");
        switch (action)
        {
            case "Rename Box": OnBankManageAction("rename"); break;
            case "Add New Box": OnBankManageAction("add"); break;
            case "Remove Box": OnBankManageAction("remove"); break;
        }
    }

    private void OnBankManageAction(string action)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            switch (action)
            {
                case "rename":
                    await PromptRenameBox();
                    break;

                case "add":
                    _bank.CreateBox($"Bank {_bank.Boxes.Count + 1}");
                    _boxIndex = _bank.Boxes.Count - 1;
                    LoadBox(_boxIndex);
                    break;

                case "remove":
                    await PromptRemoveBox();
                    break;

                // "close" — do nothing, menu already dismissed
            }
        });
    }

    private async Task PromptRemoveBox()
    {
        if (_bank.Boxes.Count <= 1)
        {
            await DisplayAlertAsync("Cannot Remove", "You must have at least one box.", "OK");
            return;
        }

        bool isEmpty = _bank.IsBoxEmpty(_boxIndex);
        string boxName = _bank.Boxes[_boxIndex].Name;

        if (!isEmpty)
        {
            bool confirmed = await DisplayAlertAsync(
                "Remove Box",
                $"\"{boxName}\" contains Pokémon. They will be permanently deleted. Continue?",
                "Remove", "Cancel");
            if (!confirmed) return;
        }

        _bank.RemoveBox(_boxIndex);
        _boxIndex = Math.Max(0, _boxIndex - 1);
        LoadBox(_boxIndex);
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
