# PKHeX Android — Box Viewer Implementation Bridge Doc

## Purpose

This document translates the HTML design mockup (`pkhex-boxviewer.html`) into concrete .NET MAUI and SkiaSharp implementation guidance for the Box Viewer screen (GamePage). This is the main screen of the app — where users browse, inspect, and move Pokémon.

Use this alongside:
- `PKHeX-Implementation-Bridge.md` (Home screen — contains the shared color system, typography, and dual-screen abstraction)
- `pkhex-boxviewer.html` (visual reference)
- The original design brief

---

## 1. Screen Layout Overview

```
┌─────────────────── TOP SCREEN (1920×1080) ──────────────────┐
│                                                              │
│  IDLE STATE:   [Trainer Art]  Name/Game/Stats  │  Box Info   │
│                                                              │
│  SELECTED:     [Sprite]  │  [Stat Radar]  │  [Moves List]   │
│                ~30%       │    ~35%         │    ~35%         │
│                                                              │
└──────────────────────────────────────────────────────────────┘

               ═══════════ HINGE ═══════════

┌─────────────── BOTTOM SCREEN (1080×1240) ────────────────────┐
│  ◀ L          BOX 2           R ▶                            │
│               L / R                                          │
│                                                              │
│  ┌────┬────┬────┬────┬────┬────┐                             │
│  │    │    │    │    │    │    │                              │
│  ├────┼────┼────┼────┼────┼────┤                             │
│  │    │    │    │    │    │    │   6 × 5 grid                │
│  ├────┼────┼────┼────┼────┼────┤                             │
│  │    │    │    │    │    │    │                              │
│  ├────┼────┼────┼────┼────┼────┤                             │
│  │    │    │    │    │    │    │                              │
│  ├────┼────┼────┼────┼────┼────┤                             │
│  │    │    │    │    │    │    │                              │
│  └────┴────┴────┴────┴────┴────┘                             │
│                                                              │
│  #101 Electrode · Lv.38       [A]Select [Y]Grab [B]Back [St] │
└──────────────────────────────────────────────────────────────┘
```

---

## 2. Rendering Approach

| Screen | Renderer | Reason |
|---|---|---|
| Top screen — idle (trainer card) | SKCanvasView | Custom gradients, animated background, flexible layout |
| Top screen — selected (detail) | SKCanvasView | Stat radar is SkiaSharp-drawn, sprite rendering, type badges |
| Bottom screen — box header | MAUI XAML | Simple label + two buttons, standard layout |
| Bottom screen — box grid | SKCanvasView | Cursor glow/pulse requires per-frame rendering, sprite drawing |
| Bottom screen — info bar | MAUI XAML | Text label + static hint glyphs |

The bottom screen is a `Grid` with three rows: `Auto` (header), `*` (SkiaSharp grid), `Auto` (info bar). The SkiaSharp canvas handles the 6×5 grid and all cursor effects.

---

## 3. Top Screen — Idle State (Trainer Card)

**Full SKCanvasView, 1920×1080.**

### Background

```
Layer 1: Solid fill BgDeep (#070C1A)
Layer 2: Radial gradient at (20%, 50%) — game accent color at 8% opacity, transparent at 50% radius
Layer 3: Radial gradient at (80%, 30%) — AccentPurple at 6% opacity, transparent at 45% radius
Layer 4: Linear gradient 135° — #070C1A → #0D1530
```

All gradients composited with `SKBlendMode.SrcOver`.

### Layout (horizontal)

```
Left margin: 40dp

[Trainer Artwork Circle]  32dp gap  [Trainer Info]  flex  [Divider]  40dp  [Box Info]  40dp right margin
```

### Trainer Artwork Circle

| Property | Value |
|---|---|
| Size | 140×140dp |
| Shape | Circle (70dp radius) |
| Fill | Linear gradient 135°: game accent at 15% → AccentPurple at 10% |
| Border | 2dp stroke, white at 6% opacity |
| Content | Game icon image centered, or fallback emoji/letter |

### Trainer Info Block

| Element | Font | Size | Color |
|---|---|---|---|
| Trainer name | Nunito | 28dp, weight 900 | TextPrimary |
| Game label | Nunito | 14dp, weight 600 | TextSecondary |
| Stat labels (TID, Pokédex, Playtime) | Nunito | 9dp, weight 700, uppercase, 0.8px tracking | TextDim |
| Stat values | Nunito | 16dp, weight 800 | TextPrimary |

Stats are laid out horizontally with 24dp gap between each column.

