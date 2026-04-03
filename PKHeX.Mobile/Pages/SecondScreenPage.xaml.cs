using Microsoft.Maui.Controls.Shapes;
using PKHeX.Core;
using PKHeX.Mobile.Services;
using PKHeX.Mobile.Theme;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PKHeX.Mobile.Pages;

/// <summary>
/// Full-screen Pokémon detail page rendered on the AYN Thor's second AMOLED display.
/// GamePage drives this page via UpdateTrainer / UpdatePokemon / ClearPokemon.
/// </summary>
public partial class SecondScreenPage : ContentPage
{
    private readonly FileSystemSpriteRenderer _sprites = new();
    private readonly GameStrings _strings = GameInfo.GetStrings("en");

    private PKM?  _previewPk;
    private float[] _radarCurrent = new float[6];
    private float   _radarVisMax  = 255f;
    private CancellationTokenSource? _radarAnimCts;
    private SaveFile? _sav;

    public SecondScreenPage() => InitializeComponent();

    // ──────────────────────────────────────────────
    //  Public API — called by GamePage
    // ──────────────────────────────────────────────

    public void UpdateTrainer(SaveFile sav, string boxName, int filled, int total)
    {
        _sav = sav;

        TrainerNameLabel.Text     = sav.OT;
        SaveGameLabel.Text        = $"Pokémon {sav.Version}  ·  Gen {sav.Generation}";
        TrainerTIDLabel.Text      = sav.TrainerTID7.ToString();
        TrainerPokedexLabel.Text  = sav.BoxCount.ToString();
        TrainerPlaytimeLabel.Text = sav.PlayTimeString;

        var iconFile = GetTrainerIconFile(sav.Version);
        if (iconFile != null)
            TrainerGameIcon.Source = ImageSource.FromStream(
                ct => FileSystem.OpenAppPackageFileAsync($"gameicons/{iconFile}").WaitAsync(ct));

        IdleBoxNameLabel.Text = boxName;
        IdleBoxFillLabel.Text = $"{filled} / {total} filled";

        TopBgCanvas.InvalidateSurface();
    }

    public void UpdateBoxInfo(string boxName, int filled, int total)
    {
        IdleBoxNameLabel.Text = boxName;
        IdleBoxFillLabel.Text = $"{filled} / {total} filled";
    }

    public void UpdatePokemon(PKM pk)
    {
        _previewPk = pk;

        var speciesName = pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species] : pk.Species.ToString();
        var natureName = (int)pk.Nature < _strings.natures.Length
            ? _strings.natures[(int)pk.Nature] : "";

        DetailSpeciesName.Text  = speciesName + (pk.IsShiny ? "  ✦" : "");
        DetailLevelNature.Text  = $"Lv.{pk.CurrentLevel}  ·  {natureName}";

        UpdateTypeBadges(pk);
        UpdateMoveRows(pk);

        var abilityName = pk.Ability < _strings.abilitylist.Length
            ? _strings.abilitylist[pk.Ability] : "—";
        DetailAbility.Text = abilityName;

        var itemName = pk.HeldItem > 0 && pk.HeldItem < _strings.itemlist.Length
            ? _strings.itemlist[pk.HeldItem] : "None";
        DetailItem.Text = itemName;

        TopIdlePanel.IsVisible     = false;
        TopSelectedPanel.IsVisible = true;

