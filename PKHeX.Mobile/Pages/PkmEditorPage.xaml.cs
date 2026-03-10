using PKHeX.Core;
using PKHeX.Drawing.Mobile.QR;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using Microsoft.Maui.Graphics;

namespace PKHeX.Mobile.Pages;

[QueryProperty(nameof(BoxIndexParam), "box")]
[QueryProperty(nameof(SlotIndexParam), "slot")]
public partial class PkmEditorPage : ContentPage
{
    private PKM? _pk;
    private int _boxIndex;
    private int _slotIndex;
    private readonly FileSystemSpriteRenderer _sprites = new();
    private readonly GameStrings _strings = GameInfo.GetStrings("en");
    private bool _movesPopulated;
    private int _currentTab = 0;

    private int[] _focusRow = new int[4];
    private Border[][] _tabRows = null!;

    public string? BoxIndexParam  { set => int.TryParse(value, out _boxIndex); }
    public string? SlotIndexParam { set => int.TryParse(value, out _slotIndex); }

    public PkmEditorPage()
    {
        InitializeComponent();

        // Natures
        foreach (var n in _strings.natures)
            if (!string.IsNullOrWhiteSpace(n))
                NaturePicker.Items.Add(n);

        GenderPicker.Items.Add("Male");
        GenderPicker.Items.Add("Female");
        GenderPicker.Items.Add("Genderless");

        // Languages (index == LanguageID value)
        LanguagePicker.Items.Add("None (0)");
        LanguagePicker.Items.Add("Japanese");
        LanguagePicker.Items.Add("English");
        LanguagePicker.Items.Add("French");
        LanguagePicker.Items.Add("Italian");
        LanguagePicker.Items.Add("German");
        LanguagePicker.Items.Add("—");
        LanguagePicker.Items.Add("Spanish");
        LanguagePicker.Items.Add("Korean");
        LanguagePicker.Items.Add("Chinese (S)");
        LanguagePicker.Items.Add("Chinese (T)");
        LanguagePicker.Items.Add("Spanish (LATAM)");

        BuildTabRows();
    }

    private void BuildTabRows()
    {
        _tabRows =
        [
            [Row_Nickname, Row_Level, Row_Nature, Row_Ability, Row_Gender, Row_Shiny,
             Row_IvHp, Row_IvAtk, Row_IvDef, Row_IvSpA, Row_IvSpD, Row_IvSpe,
             Row_EvHp, Row_EvAtk, Row_EvDef, Row_EvSpA, Row_EvSpD, Row_EvSpe],
            [Row_Move1, Row_Move2, Row_Move3, Row_Move4],
            [Row_MetLevel, Row_Ball, Row_MetLoc, Row_OriginGame],
            [Row_OTName, Row_TID, Row_SID, Row_Language],
        ];
    }