### Divider

| Property | Value |
|---|---|
| Width | 1dp |
| Height | 120dp |
| Color | Linear gradient top-to-bottom: transparent → white 6% → transparent |
| Position | `margin-left: auto; margin-right: 40dp` (pushed right of trainer info) |

### Box Info (right-aligned)

| Element | Font | Size | Color | Alignment |
|---|---|---|---|---|
| "Current Box" label | Nunito | 10dp, weight 700, uppercase, 1px tracking | TextDim | Right |
| Box name (e.g. "BOX 2") | Quicksand | 20dp, weight 800 | TextPrimary | Right |
| Fill count (e.g. "12 / 30 filled") | Nunito | 12dp, weight 600 | TextSecondary | Right |

### Transition

When a Pokémon is selected (A pressed on filled slot), the trainer card fades out (`opacity 1→0, scale 1→0.97, 400ms ease`) and the detail view fades in (`opacity 0→1, translateY 8→0, 400ms ease`). Both transitions run simultaneously.

---

## 4. Top Screen — Selected State (Pokémon Detail)

**Same SKCanvasView, content switches.**

### Background

```
Layer 1: Solid fill BgDeep
Layer 2: Radial gradient at (15%, 50%) — AccentBlue at 6%, transparent at 40% radius
Layer 3: Linear gradient 170° — #070C1A → #0B1225
```

### Column 1: Sprite (~30% width)

#### Type Badges (top of column, centered)
| Property | Value |
|---|---|
| Layout | Horizontal row, 5dp gap, centered above sprite |
| Position | 12dp from top of column |
| Badge shape | Rounded rect, 10dp radius, horizontal padding 10dp, vertical 2dp |
| Badge fill | Solid type color (see Type Color Map below) |
| Badge text | 9dp, Nunito weight 800, uppercase, 0.5px tracking, white, drop shadow (0 1dp 2dp black 30%) |

#### Sprite Display
| Property | Value |
|---|---|
| Container | 160×160dp centered in column |
| Glow | Circle behind sprite, inset -20dp, filled with primary type color, blur 30dp, opacity 15% |
| Sprite image | 120×120dp, `SKFilterQuality.None` for pixel art (nearest-neighbor), drop shadow (0 4dp 12dp black 50%) |

In the real app, this renders the animated GIF sprite. For SkiaSharp, decode the GIF frames with `SKCodec` and cycle them on a timer. Alternatively, continue using a `WebView` overlay positioned within this column (as the current app does).

#### Name Plate (bottom of column)
| Element | Font | Size | Color |
|---|---|---|---|
| Species name | Nunito | 16dp, weight 800 | TextPrimary |
| "Lv.38 · Calm" | Nunito | 11dp, weight 600 | TextSecondary |

Centered horizontally, positioned 8dp from bottom of column.

### Column 2: Stat Radar (~35% width)

**Canvas size: 220×220dp logical (440×440px at 2x for crisp rendering).**

#### Hexagonal Grid

Draw 4 concentric hexagonal rings at 25%, 50%, 75%, 100% of max radius (155dp logical / 160dp with padding).

```csharp
for (int ring = 1; ring <= 4; ring++)
{
    float r = (ring / 4f) * 155f * scale;
    var path = new SKPath();
    for (int i = 0; i <= 6; i++)
    {
        float angle = (MathF.PI * 2 / 6) * i - MathF.PI / 2;
        float x = cx + r * MathF.Cos(angle);
        float y = cy + r * MathF.Sin(angle);
        if (i == 0) path.MoveTo(x, y);
        else path.LineTo(x, y);
    }
    path.Close();
    canvas.DrawPath(path, new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = new SKColor(255, 255, 255, (byte)(ring == 4 ? 20 : 10)),
        StrokeWidth = 1,
        IsAntialias = true
    });
}
```

Axis lines: 6 lines from center to outer ring vertices, white at 4% opacity, 1dp stroke.

#### Stat Polygon

