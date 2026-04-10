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
    private int    _detailView;   // 0 = info, 1 = moves
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

        UpdateInfoLabels();
        UpdateMoveLabels();

        if (_previewPk?.Species > 0)
        {
            var pk = _previewPk;
            var spriteUrl = pk.IsShiny
                ? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/shiny/{pk.Species}.png"
                : $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{pk.Species}.png";
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
        _detailView = _detailView == 0 ? 1 : 0;
        InfoPanel.IsVisible  = _detailView == 0;
        MovesPanel.IsVisible = _detailView == 1;
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