    private void UpdateRowHighlight()
    {
        var rows = _tabRows[_currentTab];
        var focusedBg     = Color.FromArgb("#182845");
        var focusedStroke = Color.FromArgb("#4F80FF");
        var normalBg      = Color.FromArgb("#111827");

        for (int i = 0; i < rows.Length; i++)
        {
            bool focused = i == _focusRow[_currentTab];
            rows[i].BackgroundColor = focused ? focusedBg : normalBg;
            rows[i].Stroke          = focused ? focusedStroke : Colors.Transparent;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif

        if (App.ActiveSave is null) return;

        _pk = App.ActiveSave.GetBoxSlotAtIndex(_boxIndex, _slotIndex);
        await _sprites.PreloadBoxAsync([_pk]);
        PopulateControls();
        SpriteCanvas.InvalidateSurface();
        UpdateRowHighlight();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
    }

    // ──────────────────────────────────────────────
    //  Tab management
    // ──────────────────────────────────────────────

    private void SwitchTab(int tab)
    {
        _currentTab = tab;
        StatsPanel.IsVisible  = tab == 0;
        MovesPanel.IsVisible  = tab == 1;
        MetPanel.IsVisible    = tab == 2;
        OTPanel.IsVisible     = tab == 3;

        if (tab == 1)
            EnsureMovesPopulated();

        UpdateTabHighlights();
        UpdateRowHighlight();
        _ = TabScrollView.ScrollToAsync(0, 0, false);
    }

    private void UpdateTabHighlights()
    {
        var selected   = Color.FromArgb("#4F80FF");
        var unselected = Color.FromArgb("#1A2035");
        var selText    = Colors.White;
        var unselText  = Color.FromArgb("#8888BB");

        TabStats.BackgroundColor  = _currentTab == 0 ? selected : unselected;
        TabMoves.BackgroundColor  = _currentTab == 1 ? selected : unselected;
        TabMet.BackgroundColor    = _currentTab == 2 ? selected : unselected;
        TabOT.BackgroundColor     = _currentTab == 3 ? selected : unselected;

        TabStats.TextColor  = _currentTab == 0 ? selText : unselText;
        TabMoves.TextColor  = _currentTab == 1 ? selText : unselText;
        TabMet.TextColor    = _currentTab == 2 ? selText : unselText;
        TabOT.TextColor     = _currentTab == 3 ? selText : unselText;
    }

    private void OnTabClicked(object sender, EventArgs e)
    {
        int tab = sender == TabStats  ? 0
                : sender == TabMoves  ? 1
                : sender == TabMet    ? 2
                : 3;
        SwitchTab(tab);
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
            case Android.Views.Keycode.ButtonL1:
            case Android.Views.Keycode.Button5:
                SwitchTab((_currentTab + 3) % 4); break;

            case Android.Views.Keycode.ButtonR1:
            case Android.Views.Keycode.Button6:
                SwitchTab((_currentTab + 1) % 4); break;

            case Android.Views.Keycode.ButtonB:
                _ = Shell.Current.GoToAsync(".."); break;

            case Android.Views.Keycode.ButtonStart:
                OnSaveClicked(this, EventArgs.Empty); break;

            case Android.Views.Keycode.DpadUp:
                MoveFocus(-1); break;

            case Android.Views.Keycode.DpadDown:
                MoveFocus(+1); break;

            case Android.Views.Keycode.DpadLeft:
                AdjustCurrentRow(-1); break;

            case Android.Views.Keycode.DpadRight:
                AdjustCurrentRow(+1); break;

            case Android.Views.Keycode.ButtonA:
                ActivateCurrentRow(); break;
        }
    }
#endif

    private void MoveFocus(int delta)
    {
        var rows = _tabRows[_currentTab];
        int next = Math.Clamp(_focusRow[_currentTab] + delta, 0, rows.Length - 1);
        _focusRow[_currentTab] = next;
        UpdateRowHighlight();

        var targetRow = rows[next];
        _ = TabScrollView.ScrollToAsync(targetRow, ScrollToPosition.MakeVisible, true);
    }

    private void AdjustCurrentRow(int delta)
    {
        var row = _tabRows[_currentTab][_focusRow[_currentTab]];
        var tag = row.StyleId;
        if (string.IsNullOrEmpty(tag)) return;

        if (tag.StartsWith("Step:"))
            DoStep(tag[5..], delta);
        else if (tag.StartsWith("Pick:"))
            DoPick(tag[5..], delta);
    }

    private void DoStep(string field, int delta)
    {
        (Entry? entry, int min, int max) = field switch
        {
            "Level"   => ((Entry?)LevelEntry,    1,     100),
            "IV_HP"   => (IvHpEntry,             0,     31),
            "IV_ATK"  => (IvAtkEntry,            0,     31),
            "IV_DEF"  => (IvDefEntry,            0,     31),
            "IV_SPA"  => (IvSpaEntry,            0,     31),
            "IV_SPD"  => (IvSpdEntry,            0,     31),
            "IV_SPE"  => (IvSpeEntry,            0,     31),
            "EV_HP"   => (EvHpEntry,             0,     252),
            "EV_ATK"  => (EvAtkEntry,            0,     252),
            "EV_DEF"  => (EvDefEntry,            0,     252),
            "EV_SPA"  => (EvSpaEntry,            0,     252),
            "EV_SPD"  => (EvSpdEntry,            0,     252),
            "EV_SPE"  => (EvSpeEntry,            0,     252),
            "MetLevel"=> (MetLevelEntry,         0,     100),
            "TID"     => (TIDEntry,              0,     65535),
            "SID"     => (SIDEntry,              0,     65535),
            _         => (null,                  0,     0),
        };

        if (entry is null) return;
        int v = int.TryParse(entry.Text, out var x) ? x : min;
        entry.Text = Math.Clamp(v + delta, min, max).ToString();
    }

