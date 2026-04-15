using Microsoft.Maui.Controls.Shapes;
using PKHeX.Core;
using PKHeX.Mobile.Services;
using PKHeX.Mobile.Theme;
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
    private bool   _detailToggled;

    // Picker state
    private enum PickerState { Generation, Species }
    private PickerState        _pickerState;
    private int                _genCursor;
    private int                _speciesCursor;
    private readonly List<ushort> _genSpeciesList  = [];
    private readonly List<Border> _genBorders      = [];
    private readonly List<Border> _speciesBorders  = [];
    private bool               _pickerOpen;
    private bool               _bankManageOpen;

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
    ];
    private PKM?   _previewPk;

    // Radar animation
    private float[]                  _radarCurrent = new float[6];
    private float                    _radarVisMax  = 255f;
    private CancellationTokenSource? _radarAnimCts;

    // Radar colors — same order as GamePage
    private static readonly SKColor[] StatColors =
    [
        new SKColor(255,  80,  80),   // HP
        new SKColor(255, 150,  50),   // Atk
        new SKColor(240, 210,  50),   // Def
        new SKColor( 50, 210, 160),   // Spe
        new SKColor( 80, 140, 255),   // SpD
        new SKColor(185,  90, 255),   // SpA
    ];

    public BankViewPage(ISecondaryDisplay secondary)
    {
        _secondary = secondary;
        InitializeComponent();

        // Keep radar box square
        RadarBorder.SizeChanged += (_, _) =>
        {
            if (RadarBorder.Width > 0)
                RadarBorder.HeightRequest = RadarBorder.Width;
        };
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
        _radarAnimCts?.Cancel();
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RadarCanvas.InvalidateSurface();
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
        string boxName  = box < _bank.Boxes.Count ? _bank.Boxes[box].Name : $"Bank {box + 1}";
        int    boxCount = _bank.Boxes.Count;

        BoxNameLabel.Text  = boxName;
        BoxIndexLabel.Text = $"{box + 1} / {boxCount}";

        // Switch the second screen to bank mode immediately (before sprite preload)
        _secondary.ShowBankGrid(_currentSlots, _cursorSlot, boxName, box, boxCount);
        UpdateDetail();

        // Preload sprites in background then refresh
        await _sprites.PreloadBoxAsync(_currentSlots.Select(p => p ?? (PKM)new PK9()).ToArray());
        _secondary.InvalidateBankCanvas();
    }

    // ──────────────────────────────────────────────
    //  Detail panel
    // ──────────────────────────────────────────────

    private void UpdateDetail()
    {
        _previewPk = _cursorSlot < _currentSlots.Length ? _currentSlots[_cursorSlot] : null;

        _detailToggled = false;
        ApplyDetailToggle();
        UpdateInfoLabels();
        UpdateMoveLabels();

        if (_previewPk?.Species > 0)
        {
            var pk = _previewPk;
            var spriteUrl = HomeSpriteCacheService.GetHomeUrl((ushort)pk.Species, pk.Form, pk.IsShiny);
            HomeSpriteImage.Source = new UriImageSource
            {
                Uri = new Uri(spriteUrl),
                CacheValidity = TimeSpan.FromDays(30),
            };
            StartRadarAnimation(GetRadarStats(_previewPk));
        }
        else
        {
            HomeSpriteImage.Source = null;
            _radarAnimCts?.Cancel();
            _radarCurrent = new float[6];
            RadarCanvas.InvalidateSurface();
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

    private void UpdateMoveLabels()
    {
        string MoveName(int id) =>
            id > 0 && id < _strings.movelist.Length ? _strings.movelist[id] : "—";

        var pk = _previewPk;
        MoveLabel0.Text = MoveName(pk?.Move1 ?? 0);
        MoveLabel1.Text = MoveName(pk?.Move2 ?? 0);
        MoveLabel2.Text = MoveName(pk?.Move3 ?? 0);
        MoveLabel3.Text = MoveName(pk?.Move4 ?? 0);
    }

    // ──────────────────────────────────────────────
    //  WebView 3D sprite
    // ──────────────────────────────────────────────

    // ──────────────────────────────────────────────
    //  Canvas: static sprite fallback (box grid only)
    // ──────────────────────────────────────────────

    // ──────────────────────────────────────────────
    //  Canvas: radar chart
    // ──────────────────────────────────────────────

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

        // Grid rings
        using var ringPaint      = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        float ringLabelSz        = Math.Max(10f, r * 0.09f);
        using var ringFont       = new SKFont(SKTypeface.Default, ringLabelSz);
        using var ringLabelPaint = new SKPaint { Color = ThemeService.RadarStat, IsAntialias = true };

        for (int ri = 0; ri < ringValues.Length; ri++)
        {
            float frac = ringValues[ri] / visMax;
            ringPaint.Color = ThemeService.RadarGrid.WithAlpha((byte)(40 + ri * 25));
            DrawHexPath(canvas, cx, cy, r * frac, n, ringPaint);
            float ly = cy - r * frac - ringLabelSz * 0.25f;
            canvas.DrawText(ringValues[ri].ToString(), cx, ly, SKTextAlign.Center, ringFont, ringLabelPaint);
        }

        // Axes
        using var axisPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            axisPaint.Color = StatColors[i].WithAlpha(70);
            canvas.DrawLine(cx, cy, cx + r * MathF.Cos(angle), cy + r * MathF.Sin(angle), axisPaint);
        }

        // Vertex positions
        float[] vx = new float[n];
        float[] vy = new float[n];
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float v = Math.Clamp(_radarCurrent[i] / visMax, 0f, 1f);
            vx[i] = cx + r * v * MathF.Cos(angle);
            vy[i] = cy + r * v * MathF.Sin(angle);
        }

        // Colored wedge fills
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

        // Outline stroke
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

        // Vertex dots
        using var dotPaint = new SKPaint { IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            dotPaint.Color = StatColors[i];
            canvas.DrawCircle(vx[i], vy[i], 5f, dotPaint);
        }

        // Axis labels (name + value)
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
            canvas.DrawText(labels[i],                              lx, ly,                  SKTextAlign.Center, labelFont, namePaint);
            canvas.DrawText(((int)_radarCurrent[i]).ToString(),     lx, ly + valueSz * 1.1f, SKTextAlign.Center, valueFont, valuePaint);
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
            ? Math.Max(target.Max() / 0.78f, 80f)
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
        if (_previewPk?.Species is 0 or null) return;
        _detailToggled = !_detailToggled;
        ApplyDetailToggle();
        if (_detailToggled) UpdateCompatPanel(_previewPk);
    }

    private void ApplyDetailToggle()
    {
        RadarBorder.IsVisible  = !_detailToggled;
        CompatPanel.IsVisible  =  _detailToggled;
        InfoPanel.IsVisible    = !_detailToggled;
        MovesPanel.IsVisible   =  _detailToggled;
    }

    private void UpdateCompatPanel(PKM pk)
    {
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

        Application.Current!.Resources.TryGetValue("ThCardBg",        out var cardBgRes);
        Application.Current!.Resources.TryGetValue("ThNavBtnStroke",  out var strokeRes);
        Application.Current!.Resources.TryGetValue("ThTextPrimary",   out var textPriRes);
        Application.Current!.Resources.TryGetValue("ThTextSecondary", out var textSecRes);
        var cardBg  = cardBgRes  is Color cb ? cb : Color.FromArgb("#1A2A44");
        var stroke  = strokeRes  is Color st ? st : Color.FromArgb("#334466");
        var textPri = textPriRes is Color tp ? tp : Color.FromArgb("#EDF0FF");
        var textSec = textSecRes is Color ts ? ts : Color.FromArgb("#7080A0");

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

            var gameIdx  = (int)save.Version;
            var gameName = gameIdx > 0 && gameIdx < _strings.gamelist.Length
                ? _strings.gamelist[gameIdx]
                : save.Version.ToString();

            var nameStack = new VerticalStackLayout { Spacing = 0 };
            nameStack.Add(new Label
            {
                Text = gameName,
                FontFamily = "NunitoBold",
                FontSize = 11,
                TextColor = textPri,
                LineBreakMode = LineBreakMode.TailTruncation,
            });
            nameStack.Add(new Label
            {
                Text = $"{save.FileName}  ·  {save.TrainerName}",
                FontFamily = "Nunito",
                FontSize = 9,
                TextColor = textSec,
                LineBreakMode = LineBreakMode.TailTruncation,
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

            CompatList.Children.Add(new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = cardBg,
                Stroke = stroke,
                StrokeThickness = 1,
                Padding = new Thickness(8, 6),
                Content = row,
            });
        }
    }

    private enum CompatStatus { Green, Yellow, Red }

    // Max species index per generation (index = gen number)
    private static readonly int[] MaxSpeciesPerGen = [0, 151, 251, 386, 493, 649, 721, 809, 905, 1025];

    private static CompatStatus GetCompatStatus(PKM pk, SaveEntry save)
    {
        if (save.Generation == pk.Format) return CompatStatus.Green;
        int maxSp = save.Generation < MaxSpeciesPerGen.Length ? MaxSpeciesPerGen[save.Generation] : 1025;
        return pk.Species <= maxSp ? CompatStatus.Yellow : CompatStatus.Red;
    }

    // ──────────────────────────────────────────────
    //  Touch handlers
    // ──────────────────────────────────────────────

    private void OnPrevBoxTapped(object sender, EventArgs e)
    {
        if (_boxIndex > 0) _ = LoadBoxAsync(_boxIndex - 1);
    }

    private void OnNextBoxTapped(object sender, EventArgs e)
    {
        if (_boxIndex < _bank.Boxes.Count - 1) _ = LoadBoxAsync(_boxIndex + 1);
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
        if (_bankManageOpen)
        {
            // B closes the menu (second screen handles it internally too, that's fine)
            if (keyCode == Android.Views.Keycode.ButtonB)
            {
                _bankManageOpen = false;
                _secondary.HideBankManageMenu();
            }
            return; // eat all keys while manage menu is open
        }
        if (_pickerOpen) { HandlePickerKey(keyCode); return; }
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
            case Android.Views.Keycode.ButtonA:
                if (_previewPk is null || _previewPk.Species == 0) OpenPicker();
                break;
            case Android.Views.Keycode.ButtonY:
                if (_previewPk?.Species > 0) _ = TryDeleteSlotAsync();
                break;
            case Android.Views.Keycode.ButtonX:
                CycleDetailView(); break;
            case Android.Views.Keycode.ButtonB:
                _ = Shell.Current.GoToAsync(".."); break;
            case Android.Views.Keycode.ButtonSelect:
                OpenBankManageMenu(); break;
        }
    }

    private void OpenBankManageMenu()
    {
        _bankManageOpen = true;
        var name = _boxIndex < _bank.Boxes.Count ? _bank.Boxes[_boxIndex].Name : $"Bank {_boxIndex + 1}";
        _secondary.ShowBankManageMenu(_boxIndex, name, _bank.Boxes.Count, OnBankManageAction);
    }

    private void OnBankManageAction(string action)
    {
        _bankManageOpen = false;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            switch (action)
            {
                case "rename":
                    var current = _boxIndex < _bank.Boxes.Count ? _bank.Boxes[_boxIndex].Name : "";
                    var newName = await DisplayPromptAsync("Rename Box", "Enter a new name:", initialValue: current, maxLength: 24);
                    if (newName is null) break;
                    _bank.RenameBox(_boxIndex, newName.Trim().Length > 0 ? newName.Trim() : current);
                    await LoadBoxAsync(_boxIndex, resetCursor: false);
                    break;

                case "add":
                    _bank.CreateBox($"Bank {_bank.Boxes.Count + 1}");
                    await LoadBoxAsync(_bank.Boxes.Count - 1);
                    break;

                case "remove":
                    if (_bank.Boxes.Count <= 1)
                    {
                        await DisplayAlert("Cannot Remove", "You must have at least one box.", "OK");
                        break;
                    }
                    if (!_bank.IsBoxEmpty(_boxIndex))
                    {
                        bool confirmed = await DisplayAlert(
                            "Remove Box",
                            $"\"{_bank.Boxes[_boxIndex].Name}\" contains Pokémon. They will be permanently deleted. Continue?",
                            "Remove", "Cancel");
                        if (!confirmed) break;
                    }
                    _bank.RemoveBox(_boxIndex);
                    await LoadBoxAsync(Math.Max(0, _boxIndex - 1));
                    break;
            }
        });
    }

    private void HandlePickerKey(Android.Views.Keycode keyCode)
    {
        if (_pickerState == PickerState.Generation)
        {
            const int cols = 3;
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
        else
        {
            const int cols = 8;
            switch (keyCode)
            {
                case Android.Views.Keycode.DpadLeft:
                    if (_speciesCursor % cols > 0) { _speciesCursor--; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.DpadRight:
                    if (_speciesCursor % cols < cols - 1 && _speciesCursor + 1 < _genSpeciesList.Count)
                    { _speciesCursor++; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.DpadUp:
                    if (_speciesCursor >= cols) { _speciesCursor -= cols; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.DpadDown:
                    if (_speciesCursor + cols < _genSpeciesList.Count)
                    { _speciesCursor += cols; UpdateSpeciesHighlight(); } break;
                case Android.Views.Keycode.ButtonA:
                    ConfirmPickerSelection(); break;
                case Android.Views.Keycode.ButtonB:
                    ShowGenSelector(); break;
            }
        }
    }
#endif

    // ──────────────────────────────────────────────
    //  Pokémon picker (A on empty slot)
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

        // Build gen buttons once
        if (_genBorders.Count == 0)
        {
            for (int i = 0; i < GenRanges.Length; i++)
            {
                var (start, end, region) = GenRanges[i];
                Application.Current!.Resources.TryGetValue("ThCardBg",        out var cardBgRes);
                Application.Current!.Resources.TryGetValue("ThTextPrimary",   out var textPrimaryRes);
                Application.Current!.Resources.TryGetValue("ThTextSecondary", out var textSecondaryRes);
                Application.Current!.Resources.TryGetValue("ThNavBtnStroke",  out var strokeRes);
                var cardBg    = cardBgRes       is Color cb ? cb : Color.FromArgb("#1A2A44");
                var textPri   = textPrimaryRes  is Color tp ? tp : Color.FromArgb("#EDF0FF");
                var textSec   = textSecondaryRes is Color ts ? ts : Color.FromArgb("#7080A0");
                var strokeCol = strokeRes        is Color st ? st : Color.FromArgb("#334466");

                var label = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions   = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = $"GEN {i + 1}",
                            FontFamily = "NunitoExtraBold", FontSize = 14,
                            TextColor = textPri,
                            HorizontalOptions = LayoutOptions.Center,
                        },
                        new Label
                        {
                            Text = region,
                            FontFamily = "Nunito", FontSize = 10,
                            TextColor = textSec,
                            HorizontalOptions = LayoutOptions.Center,
                        },
                    },
                };

                var border = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = 12 },
                    BackgroundColor = cardBg,
                    Stroke = strokeCol,
                    StrokeThickness = 1,
                    Padding = new Thickness(8, 12),
                    Content = label,
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
        _pickerState = PickerState.Species;
        _speciesCursor = 0;
        _genCursor = genIndex;

        var (start, end, region) = GenRanges[genIndex];
        PickerTitle.Text = $"Gen {genIndex + 1}  —  {region}";
        PickerBackBtn.IsVisible = true;
        GenSelectorGrid.IsVisible = false;
        SpeciesScroll.IsVisible = true;
        SpeciesGrid.Children.Clear();
        _genSpeciesList.Clear();
        _speciesBorders.Clear();

        // Collect species for this generation
        int maxSpecies = _strings.specieslist.Length - 1;
        for (ushort sp = (ushort)start; sp <= Math.Min(end, maxSpecies); sp++)
        {
            if (!string.IsNullOrWhiteSpace(_strings.specieslist[sp]))
                _genSpeciesList.Add(sp);
        }

        // Build cells in batches
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

        // Get primary type color
        Color typeColor;
        try
        {
            var pi = PersonalTable.SV.GetFormEntry(species, 0);
            int t  = pi.Type1;
            typeColor = t < TypeColors.Length ? TypeColors[t] : TypeColors[0];
        }
        catch { typeColor = TypeColors[0]; }

        var cell = new Grid
        {
            RowDefinitions = new RowDefinitionCollection(
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)),
            WidthRequest  = 80,
            HeightRequest = 90,
            Margin        = new Thickness(3),
        };

        cell.Add(new Image
        {
            Source = new UriImageSource { Uri = new Uri(url), CacheValidity = TimeSpan.FromDays(30) },
            Aspect = Aspect.AspectFit,
            Margin = new Thickness(4),
        }, 0, 0);

        // Type-colored name overlay
        var nameBar = new Border
        {
            BackgroundColor = typeColor.WithAlpha(0.75f),
            StrokeThickness = 0,
            Padding = new Thickness(2, 2),
            Content = new Label
            {
                Text = name,
                FontFamily = "NunitoBold", FontSize = 9,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            },
        };
        cell.Add(nameBar, 0, 1);

        Application.Current!.Resources.TryGetValue("ThCardBg", out var cellBgRes);
        var cellBg = cellBgRes is Color cb2 ? cb2 : Color.FromArgb("#1A2A44");

        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = cellBg,
            Stroke = Colors.Transparent, // UpdateSpeciesHighlight paints the selected cell
            StrokeThickness = 2,
            Padding = new Thickness(0),
            Content = cell,
            WidthRequest  = 86,
            HeightRequest = 96,
            Margin        = new Thickness(3),
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
        ScrollSpeciesIntoViewIfNeeded();
    }

    private void ScrollSpeciesIntoViewIfNeeded()
    {
        const int cols    = 8;
        const int cellH   = 102; // HeightRequest(96) + Margin(3*2)
        int row   = _speciesCursor / cols;
        double topY       = row * cellH;
        double botY       = topY + cellH;
        double scrollTop  = SpeciesScroll.ScrollY;
        double viewH      = SpeciesScroll.Height;

        if (botY > scrollTop + viewH)
            _ = SpeciesScroll.ScrollToAsync(0, botY - viewH, false);
        else if (topY < scrollTop)
            _ = SpeciesScroll.ScrollToAsync(0, topY, false);
    }

    private static void SetDefaultMoves(PKM pk)
    {
        try
        {
            var learnset = LearnSource9SV.Instance.GetLearnset((ushort)pk.Species, 0);
            byte level = (byte)Math.Min((int)pk.CurrentLevel, 100);
            var moves = learnset.GetMoveRange(level);
            if (moves.IsEmpty)
                moves = learnset.GetAllMoves(); // fallback: full learnset
            if (moves.IsEmpty) return;
            int start = Math.Max(0, moves.Length - 4);
            pk.SetMoves(moves[start..]);
        }
        catch { }
    }

    private async Task TryDeleteSlotAsync()
    {
        var name = _previewPk?.Species > 0 && _previewPk.Species < _strings.specieslist.Length
            ? _strings.specieslist[_previewPk.Species]
            : "this Pokémon";
        bool ok = await DisplayAlertAsync("Remove from Bank", $"Remove {name} from the bank?", "Remove", "Cancel");
        if (!ok) return;
        _bank.ClearSlot(_boxIndex, _cursorSlot);
        await LoadBoxAsync(_boxIndex, resetCursor: false);
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
        var sp = _genSpeciesList[_speciesCursor];
        PKM pk = App.ActiveSave is { } sav ? sav.BlankPKM : new PK9();
        pk.Species      = sp;
        pk.CurrentLevel = 1;
        pk.Gender       = pk.GetSaneGender();
        // Randomize PID until not shiny (PID=0 is shiny in most games)
        do { pk.PID = (uint)Random.Shared.Next(); } while (pk.IsShiny);
        SetDefaultMoves(pk);
        _bank.Deposit(_boxIndex, _cursorSlot, pk);
        _ = LoadBoxAsync(_boxIndex, resetCursor: false);
        ClosePicker();
    }
}