        PreviewCanvas.InvalidateSurface();
        StartRadarAnimation(GetRadarStats(pk));
    }

    public void ClearPokemon()
    {
        _previewPk = null;
        TopIdlePanel.IsVisible     = true;
        TopSelectedPanel.IsVisible = false;
        PreviewCanvas.InvalidateSurface();
    }

    // ──────────────────────────────────────────────
    //  Type badges
    // ──────────────────────────────────────────────

    private void UpdateTypeBadges(PKM pk)
    {
        TypeBadgeRow.Children.Clear();
        var types = new List<int> { pk.PersonalInfo.Type1 };
        if (pk.PersonalInfo.Type2 != pk.PersonalInfo.Type1)
            types.Add(pk.PersonalInfo.Type2);

        foreach (var typeId in types)
        {
            var typeName = typeId < _strings.types.Length ? _strings.types[typeId] : "???";
            var color = Theme.TypeColors.Map.TryGetValue(typeName, out var c)
                ? Color.FromUint((uint)((c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue))
                : Color.FromArgb("#A8A878");

            TypeBadgeRow.Children.Add(new Border
            {
                StrokeShape  = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                BackgroundColor = color,
                Stroke       = Colors.Transparent,
                Padding      = new Thickness(10, 2),
                Content      = new Label
                {
                    Text = typeName.ToUpperInvariant(),
                    FontFamily = "NunitoExtraBold",
                    FontSize = 9,
                    TextColor = Colors.White,
                    CharacterSpacing = 0.5,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment   = TextAlignment.Center,
                },
            });
        }
    }

    // ──────────────────────────────────────────────
    //  Move rows
    // ──────────────────────────────────────────────

    private void UpdateMoveRows(PKM pk)
    {
        Label[]  names = [MoveName0, MoveName1, MoveName2, MoveName3];
        Label[]  cats  = [MoveCat0,  MoveCat1,  MoveCat2,  MoveCat3];
        Label[]  pps   = [MovePP0,   MovePP1,   MovePP2,   MovePP3];
        Ellipse[] dots = [MoveDot0,  MoveDot1,  MoveDot2,  MoveDot3];
        Border[] rows  = [MoveRow0,  MoveRow1,  MoveRow2,  MoveRow3];

        int[] moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4];
        int[] ppArr = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];

        for (int i = 0; i < 4; i++)
        {
            if (moves[i] == 0) { rows[i].IsVisible = false; continue; }
            rows[i].IsVisible = true;
            names[i].Text = moves[i] < _strings.movelist.Length ? _strings.movelist[moves[i]] : $"Move {moves[i]}";
            pps[i].Text   = $"PP {ppArr[i]}";
            cats[i].Text  = "";

            var ctx = _sav?.Context ?? EntityContext.Gen9;
            var moveType = MoveInfo.GetType((ushort)moves[i], ctx);
            var tn = moveType < _strings.types.Length ? _strings.types[moveType] : "Normal";
            if (Theme.TypeColors.Map.TryGetValue(tn, out var tc))
                dots[i].Fill = new SolidColorBrush(
                    Color.FromUint((uint)((tc.Alpha << 24) | (tc.Red << 16) | (tc.Green << 8) | tc.Blue)));
        }
    }

    // ──────────────────────────────────────────────
    //  Background canvas
    // ──────────────────────────────────────────────

    private void OnTopBgPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float w = e.Info.Width, h = e.Info.Height;

        using var dimPaint = new SKPaint { Color = new SKColor(7, 12, 26, 60) };
        canvas.DrawRect(0, 0, w, h, dimPaint);

        var gameColor = _sav != null
            ? GameColors.Get(_sav.Version).Light
            : new SKColor(59, 139, 255);
        using var glowPaint1 = new SKPaint();
        glowPaint1.Shader = SKShader.CreateRadialGradient(
            new SKPoint(w * 0.2f, h * 0.5f), MathF.Min(w, h) * 0.5f,
            [gameColor.WithAlpha(20), SKColors.Transparent],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, glowPaint1);

        using var glowPaint2 = new SKPaint();
        glowPaint2.Shader = SKShader.CreateRadialGradient(
            new SKPoint(w * 0.8f, h * 0.3f), MathF.Min(w, h) * 0.45f,
            [new SKColor(167, 139, 250, 15), SKColors.Transparent],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, glowPaint2);
    }

    // ──────────────────────────────────────────────
    //  Static sprite canvas
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
        canvas.DrawBitmap(sprite, SKRect.Create((w - drawW) / 2f, (h - drawH) / 2f, drawW, drawH));
    }

    // ──────────────────────────────────────────────
    //  Stat radar
    // ──────────────────────────────────────────────

    private static readonly SKColor[] StatColors =
    [
        new SKColor(255,  80,  80),
        new SKColor(255, 150,  50),
        new SKColor(240, 210,  50),
        new SKColor( 50, 210, 160),
        new SKColor( 80, 140, 255),
        new SKColor(185,  90, 255),
    ];

    private void OnRadarPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_previewPk is null) return;

        const int n  = 6;
        float visMax = _radarVisMax;
        int[] ringValues = [(int)(visMax * 0.25f), (int)(visMax * 0.50f), (int)(visMax * 0.75f), (int)visMax];
        string[] labels  = ["HP", "Atk", "Def", "Spe", "SpD", "SpA"];

        float margin = MathF.Min(e.Info.Width, e.Info.Height) * 0.24f;
        float cx = e.Info.Width / 2f, cy = e.Info.Height / 2f;
        float r  = MathF.Min(cx, cy) - margin;

        using var ringPaint      = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        float ringLabelSz        = MathF.Max(10f, r * 0.09f);
        using var ringFont       = new SKFont(SKTypeface.Default, ringLabelSz);
        using var ringLabelPaint = new SKPaint { Color = new SKColor(80, 100, 150, 160), IsAntialias = true };

        for (int ri = 0; ri < ringValues.Length; ri++)
        {
            float frac = ringValues[ri] / visMax;
            float rr   = r * frac;
            ringPaint.Color = new SKColor(35, 50, 90, (byte)(40 + ri * 25));
            DrawHexPath(canvas, cx, cy, rr, n, ringPaint);
            canvas.DrawText(ringValues[ri].ToString(), cx, cy - rr - ringLabelSz * 0.25f, SKTextAlign.Center, ringFont, ringLabelPaint);
        }

        using var axisPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        float[] vx = new float[n], vy = new float[n];
        for (int i = 0; i < n; i++)
        {
            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            axisPaint.Color = StatColors[i].WithAlpha(70);
            canvas.DrawLine(cx, cy, cx + r * MathF.Cos(angle), cy + r * MathF.Sin(angle), axisPaint);
            float v = Math.Clamp(_radarCurrent[i] / visMax, 0f, 1f);
            vx[i] = cx + r * v * MathF.Cos(angle);
            vy[i] = cy + r * v * MathF.Sin(angle);
        }

        using var wedgePaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            using var wedgePath = new SKPath();
            wedgePath.MoveTo(cx, cy); wedgePath.LineTo(vx[i], vy[i]); wedgePath.LineTo(vx[j], vy[j]); wedgePath.Close();
            wedgePaint.Color = StatColors[i].WithAlpha(90);
            canvas.DrawPath(wedgePath, wedgePaint);
        }

        using var statPath = new SKPath();
        for (int i = 0; i < n; i++) { if (i == 0) statPath.MoveTo(vx[i], vy[i]); else statPath.LineTo(vx[i], vy[i]); }
        statPath.Close();
        using var strokePaint = new SKPaint { Color = new SKColor(220, 235, 255, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
        canvas.DrawPath(statPath, strokePaint);

        using var dotPaint = new SKPaint { IsAntialias = true };
        float textR  = r + margin * 0.52f;
        float labelSz = MathF.Max(14f, r * 0.14f);
        float valueSz = MathF.Max(18f, r * 0.18f);
        using var labelFont  = new SKFont(SKTypeface.Default, labelSz);
        using var valueFont  = new SKFont(SKTypeface.Default, valueSz) { Embolden = true };
        using var namePaint  = new SKPaint { IsAntialias = true };
        using var valuePaint = new SKPaint { IsAntialias = true };

        for (int i = 0; i < n; i++)
        {
            dotPaint.Color = StatColors[i];
            canvas.DrawCircle(vx[i], vy[i], 5f, dotPaint);

            float angle = MathF.PI * 2 * i / n - MathF.PI / 2;
            float lx = cx + textR * MathF.Cos(angle);
            float ly = cy + textR * MathF.Sin(angle);
            namePaint.Color  = StatColors[i].WithAlpha(180);
            valuePaint.Color = StatColors[i];
            canvas.DrawText(labels[i],                         lx, ly,                   SKTextAlign.Center, labelFont, namePaint);
            canvas.DrawText(((int)_radarCurrent[i]).ToString(), lx, ly + valueSz * 1.1f,  SKTextAlign.Center, valueFont, valuePaint);
        }
    }

    private static float[] GetRadarStats(PKM pk)
    {
        var s = pk.GetStats(pk.PersonalInfo);
        return [(float)s[0], (float)s[1], (float)s[2], (float)s[5], (float)s[4], (float)s[3]];
    }

    private void StartRadarAnimation(float[] target)
    {
        _radarAnimCts?.Cancel();
        _radarAnimCts = new CancellationTokenSource();
        var ct    = _radarAnimCts.Token;
        var start = _radarCurrent.ToArray();
        _radarVisMax = 255f;

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
            float x = cx + r * MathF.Cos(angle), y = cy + r * MathF.Sin(angle);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        path.Close();
        canvas.DrawPath(path, paint);
    }

    // ──────────────────────────────────────────────
    //  Trainer icon helper (same as GamePage)
    // ──────────────────────────────────────────────

    private static string? GetTrainerIconFile(GameVersion v) => v switch
    {
        GameVersion.RD  => "red_vc.png",
        GameVersion.BU  => "blue_vc.png",
        GameVersion.GN  => "green_vc.png",
        GameVersion.YW  => "yellow_vc.png",
        GameVersion.GD  => "gold_vc.png",
        GameVersion.SI  => "silver_vc.png",
        GameVersion.C   => "crystal.png",
        GameVersion.D   => "diamond.png",
        GameVersion.P   => "pearl.png",
        GameVersion.R   => "ruby.png",
        GameVersion.S   => "sapphire.png",
        GameVersion.E   => "emerald.png",
        GameVersion.FR  => "fire_red.png",
        GameVersion.LG  => "leaf_green.png",
        GameVersion.Pt  => "platinum.png",
        GameVersion.HG  => "heartgold.png",
        GameVersion.SS  => "soulsilver.png",
        GameVersion.B   => "black.png",
        GameVersion.W   => "white.png",
        GameVersion.B2  => "black2.png",
        GameVersion.W2  => "white2.png",
        GameVersion.X   => "x.png",
        GameVersion.Y   => "y.png",
        GameVersion.OR  => "omega_ruby.png",
        GameVersion.AS  => "alpha_sapphire.png",
        GameVersion.SN  => "sun.png",
        GameVersion.MN  => "moon.png",
        GameVersion.US  => "ultra_sun.png",
        GameVersion.UM  => "ultra_moon.png",
        GameVersion.GP  => "lets_go_pikachu.jpg",
        GameVersion.GE  => "lets_go_eevee.jpg",
        GameVersion.SW  => "sword.png",
        GameVersion.SH  => "shield.png",
        GameVersion.BD  => "brilliant_diamond.jpg",
        GameVersion.SP  => "shining_pearl.jpg",
        GameVersion.PLA => "legends_arceus.jpg",
        GameVersion.SL  => "scarlet.jpg",
        GameVersion.VL  => "violet.jpg",
        _               => null,
    };
}