    private void DoPick(string field, int delta)
    {
        Picker? picker = field switch
        {
            "Nature"   => NaturePicker,
            "Gender"   => GenderPicker,
            "Move1"    => Move1Picker,
            "Move2"    => Move2Picker,
            "Move3"    => Move3Picker,
            "Move4"    => Move4Picker,
            "Language" => LanguagePicker,
            _          => null,
        };

        if (picker is null) return;
        int count = picker.Items.Count;
        if (count == 0) return;
        picker.SelectedIndex = Math.Clamp(picker.SelectedIndex + delta, 0, count - 1);
    }

    private void ActivateCurrentRow()
    {
        int tab = _currentTab;
        int row = _focusRow[tab];

        switch (tab)
        {
            case 0: // Stats
                switch (row)
                {
                    case 0:  NicknameEntry.Focus(); break;
                    case 1:  LevelEntry.Focus();    break;
                    case 2:  NaturePicker.Focus();  break;
                    case 3:  /* read-only */         break;
                    case 4:  GenderPicker.Focus();  break;
                    case 5:  ShinySwitch.IsToggled = !ShinySwitch.IsToggled; break;
                    case 6:  IvHpEntry.Focus();     break;
                    case 7:  IvAtkEntry.Focus();    break;
                    case 8:  IvDefEntry.Focus();    break;
                    case 9:  IvSpaEntry.Focus();    break;
                    case 10: IvSpdEntry.Focus();    break;
                    case 11: IvSpeEntry.Focus();    break;
                    case 12: EvHpEntry.Focus();     break;
                    case 13: EvAtkEntry.Focus();    break;
                    case 14: EvDefEntry.Focus();    break;
                    case 15: EvSpaEntry.Focus();    break;
                    case 16: EvSpdEntry.Focus();    break;
                    case 17: EvSpeEntry.Focus();    break;
                }
                break;

            case 1: // Moves
                switch (row)
                {
                    case 0: Move1Picker.Focus(); break;
                    case 1: Move2Picker.Focus(); break;
                    case 2: Move3Picker.Focus(); break;
                    case 3: Move4Picker.Focus(); break;
                }
                break;

            case 2: // Met
                if (row == 0) MetLevelEntry.Focus();
                break;

            case 3: // OT
                switch (row)
                {
                    case 0: OTNameEntry.Focus();    break;
                    case 1: TIDEntry.Focus();       break;
                    case 2: SIDEntry.Focus();       break;
                    case 3: LanguagePicker.Focus(); break;
                }
                break;
        }
    }

    private void OnStepDec(object sender, EventArgs e)
    {
        if (sender is Button b && b.StyleId is { Length: > 5 } t) DoStep(t[5..], -1);
    }

    private void OnStepInc(object sender, EventArgs e)
    {
        if (sender is Button b && b.StyleId is { Length: > 5 } t) DoStep(t[5..], +1);
    }

    private void OnPickerDec(object sender, EventArgs e)
    {
        if (sender is Button b && b.StyleId is { Length: > 5 } t) DoPick(t[5..], -1);
    }

    private void OnPickerInc(object sender, EventArgs e)
    {
        if (sender is Button b && b.StyleId is { Length: > 5 } t) DoPick(t[5..], +1);
    }

    // ──────────────────────────────────────────────
    //  Data population
    // ──────────────────────────────────────────────

    private void PopulateControls()
    {
        var pk = _pk!;
        var speciesName = pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species] : pk.Species.ToString();

