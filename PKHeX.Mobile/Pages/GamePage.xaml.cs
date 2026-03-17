using PKHeX.Core;
using PKHeX.Mobile.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;

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
    private int _selectedSlot = -1;   // gold outline (A-confirmed), -1 = none
    private PKM? _previewPk;          // Pokémon shown in top panel (follows cursor)
    private int  _previewSpecies = -1; // debounce WebView reloads
    private bool _loadingBox;
    private bool _spriteWebViewReady; // true after first full HTML load

    // Radar animation
    private float[]                  _radarCurrent = new float[6];
    private float                    _radarVisMax  = 255f;
    private CancellationTokenSource? _radarAnimCts;

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
            _spriteWebViewReady = false;

        if (freshSave)
        {
            _boxIndex    = 0;
            _cursorSlot  = 0;
            DeselectSlot();

            TrainerNameLabel.Text = sav.OT;
            SaveGameLabel.Text    = $"{sav.Version} — Gen {sav.Generation}";
            BoxCountLabel.Text    = $"{sav.BoxCount} boxes · {sav.SlotCount} slots";
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
            _currentBox    = _sav.GetBoxData(box);
            BoxNameLabel.Text = _sav is IBoxDetailName named
                ? named.GetBoxName(box)
                : $"Box {box + 1}";

            // Clear gold outline if the slot is now empty (e.g. Pokémon was moved/deleted in editor)
            if (_selectedSlot >= 0 && (_selectedSlot >= _currentBox.Length
                || _currentBox[_selectedSlot].Species == 0))
                _selectedSlot = -1;

            await _sprites.PreloadBoxAsync(_currentBox);
            BoxCanvas.InvalidateSurface();
            UpdateTopPanel();
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
    //  Rendering — box grid (bottom screen)
    // ──────────────────────────────────────────────

    private void OnBoxPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(10, 10, 20));
        if (_currentBox.Length == 0) return;

        float slotSize = Math.Min((float)e.Info.Width / Columns, (float)e.Info.Height / Rows);
        float offX = ((float)e.Info.Width  - slotSize * Columns) / 2f;
        float offY = ((float)e.Info.Height - slotSize * Rows)    / 2f;

        const float pad    = 4f;
        const float radius = 8f;

        for (int i = 0; i < _currentBox.Length; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            float x = offX + col * slotSize;
            float y = offY + row * slotSize;
            var pk = _currentBox[i];

            bool isCursor   = i == _cursorSlot;
            bool isSelected = i == _selectedSlot;

            var bgColor = isSelected
                ? new SKColor(80, 60, 20, 220)
                : isCursor
                ? new SKColor(30, 50, 100, 220)
                : new SKColor(20, 20, 40, 180);

            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawRoundRect(x + pad, y + pad, slotSize - pad * 2, slotSize - pad * 2, radius, radius, bgPaint);

            if (pk.Species != 0)
            {
                var sprite = _sprites.GetSprite(pk);
                float inner  = slotSize - pad * 2;
                float aspect = sprite.Width > 0 ? (float)sprite.Width / sprite.Height : 1f;
                float drawW, drawH;
                if (aspect >= 1f) { drawW = inner; drawH = inner / aspect; }
                else              { drawH = inner; drawW = inner * aspect; }
                float sx = x + pad + (inner - drawW) / 2f;
                float sy = y + pad + (inner - drawH) / 2f;
                canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH));
            }

            if (isCursor)
            {
                using var p = new SKPaint
                {
                    Color = new SKColor(80, 160, 255, 230),
                    Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true,
                };
                canvas.DrawRoundRect(x + pad, y + pad, slotSize - pad * 2, slotSize - pad * 2, radius, radius, p);
            }

            if (isSelected)
            {
                using var p = new SKPaint
                {
                    Color = new SKColor(200, 170, 80, 160),
                    Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true,
                };
                canvas.DrawRoundRect(x + pad, y + pad, slotSize - pad * 2, slotSize - pad * 2, radius, radius, p);
            }
        }
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
        if (aspect >= 1f) { drawW = w; drawH = w / aspect; }
        else              { drawH = h; drawW = h * aspect; }
        float sx = (w - drawW) / 2f;
        float sy = (h - drawH) / 2f;
        canvas.DrawBitmap(sprite, SKRect.Create(sx, sy, drawW, drawH));
    }

    // ──────────────────────────────────────────────
    //  Rendering — hexagonal stat radar (top-right)
    // ──────────────────────────────────────────────

    private void OnRadarPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_previewPk is null) return;

        const int n    = 6;
        float visMax   = _radarVisMax;
        int[] ringValues = [
            (int)(visMax * 0.20f),
            (int)(visMax * 0.40f),
            (int)(visMax * 0.60f),
            (int)(visMax * 0.80f),
            (int)visMax,
        ];

        string[] labels = ["HP", "Atk", "Def", "Spe", "SpD", "SpA"];

        float margin = Math.Min(e.Info.Width, e.Info.Height) * 0.24f;
        float cx = e.Info.Width  / 2f;
        float cy = e.Info.Height / 2f;
        float r  = Math.Min(cx, cy) - margin;

        // Background grid rings with labels on the HP axis (top)
        using var ringPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        float ringLabelSz = Math.Max(11f, r * 0.10f);
        using var ringFont  = new SKFont(SKTypeface.Default, ringLabelSz);
        using var ringLabelPaint = new SKPaint { Color = new SKColor(80, 100, 150, 180), IsAntialias = true };

        for (int ri = 0; ri < ringValues.Length; ri++)
        {
            float frac = ringValues[ri] / visMax;
            float rr   = r * frac;
            ringPaint.Color = new SKColor(35, 50, 90, (byte)(50 + ri * 20));
            DrawHexPath(canvas, cx, cy, rr, n, ringPaint);

            // Label on the HP axis (straight up, angle = -π/2)
            float lx = cx;
            float ly = cy - rr - ringLabelSz * 0.3f;
            canvas.DrawText(ringValues[ri].ToString(), lx, ly, SKTextAlign.Center, ringFont, ringLabelPaint);
        }

        // Axes
        using var axisPaint = new SKPaint { Color = new SKColor(35, 45, 80, 100), StrokeWidth = 1f, IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            canvas.DrawLine(cx, cy, cx + r * MathF.Cos(angle), cy + r * MathF.Sin(angle), axisPaint);
        }

        // Stat polygon — uses interpolated _radarCurrent
        using var statPath = new SKPath();
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float v  = Math.Clamp(_radarCurrent[i] / visMax, 0f, 1f);
            float px = cx + r * v * MathF.Cos(angle);
            float py = cy + r * v * MathF.Sin(angle);
            if (i == 0) statPath.MoveTo(px, py); else statPath.LineTo(px, py);
        }
        statPath.Close();

        using var fillPaint   = new SKPaint { Color = new SKColor(79, 128, 255, 115), Style = SKPaintStyle.Fill,   IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(120, 175, 255, 235), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true };
        canvas.DrawPath(statPath, fillPaint);
        canvas.DrawPath(statPath, strokePaint);

        // Vertex dots
        using var dotPaint = new SKPaint { Color = new SKColor(160, 205, 255), IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float v  = Math.Clamp(_radarCurrent[i] / visMax, 0f, 1f);
            canvas.DrawCircle(cx + r * v * MathF.Cos(angle), cy + r * v * MathF.Sin(angle), 4.5f, dotPaint);
        }

        // Labels and stat values at each axis tip
        float textR   = r + margin * 0.52f;
        float labelSz = Math.Max(16f, r * 0.15f);
        float valueSz = Math.Max(20f, r * 0.19f);

        using var labelFont = new SKFont(SKTypeface.Default, labelSz);
        using var valueFont = new SKFont(SKTypeface.Default, valueSz) { Embolden = true };
        using var labelPaint = new SKPaint { Color = new SKColor(110, 130, 180), IsAntialias = true };
        using var valuePaint = new SKPaint { Color = new SKColor(225, 235, 255), IsAntialias = true };

        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float lx = cx + textR * MathF.Cos(angle);
            float ly = cy + textR * MathF.Sin(angle);
            canvas.DrawText(labels[i], lx, ly, SKTextAlign.Center, labelFont, labelPaint);
            canvas.DrawText(((int)_radarCurrent[i]).ToString(), lx, ly + valueSz * 1.05f, SKTextAlign.Center, valueFont, valuePaint);
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
            ? Math.Max(target.Max() / 0.85f, 150f)
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
    //  Animated sprite (Pokémon Showdown CDN)
    // ──────────────────────────────────────────────

    private async void LoadAnimatedSprite(PKM pk)
    {
        var name = pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species]
            : pk.Species.ToString();

        var slug = ToShowdownSlug(name);
        var folder   = pk.IsShiny ? "ani-shiny" : "ani";
        var primary  = $"https://play.pokemonshowdown.com/sprites/{folder}/{slug}.gif";
        var fallback = $"https://play.pokemonshowdown.com/sprites/gen5ani/{slug}.gif";

        if (!_spriteWebViewReady)
        {
            SpriteWebView.Source = new HtmlWebViewSource { Html = BuildSpriteShell(primary, fallback) };
            SpriteWebView.IsVisible = true;
            PreviewCanvas.IsVisible = false;
            _spriteWebViewReady = true;
        }
        else
        {
            // Subsequent loads: update only the img src via JS — background stays
            var js = $$"""
                (function(){
                  var img=document.getElementById('s');
                  img.onerror=function(){if(img.src!=='{{fallback}}')img.src='{{fallback}}'};
                  img.src='{{primary}}';
                })();
                """;
            await SpriteWebView.EvaluateJavaScriptAsync(js);
        }
    }

    private static string ToShowdownSlug(string speciesName) => speciesName
        .ToLowerInvariant()
        .Replace("♀", "-f").Replace("♂", "-m")
        .Replace(" ", "-").Replace(".", "")
        .Replace("'", "").Replace(":", "")
        .Replace("é", "e");

    private static string BuildSpriteShell(string primary, string fallback) => $$"""
        <!DOCTYPE html>
        <html><head>
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <style>*{margin:0;padding:0}body{background:transparent;display:flex;align-items:center;justify-content:center;width:100vw;height:100vh;overflow:hidden}</style>
        </head><body>
        <img id="s" src="{{primary}}"
             style="max-width:88%;max-height:88%;image-rendering:pixelated;filter:drop-shadow(0 2px 8px rgba(0,0,0,0.8))"
             onerror="if(this.src!=='{{fallback}}')this.src='{{fallback}}'">
        </body></html>
        """;

    // ──────────────────────────────────────────────
    //  Stats panel — keep square on resize
    // ──────────────────────────────────────────────

    private void OnStatsPanelSizeChanged(object sender, EventArgs e)
    {
        var side = Math.Min(StatsPanelBorder.Width, StatsPanelBorder.Height);
        if (side <= 0) return;
        StatsPanelBorder.WidthRequest  = side;
        StatsPanelBorder.HeightRequest = side;
    }

    // ──────────────────────────────────────────────
    //  Touch input
    // ──────────────────────────────────────────────

    private void OnBoxTapped(object sender, TappedEventArgs e)
    {
        if (_sav is null || sender is not View view) return;

        var point = e.GetPosition(view);
        if (point is null) return;

        float slotSize = (float)Math.Min(view.Width / Columns, view.Height / Rows);
        float offX = (float)(view.Width  - slotSize * Columns) / 2f;
        float offY = (float)(view.Height - slotSize * Rows)    / 2f;

        int col   = (int)((point.Value.X - offX) / slotSize);
        int row   = (int)((point.Value.Y - offY) / slotSize);
        int index = row * Columns + col;

        if ((uint)index >= (uint)_currentBox.Length) return;

        _cursorSlot = index;
        UpdateTopPanel();   // stats update immediately on any tap/cursor move

        if (_currentBox[index].Species != 0)
        {
            if (index == _selectedSlot) _ = OpenEditor();  // tap selected again → edit
            else SelectSlot(index);                         // tap new slot → select
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
            case Android.Views.Keycode.DpadUp:    MoveCursor(-Columns); break;
            case Android.Views.Keycode.DpadDown:  MoveCursor(+Columns); break;
            case Android.Views.Keycode.DpadLeft:  MoveCursor(-1);       break;
            case Android.Views.Keycode.DpadRight: MoveCursor(+1);       break;

            case Android.Views.Keycode.ButtonA:
                if (_cursorSlot < _currentBox.Length && _currentBox[_cursorSlot].Species != 0)
                {
                    if (_cursorSlot == _selectedSlot) _ = OpenEditor();   // 2nd A on same slot → edit
                    else SelectSlot(_cursorSlot);                          // 1st A / new slot → select
                }
                break;

            case Android.Views.Keycode.ButtonB:
                if (_selectedSlot >= 0) DeselectSlot();
                else OnMenuClicked(this, EventArgs.Empty);
                break;

            case Android.Views.Keycode.ButtonL1:
            case Android.Views.Keycode.Button5:
                OnPrevBox(this, EventArgs.Empty); break;

            case Android.Views.Keycode.ButtonR1:
            case Android.Views.Keycode.Button6:
                OnNextBox(this, EventArgs.Empty); break;

            case Android.Views.Keycode.ButtonX:      OnSearchClicked(this, EventArgs.Empty);  break;
            case Android.Views.Keycode.ButtonY:      OnGiftsClicked(this, EventArgs.Empty);   break;
            case Android.Views.Keycode.ButtonSelect: OnSettingsClicked(this, EventArgs.Empty); break;
            case Android.Views.Keycode.ButtonStart:  OnExportClicked(this, EventArgs.Empty);  break;
        }
    }