| Property | Value |
|---|---|
| Max stat reference | 200 (scale all stats against this) |
| Fill | Radial gradient: center AccentBlue at 25% opacity → edge AccentPurple at 8% opacity |
| Stroke | AccentBlueSoft (#6BABFF) at 60% opacity, 2dp width |
| Vertices | 6 points, one per stat, distance from center = `(statValue / 200) * 155dp` |

#### Stat Dots and Labels

Each of the 6 axes has a colored dot at the stat vertex and a label outside the hex.

| Stat | Color | Label Position |
|---|---|---|
| HP | #3CDC6E (green) | Top center |
| Atk | #FF5252 (red) | Top-right |
| Def | #FFD93D (yellow) | Bottom-right |
| SpA | #6BABFF (blue) | Bottom center (mirrored, actually bottom-left in hex) |
| SpD | #A78BFA (purple) | Bottom-left |
| Spe | #FF6B9D (pink) | Top-left |

Dot: 4dp radius circle, solid stat color fill.

Labels at radius 180dp from center (outside the hex):
- Stat abbreviation: stat color, Nunito 10dp bold (canvas: 20px at 2x)
- Stat value: TextPrimary, Nunito 12dp bold (canvas: 24px at 2x), 12dp below abbreviation

### Column 3: Moves (~35% width)

| Property | Value |
|---|---|
| Padding | 20dp top, 28dp right, 20dp bottom, 12dp left |
| Vertical alignment | Center |

#### "Moves" Section Label
| Property | Value |
|---|---|
| Font | Nunito 9dp, weight 700, uppercase, 1.2px tracking |
| Color | TextDim |
| Margin bottom | 8dp |

#### Move Row (×4)

```
┌────────────────────────────────────┐
│  ●  Move Name              PP     │
│     Category                      │
└────────────────────────────────────┘
```

| Property | Value |
|---|---|
| Background | White at 2% opacity |
| Border | 1dp, white at 3% opacity |
| Corner radius | 10dp |
| Padding | 8dp vertical, 12dp horizontal |
| Margin bottom | 5dp |
| Type dot | 8dp circle, solid type color, left-aligned |
| Move name | Nunito 13dp, weight 700, TextPrimary |
| Category (Phys/Spec/Stat) | Nunito 10dp, weight 600, TextDim |
| PP text | Nunito 11dp, weight 700, TextSecondary, right-aligned |

#### Ability/Item Row

Below the 4 move rows, separated by a 1dp border-top (white 4% opacity) with 10dp margin-top and 8dp padding-top.

Two columns side by side with 16dp gap:

| Element | Font | Size | Color |
|---|---|---|---|
| Label ("Ability", "Item") | Nunito | 8dp, weight 700, uppercase, 0.6px tracking | TextDim |
| Value | Nunito | 12dp, weight 700 | TextPrimary |

---

## 5. Type Color Map

```csharp
public static readonly Dictionary<string, SKColor> TypeColors = new()
{
    ["Normal"]   = SKColor.Parse("#A8A878"),
    ["Fire"]     = SKColor.Parse("#F08030"),
    ["Water"]    = SKColor.Parse("#6890F0"),
    ["Grass"]    = SKColor.Parse("#78C850"),
    ["Electric"] = SKColor.Parse("#F8D030"),
    ["Ice"]      = SKColor.Parse("#98D8D8"),
    ["Fighting"] = SKColor.Parse("#C03028"),
    ["Poison"]   = SKColor.Parse("#A040A0"),
    ["Ground"]   = SKColor.Parse("#E0C068"),
    ["Flying"]   = SKColor.Parse("#A890F0"),
    ["Psychic"]  = SKColor.Parse("#F85888"),
    ["Bug"]      = SKColor.Parse("#A8B820"),
    ["Rock"]     = SKColor.Parse("#B8A038"),
    ["Ghost"]    = SKColor.Parse("#705898"),
    ["Dark"]     = SKColor.Parse("#705848"),
    ["Dragon"]   = SKColor.Parse("#7038F8"),
    ["Steel"]    = SKColor.Parse("#B8B8D0"),
    ["Fairy"]    = SKColor.Parse("#EE99AC"),
};
```

---

## 6. Bottom Screen — Box Header (MAUI XAML)

```xml
<Grid ColumnDefinitions="Auto,*,Auto" Padding="16,12,16,8">
  <!-- L button -->
  <Border Grid.Column="0" StrokeShape="RoundRectangle 10"
          Background="#131B35" Stroke="#0D0D0D0D" StrokeThickness="1"
          WidthRequest="40" HeightRequest="36">
    <Image Source="chevron_left.png" WidthRequest="16" HeightRequest="16" />
  </Border>

  <!-- Box title -->
  <VerticalStackLayout Grid.Column="1" HorizontalOptions="Center" Spacing="1">
    <Label Text="BOX 2" FontFamily="QuicksandExtraBold" FontSize="18"
           TextColor="#EDF0FF" HorizontalTextAlignment="Center" />
    <Label Text="L / R" FontFamily="Nunito" FontSize="9" FontWeight="600"
           TextColor="#283456" HorizontalTextAlignment="Center" />
  </VerticalStackLayout>

  <!-- R button -->
  <Border Grid.Column="2" StrokeShape="RoundRectangle 10"
          Background="#131B35" Stroke="#0D0D0D0D" StrokeThickness="1"
          WidthRequest="40" HeightRequest="36">
    <Image Source="chevron_right.png" WidthRequest="16" HeightRequest="16" />
  </Border>
</Grid>
```

L/R bumper presses trigger box switching. The box name label updates, and the grid content reloads.

---

## 7. Bottom Screen — Box Grid (SkiaSharp)

**This is the most performance-critical component.** The entire 6×5 grid, all sprites, cursor effects, and move-mode visuals are drawn on a single `SKCanvasView`.

### Grid Layout Calculation

```csharp
// Given canvas dimensions
float canvasW = info.Width;
float canvasH = info.Height;

// Grid params
const int cols = 6, rows = 5;
const float gap = 6f;   // dp, multiply by density
const float slotRadius = 10f;
float padX = 14f, padY = 4f; // dp

// Calculate slot size to fit
float availW = canvasW - (padX * 2) - (gap * (cols - 1));
float availH = canvasH - (padY * 2) - (gap * (rows - 1));
float slotSize = MathF.Min(availW / cols, availH / rows);

// Center the grid
float gridW = slotSize * cols + gap * (cols - 1);
float gridH = slotSize * rows + gap * (rows - 1);
float offsetX = (canvasW - gridW) / 2f;
float offsetY = (canvasH - gridH) / 2f;
```

### Slot Rendering

For each slot at grid position (col, row):

```csharp
float x = offsetX + col * (slotSize + gap);
float y = offsetY + row * (slotSize + gap);
var rect = new SKRect(x, y, x + slotSize, y + slotSize);
```

**Empty slot:**
| Property | Value |
|---|---|
| Fill | BgSlot (#0E1529) |
| Border | 1.5dp, white at 3% opacity |
| Corner radius | 10dp |

**Filled slot:**
| Property | Value |
|---|---|
| Fill | BgSlotFilled (#111C33) |
| Border | 1.5dp, white at 3% opacity |
| Corner radius | 10dp |
| Sprite | Centered, 70% of slot size, nearest-neighbor filtering |
| Legality dot | 6dp circle at top-right (4dp inset), green (#3CDC6E) or red (#FF5252), with 4dp glow shadow |

### Cursor System (the star of the show)

The cursor is drawn as three composited layers on top of the slot, all animated on a shared oscillating timer.

**Timer setup:**
```csharp
private readonly Stopwatch _cursorTimer = Stopwatch.StartNew();

// In the draw loop:
float t = (float)(_cursorTimer.Elapsed.TotalMilliseconds % 1800) / 1800f; // 0→1 over 1.8s
float pulse = 0.5f + 0.5f * MathF.Sin(t * MathF.PI * 2); // 0→1→0 sinusoidal
```

#### Normal Cursor (Blue)

**Layer 1 — Slot border override:**
| Property | Value |
|---|---|
| Stroke color | CursorBlueStroke (#5CA0FF) |
| Stroke width | 1.5dp |
| Fill | CursorBlueBg (AccentBlue at 12% opacity) |

**Layer 2 — Outer glow ring:**
| Property | Value |
|---|---|
| Inset | -4dp from slot rect (expand outward) |
| Corner radius | 13dp |
| Stroke | AccentBlueSoft at 35% opacity, 2dp |
| Animated | Opacity: `0.5 + 0.5 * pulse`, Scale: `1.0 + 0.02 * pulse` (from center of slot) |

**Layer 3 — Inner glow (box shadow):**
| Property | Value |
|---|---|
| Inset | -1dp from slot rect |
| Corner radius | 11dp |
| Effect | `SKImageFilter.CreateBlur(6, 6)` on a rect filled with AccentBlue at 30% opacity |
| Additional | Second blur at (12, 12) sigma with AccentBlue at 12% opacity (wider softer glow) |
| Animated | Opacity: `0.6 + 0.4 * pulse` |

**Sprite scale-up:** When cursor is on a filled slot, draw the sprite at 108% scale (scale from center).

```csharp
// Example composite cursor draw
void DrawCursor(SKCanvas canvas, SKRect slotRect, float pulse)
{
    // Layer 1: Fill + border
    using var fillPaint = new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = new SKColor(59, 139, 255, 31) // 12% opacity
    };
    canvas.DrawRoundRect(slotRect, 10, 10, fillPaint);

    using var borderPaint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = SKColor.Parse("#5CA0FF"),
        StrokeWidth = 1.5f * density,
        IsAntialias = true
    };
    canvas.DrawRoundRect(slotRect, 10, 10, borderPaint);

    // Layer 2: Outer glow ring
    float outerExpand = 4f * density;
    var outerRect = new SKRect(
        slotRect.Left - outerExpand, slotRect.Top - outerExpand,
        slotRect.Right + outerExpand, slotRect.Bottom + outerExpand);
    float outerScale = 1f + 0.02f * pulse;
    canvas.Save();
    canvas.Scale(outerScale, outerScale,
        slotRect.MidX, slotRect.MidY);

    using var outerPaint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = new SKColor(92, 160, 255, (byte)(89 + 77 * pulse)), // 35-65% range
        StrokeWidth = 2f * density,
        IsAntialias = true
    };
    canvas.DrawRoundRect(outerRect, 13, 13, outerPaint);
    canvas.Restore();

    // Layer 3: Inner blur glow
    float glowExpand = 1f * density;
    var glowRect = new SKRect(
        slotRect.Left - glowExpand, slotRect.Top - glowExpand,
        slotRect.Right + glowExpand, slotRect.Bottom + glowExpand);

    using var glowPaint = new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = new SKColor(59, 139, 255, (byte)(46 + 31 * pulse)), // varying opacity
        ImageFilter = SKImageFilter.CreateBlur(6 * density, 6 * density),
        IsAntialias = true
    };
    canvas.DrawRoundRect(glowRect, 11, 11, glowPaint);
}
```

#### Selected Cursor (Gold)

Same three-layer structure, different colors:

| Layer | Color |
|---|---|
| Fill | CursorGoldBg: `rgba(200,170,80, 0.15)` |
| Border stroke | CursorGoldStroke: `#C8AA50` |
| Outer ring | `rgba(200,170,80, 0.3)` |
| Inner glow | `rgba(200,170,80, 0.2)` blur 7dp |

No pulse animation — selected state is static. Only drawn when `selectedSlot == currentIndex && !moveMode`.

#### Move Mode Cursor (Green)

Same three-layer structure, green palette, faster pulse:

| Property | Value |
|---|---|
| Pulse period | 1400ms (faster than normal 1800ms — creates urgency) |
| Fill | `rgba(60,220,110, 0.1)` |
| Border stroke | CursorGreenStroke: `#3CDC6E` |
| Outer ring | `rgba(60,220,110, 0.35)`, pulsing |
| Inner glow | `rgba(60,220,110, 0.25)` blur 7dp, pulsing |

#### Move Source Ghost

The slot where the Pokémon was grabbed from:
- Sprite drawn at 30% opacity
- No cursor highlight
- The class `move-source` in the mockup

---

## 8. Bottom Screen — Info Bar (MAUI XAML)

```xml
<Grid ColumnDefinitions="*,Auto" Padding="16,8,16,12">
  <!-- Slot info -->
  <HorizontalStackLayout Spacing="4">
    <Label Text="#101" FontFamily="Nunito" FontSize="12" FontWeight="600"
           TextColor="#3D4A6E" />
    <Label Text="Electrode · Lv.38" FontFamily="NunitoBold" FontSize="12"
           TextColor="#8892B5" />
  </HorizontalStackLayout>

  <!-- Gamepad hints -->
  <HorizontalStackLayout Grid.Column="1" Spacing="12">
    <!-- Each hint: [glyph] label -->
  </HorizontalStackLayout>
</Grid>
```

Info bar updates are driven by the cursor position. When cursor moves to an empty slot, display "Empty slot" in TextSecondary.

---

## 9. State Machine

The Box Viewer has four distinct interaction states. The cursor appearance and gamepad behavior change per state.

```
┌──────────────┐     A (on filled)     ┌──────────────┐
│              │ ───────────────────▶   │              │
│   BROWSING   │                        │   SELECTED   │
│  (blue cursor)│ ◀───────────────────  │ (gold cursor)│
│              │     B or A (toggle)    │              │
└──────┬───────┘                        └──────────────┘
       │
       │ Y (on filled)
       ▼
┌──────────────┐     A (on target)     ┌──────────────┐
│              │ ───────────────────▶   │              │
│  MOVE MODE   │                        │   DROPPED    │
│(green cursor)│                        │ (swap done,  │
│              │ ◀── B (cancel) ──────  │  → BROWSING) │
└──────────────┘                        └──────────────┘
```

| State | Cursor Color | Top Screen | Info Bar | Y Button |
|---|---|---|---|---|
| BROWSING | Blue, pulsing | Trainer card (idle) | Shows hovered Pokémon info | "Grab" |
| SELECTED | Gold, static (on selected) + Blue pulsing (cursor if moved away) | Pokémon detail (3-column) | Shows selected Pokémon info | "Grab" (grabs selected) |
| MOVE MODE | Green, pulsing + ghost at source | Trainer card (idle) | Shows "Moving [Name]..." | "Cancel" |
| DROPPED | → returns to BROWSING | Trainer card | Updated slot info | "Grab" |

---

## 10. Animation Summary

| Animation | Duration | Easing | Trigger |
|---|---|---|---|
| Cursor glow pulse | 1800ms | Sinusoidal (ease-in-out loop) | Continuous while browsing |
| Move mode cursor pulse | 1400ms | Sinusoidal (faster) | Continuous while in move mode |
| Cursor sprite scale-up | 150ms | ease-out | Cursor enters filled slot |
| Top screen idle → detail | 400ms | ease | A pressed on filled slot |
| Top screen detail → idle | 400ms | ease | B pressed |
| Stat radar polygon draw | 300ms | ease-out | Detail appears (animate vertices from center outward) |
| Box switch content swap | 200ms | ease | L/R bumper press |

### Radar Polygon Entrance Animation

When the detail view appears, the stat radar polygon should animate from the center outward:

```csharp
// animProgress goes from 0 → 1 over 300ms
float animatedRadius = baseRadius * EaseOut(animProgress);

// EaseOut: t * (2 - t)
float EaseOut(float t) => t * (2f - t);
```

Each vertex position is lerped from center `(cx, cy)` to its final position using `animProgress`.

---

## 11. Performance Notes

The bottom screen grid redraws on every cursor move and continuously for the pulse animation. Optimization is critical:

- **Invalidate strategically:** Only call `InvalidateSurface()` when cursor moves OR on the pulse timer tick (~60fps). Don't invalidate on every frame if cursor is stationary — only when the pulse timer advances.
- **Cache sprite bitmaps:** Decode all 30 slot sprites once when the box loads. Store as `SKBitmap[]`. Re-decode only on box switch.
- **Pre-compute grid positions:** Calculate all 30 slot rects once on layout, store in array. Don't recalculate in the draw loop.
- **Clip drawing:** Only redraw the cursor's current and previous slot regions if possible (dirty rect optimization). Though for simplicity, full-canvas redraw at 60fps should be fine on Snapdragon 8 Gen 2.
- **Sprite filtering:** Use `SKFilterQuality.None` for pixel art sprites (nearest-neighbor). Set `paint.FilterQuality = SKFilterQuality.None`.

---

## 12. Gamepad Input Mapping

| Button | Action |
|---|---|
| D-pad | Move cursor (4-directional, wraps within grid) |
| A | Select (toggle detail) / Drop (in move mode) |
| B | Deselect / Cancel move / Go back to Home |
| Y | Grab Pokémon (enter move mode) / Cancel move mode |
| X | Quick search (future) |
| L bumper | Previous box |
| R bumper | Next box |
| Start | Open save/export overlay menu |

D-pad cursor wrapping behavior:
- Left from column 0 → stays at column 0 (no wrap)
- Right from column 5 → stays at column 5
- Up from row 0 → focus jumps to box header (L/R buttons)
- Down from row 4 → focus jumps to info bar area (or no-op)

---

## 13. Quick Reference for Claude Code

When prompting Claude Code with this doc:

1. This markdown file (Box Viewer implementation spec)
2. The Home screen bridge doc (for shared color system and dual-screen abstraction)
3. The HTML mockup file (`pkhex-boxviewer.html`)
4. The original design brief

**Tell Claude Code:**
> "Build the PKHeX Box Viewer screen for .NET MAUI targeting Android. The top screen is a full SkiaSharp canvas (1920×1080 landscape) with two states: an idle trainer card and a three-column Pokémon detail view (sprite, stat radar, moves). The bottom screen has a MAUI XAML header and info bar with a SkiaSharp canvas for the 6×5 box grid. The cursor system is the highest priority — it needs three modes (blue browse, gold selected, green move) with a pulsing glow effect. Use the Box Viewer implementation bridge doc for exact colors, layout, and animation specs. Use the Home screen bridge doc for the shared Theme class and dual-screen abstraction. Build single-screen fallback first."