        SpeciesLabel.Text = $"#{pk.Species:000} {speciesName}";
        SubtitleLabel.Text = $"Lv.{pk.CurrentLevel}" +
            (pk.IsShiny ? " ★" : "") +
            $"  {_strings.natures[(int)pk.Nature]}";

        // QR only supported for Gen 6 and Gen 7
        QRButton.IsEnabled = pk.Generation is 6 or 7;

        // Stats
        NicknameEntry.Text = pk.Nickname;
        LevelEntry.Text = pk.CurrentLevel.ToString();
        NaturePicker.SelectedIndex = (int)pk.Nature < NaturePicker.Items.Count
            ? (int)pk.Nature : 0;
        AbilityLabel.Text = pk.Ability < _strings.abilitylist.Length
            ? _strings.abilitylist[pk.Ability] : pk.Ability.ToString();
        GenderPicker.SelectedIndex = pk.Gender < 3 ? pk.Gender : 2;
        ShinySwitch.IsToggled = pk.IsShiny;

        IvHpEntry.Text  = pk.IV_HP.ToString();
        IvAtkEntry.Text = pk.IV_ATK.ToString();
        IvDefEntry.Text = pk.IV_DEF.ToString();
        IvSpaEntry.Text = pk.IV_SPA.ToString();
        IvSpdEntry.Text = pk.IV_SPD.ToString();
        IvSpeEntry.Text = pk.IV_SPE.ToString();

        EvHpEntry.Text  = pk.EV_HP.ToString();
        EvAtkEntry.Text = pk.EV_ATK.ToString();
        EvDefEntry.Text = pk.EV_DEF.ToString();
        EvSpaEntry.Text = pk.EV_SPA.ToString();
        EvSpdEntry.Text = pk.EV_SPD.ToString();
        EvSpeEntry.Text = pk.EV_SPE.ToString();

        // Moves — deferred until Moves tab first opened
        // Met
        MetLevelEntry.Text = pk.MetLevel.ToString();
        BallLabel.Text = pk.Ball < _strings.balllist.Length
            ? _strings.balllist[pk.Ball] : pk.Ball.ToString();
        MetLocationLabel.Text = pk.MetLocation.ToString();
        OriginGameLabel.Text = pk.Version.ToString();