#endif

    private void MoveCursor(int delta)
    {
        if (delta == -1 && _cursorSlot % Columns == 0)           return;
        if (delta == +1 && _cursorSlot % Columns == Columns - 1) return;

        int next = _cursorSlot + delta;
        if ((uint)next >= (uint)_currentBox.Length) return;

        _cursorSlot = next;
        UpdateTopPanel();
        BoxCanvas.InvalidateSurface();
    }

    // ──────────────────────────────────────────────
    //  Top panel — follows cursor
    // ──────────────────────────────────────────────

    private void UpdateTopPanel()
    {
        if (_currentBox.Length == 0) return;
        var pk = _cursorSlot < _currentBox.Length ? _currentBox[_cursorSlot] : null;
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

        SelectedSpeciesLabel.Text =
            $"#{pk.Species:000} {speciesName}  •  Lv.{pk.CurrentLevel}" +
            (pk.IsShiny ? "  ✦" : "") + $"  {natureName}";

        TopIdlePanel.IsVisible     = false;
        TopSelectedPanel.IsVisible = true;

        // Only reload WebView if the species/shiny changed (avoid flicker on cursor move)
        int key = pk.Species * 2 + (pk.IsShiny ? 1 : 0);
        if (key != _previewSpecies)
        {
            _previewSpecies = key;
            LoadAnimatedSprite(pk);
        }

        PreviewCanvas.InvalidateSurface();
        StartRadarAnimation(GetRadarStats(pk));
    }

    private void ShowIdlePanel()
    {
        _previewPk = null;
        SpriteWebView.IsVisible    = false;
        PreviewCanvas.IsVisible    = true;
        TopIdlePanel.IsVisible     = true;
        TopSelectedPanel.IsVisible = false;
    }

    // ──────────────────────────────────────────────
    //  Selection state machine (gold outline only)
    // ──────────────────────────────────────────────

    /// <summary>Mark slot with gold outline (does not change the top panel display).</summary>
    private void SelectSlot(int slot)
    {
        if (_currentBox[slot].Species == 0) return;
        _selectedSlot = slot;
        BoxCanvas.InvalidateSurface();
    }

    /// <summary>Clear gold outline.</summary>
    private void DeselectSlot()
    {
        _selectedSlot = -1;
        BoxCanvas.InvalidateSurface();
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

    private async void OnMenuClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnSearchClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(DatabasePage));

    private async void OnGiftsClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(MysteryGiftDBPage));

    private async void OnSettingsClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SettingsPage));

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
            await DisplayAlertAsync("Save failed", ex.Message, "OK");
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