        // OT
        OTNameEntry.Text = pk.OriginalTrainerName;
        TIDEntry.Text = pk.TID16.ToString();
        SIDEntry.Text = pk.SID16.ToString();
        LanguagePicker.SelectedIndex = pk.Language < LanguagePicker.Items.Count
            ? pk.Language : 0;
    }

    private void EnsureMovesPopulated()
    {
        if (_movesPopulated) return;
        _movesPopulated = true;

        var moves = _strings.movelist;
        foreach (var m in moves)
        {
            var name = string.IsNullOrWhiteSpace(m) ? "—" : m;
            Move1Picker.Items.Add(name);
            Move2Picker.Items.Add(name);
            Move3Picker.Items.Add(name);
            Move4Picker.Items.Add(name);
        }

        if (_pk is { } pk)
        {
            Move1Picker.SelectedIndex = pk.Move1 < moves.Length ? pk.Move1 : 0;
            Move2Picker.SelectedIndex = pk.Move2 < moves.Length ? pk.Move2 : 0;
            Move3Picker.SelectedIndex = pk.Move3 < moves.Length ? pk.Move3 : 0;
            Move4Picker.SelectedIndex = pk.Move4 < moves.Length ? pk.Move4 : 0;
        }
    }

    private void OnSpritePaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_pk is null) return;
        var sprite = _sprites.GetSprite(_pk);
        canvas.DrawBitmap(sprite, SKRect.Create(0, 0, e.Info.Width, e.Info.Height));
    }

    private async void OnQRClicked(object sender, EventArgs e)
    {
        if (_pk is null) return;
        try
        {
            var message = QRMessageUtil.GetMessage(_pk);
            var png = QRGenerator.GeneratePng(message);
            var path = Path.Combine(FileSystem.CacheDirectory, $"qr_{_pk.Species}.png");
            await File.WriteAllBytesAsync(path, png);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "QR Code",
                File = new ShareFile(path, "image/png"),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("QR Error", ex.Message, "OK");
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (_pk is null || App.ActiveSave is null) return;
        ApplyChanges();
        App.ActiveSave.SetBoxSlotAtIndex(_pk, _boxIndex, _slotIndex);
        await Shell.Current.GoToAsync("..");
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private void ApplyChanges()
    {
        var pk = _pk!;

        pk.Nickname = NicknameEntry.Text ?? pk.Nickname;

        if (byte.TryParse(LevelEntry.Text, out var lvl) && lvl is >= 1 and <= 100)
            pk.CurrentLevel = lvl;

        if (NaturePicker.SelectedIndex >= 0)
        {
            pk.Nature = (Nature)NaturePicker.SelectedIndex;
            pk.StatNature = pk.Nature;
        }

        if (GenderPicker.SelectedIndex >= 0)
            pk.Gender = (byte)GenderPicker.SelectedIndex;

        // Shiny — uses PKM.SetShiny() / SetUnshiny() to keep PID consistent
        if (ShinySwitch.IsToggled != pk.IsShiny)
        {
            if (ShinySwitch.IsToggled) pk.SetShiny();
            else pk.SetUnshiny();
        }

        if (TryParseRange(IvHpEntry.Text,  0, 31, out var v)) pk.IV_HP  = v;
        if (TryParseRange(IvAtkEntry.Text, 0, 31, out v))     pk.IV_ATK = v;
        if (TryParseRange(IvDefEntry.Text, 0, 31, out v))     pk.IV_DEF = v;
        if (TryParseRange(IvSpaEntry.Text, 0, 31, out v))     pk.IV_SPA = v;
        if (TryParseRange(IvSpdEntry.Text, 0, 31, out v))     pk.IV_SPD = v;
        if (TryParseRange(IvSpeEntry.Text, 0, 31, out v))     pk.IV_SPE = v;

        if (TryParseRange(EvHpEntry.Text,  0, 252, out v)) pk.EV_HP  = v;
        if (TryParseRange(EvAtkEntry.Text, 0, 252, out v)) pk.EV_ATK = v;
        if (TryParseRange(EvDefEntry.Text, 0, 252, out v)) pk.EV_DEF = v;
        if (TryParseRange(EvSpaEntry.Text, 0, 252, out v)) pk.EV_SPA = v;
        if (TryParseRange(EvSpdEntry.Text, 0, 252, out v)) pk.EV_SPD = v;
        if (TryParseRange(EvSpeEntry.Text, 0, 252, out v)) pk.EV_SPE = v;

        if (_movesPopulated)
        {
            if (Move1Picker.SelectedIndex >= 0) pk.Move1 = (ushort)Move1Picker.SelectedIndex;
            if (Move2Picker.SelectedIndex >= 0) pk.Move2 = (ushort)Move2Picker.SelectedIndex;
            if (Move3Picker.SelectedIndex >= 0) pk.Move3 = (ushort)Move3Picker.SelectedIndex;
            if (Move4Picker.SelectedIndex >= 0) pk.Move4 = (ushort)Move4Picker.SelectedIndex;
        }

        if (byte.TryParse(MetLevelEntry.Text, out var metLvl))
            pk.MetLevel = metLvl;

        pk.OriginalTrainerName = OTNameEntry.Text ?? pk.OriginalTrainerName;

        if (ushort.TryParse(TIDEntry.Text, out var tid)) pk.TID16 = tid;
        if (ushort.TryParse(SIDEntry.Text, out var sid)) pk.SID16 = sid;

        if (LanguagePicker.SelectedIndex >= 0)
            pk.Language = LanguagePicker.SelectedIndex;
    }

    private static bool TryParseRange(string? text, int min, int max, out int value)
    {
        if (int.TryParse(text, out value) && value >= min && value <= max)
            return true;
        value = 0;
        return false;
    }
}
