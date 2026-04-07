# PKHeX Android — Implementation Bridge Doc

## Purpose

This document translates the HTML design mockup (`pkhex-dualscreen-home-v2.html`) into concrete .NET MAUI and SkiaSharp implementation guidance. Give this file + the HTML mockup to Claude Code together as the source of truth for building each screen.

---

## 1. Hardware Target

| Property | Top Screen | Bottom Screen |
|---|---|---|
| Size | 6" AMOLED | 3.92" AMOLED |
| Resolution | 1920×1080 (landscape, 16:9) | 1080×1240 (near-square, ~7:8) |
| Refresh | 120Hz | 60Hz |
| Touch | No | Yes |
| Role | Display surface (sprites, stats, branding) | Interaction surface (lists, grids, buttons) |

**Dual-screen routing is TBD** — the AYN Thor uses Android 13 with a custom AOSP launcher. The second screen is likely accessible via Android's `Presentation` API (`DisplayManager.getDisplays()` → create a `Presentation` for the secondary display). Wrap all top-screen rendering in an abstraction layer so it can be swapped between:
- `Presentation` with a `SKCanvasView` (Thor dual-screen mode)
- Top half of a single-screen layout (regular Android fallback)

---

## 2. Design System — Color Tokens

Use these as named constants in a static `Theme` class. Every color below is used in the mockup.

### Core Palette

```csharp
public static class Theme
{
    // Backgrounds
    public static readonly SKColor BgDeep       = SKColor.Parse("#070C1A");
    public static readonly SKColor BgMid        = SKColor.Parse("#0C1228");
    public static readonly SKColor BgCard       = SKColor.Parse("#131B35");
    public static readonly SKColor BgCardHover  = SKColor.Parse("#182242");
    public static readonly SKColor BgCardActive = SKColor.Parse("#1C2850");

    // Accents
    public static readonly SKColor AccentBlue     = SKColor.Parse("#3B8BFF");
    public static readonly SKColor AccentBlueSoft = SKColor.Parse("#6BABFF");
    public static readonly SKColor AccentTeal     = SKColor.Parse("#36D1C4");
    public static readonly SKColor AccentGreen    = SKColor.Parse("#34D990");
    public static readonly SKColor AccentPink     = SKColor.Parse("#FF6B9D");
    public static readonly SKColor AccentOrange   = SKColor.Parse("#FF9F43");
    public static readonly SKColor AccentPurple   = SKColor.Parse("#A78BFA");
    public static readonly SKColor AccentRed      = SKColor.Parse("#FF5252");
    public static readonly SKColor AccentYellow   = SKColor.Parse("#FFD93D");

    // Text
    public static readonly SKColor TextPrimary   = SKColor.Parse("#EDF0FF");
    public static readonly SKColor TextSecondary = SKColor.Parse("#8892B5");
    public static readonly SKColor TextDim       = SKColor.Parse("#3D4A6E");
    public static readonly SKColor TextMuted     = SKColor.Parse("#283456");

    // Borders
    public static readonly SKColor BorderSubtle = new SKColor(255, 255, 255, 13);  // ~5% white
    public static readonly SKColor BorderFocus  = new SKColor(59, 139, 255, 153);  // ~60% accent blue

    // Corner radii (in dp, scale for density)
    public const float RadiusSm = 8f;
    public const float RadiusMd = 14f;
    public const float RadiusLg = 20f;
}
```

### Per-Game Accent Colors

Each save file has a game-specific accent color used for:
- The game icon badge gradient
- The focused card's left accent stripe
- The top-screen ambient glow

```csharp
public static class GameColors
{
    public static readonly Dictionary<string, (SKColor Dark, SKColor Light)> Map = new()
    {
        ["Emerald"]    = (SKColor.Parse("#2D8B57"), SKColor.Parse("#50C878")),
        ["Y"]          = (SKColor.Parse("#C8384A"), SKColor.Parse("#E85D75")),
        ["UltraMoon"]  = (SKColor.Parse("#D67028"), SKColor.Parse("#FF8C42")),
        ["Scarlet"]    = (SKColor.Parse("#B82E2E"), SKColor.Parse("#E83F3F")),
        ["Violet"]     = (SKColor.Parse("#7B52A8"), SKColor.Parse("#9B72CF")),
        ["Gold"]       = (SKColor.Parse("#B88D1C"), SKColor.Parse("#DAA520")),
        ["Crystal"]    = (SKColor.Parse("#5AA0C0"), SKColor.Parse("#7EC8E3")),
        ["Black"]      = (SKColor.Parse("#3A3A4A"), SKColor.Parse("#5A5A6A")),
    };

    // Badge gradient: linear 135deg from Dark to Light
    // Accent stripe: solid Light color
    // Hero glow: Light color at 10-12% opacity, gaussian blur radius ~60dp
}
```

---

## 3. Typography

The mockup uses **Nunito** (body) and **Quicksand** (display/headers). Both are Google Fonts, both are free. Bundle the TTF files in the MAUI app's `Resources/Fonts/` folder.

```csharp
// Register in MauiProgram.cs
builder.ConfigureFonts(fonts =>
{
    fonts.AddFont("Nunito-Regular.ttf", "Nunito");
    fonts.AddFont("Nunito-Bold.ttf", "NunitoBold");
    fonts.AddFont("Nunito-ExtraBold.ttf", "NunitoExtraBold");
    fonts.AddFont("Quicksand-Bold.ttf", "Quicksand");
    fonts.AddFont("Quicksand-ExtraBold.ttf", "QuicksandExtraBold");
});
```

For SkiaSharp text rendering, load with `SKTypeface.FromFile()` or embed as resources.

### Type Scale

| Element | Font | Weight | Size (dp) | Color |
|---|---|---|---|---|
| App title "PKHeX" | Quicksand | 800 | 32 | Gradient (white → AccentBlueSoft → AccentTeal) |
| Subtitle "Save Manager" | Quicksand | 700 | 11 | TextSecondary |
| Version label | Nunito | 600 | 10 | TextDim |
| Section label "Save Files" | Quicksand | 700 | 12 | TextDim, uppercase, 1.5px letter-spacing |
| Card trainer name | Nunito | 800 | 16 | TextPrimary |
| Card game version | Nunito | 600 | 12 | TextSecondary |
| Card meta (TID, boxes, time) | Nunito | 600 | 10.5 | TextDim |
| Primary button | Nunito | 800 | 15 | White |
| Secondary button label | Nunito | 700 | 10 | TextSecondary |
| Gamepad hint text | Nunito | 600 | 10 | TextDim |
| Preview trainer (top screen) | Nunito | 900 | 24 | TextPrimary |
| Preview game (top screen) | Nunito | 600 | 13 | TextSecondary |
| Preview meta label | Nunito | 700 | 9 | TextDim, uppercase |
| Preview meta value | Nunito | 800 | 14 | TextPrimary |

---

## 4. Screen Architecture

### Home Screen — Top Screen (1920×1080)

**Rendering approach:** Single `SKCanvasView` filling the entire Presentation window. Everything is drawn in the SkiaSharp paint loop.

**Layout (horizontal, left-to-right):**

```
┌──────────────────────────────────────────────────────┐
│  [Pokéball]                 │                         │
│  PKHeX           divider    │  [Game Badge]  RYAN     │
│  SAVE MANAGER    line       │  Pokémon Emerald · G3   │
│  v25.03          (1px,      │  TID    Boxes  Playtime │
│                   6% white) │  21121  14     999:59   │
│                             │                [ACTIVE] │
│  ← ~30% width →            │  ← ~65% width →         │
└──────────────────────────────────────────────────────┘
```

**Background layers (drawn bottom-to-top):**
1. Solid fill `BgDeep`
2. Radial gradient glow — 3 overlapping ellipses with very low opacity blues/purples/teals. Use `SKShader.CreateRadialGradient`. Animate hue-rotation slowly (shift gradient center positions over ~20s cycle).
3. Game-colored glow — centered radial gradient using the focused save's game color at 10-12% opacity, blur radius ~60dp. Transition on save change (lerp color over 0.6s).
4. Grid overlay — draw thin 1dp lines every 36dp at ~2.5% white opacity. Mask with radial gradient (full opacity center, transparent at edges).
5. Floating particles — 15-20 small circles (1-3dp), slowly drifting upward. Each has random hue (220° or 260°), random opacity (0.1-0.45), random speed (8-20s full traverse). Use a list of particle structs updated each frame.

**Branding (left column):**
- Pokéball SVG: translate the SVG paths to `SKPath` objects. Rotate slowly (one full rotation per ~28s). Apply `SKImageFilter.CreateDropShadow` with AccentBlue glow.
- Title text: for the gradient text effect, draw text with `SKShader.CreateLinearGradient` (white → AccentBlueSoft → AccentTeal at 135°) applied to the paint's shader.

**Save preview (right column):**
- Slides in from right (translateX 15dp → 0 over 0.4s, ease-out) when focus changes.
- Game badge: 60×60dp rounded rect (16dp radius) with linear gradient fill. Draw white highlight overlay on top half at 18% opacity.
- Meta items: three columns (TID, Boxes, Playtime) with uppercase label above and bold value below.
- Active pill: rounded capsule (20dp radius) with AccentGreen at 10% fill, 25% border. Only shown when the focused save is the active one.

### Home Screen — Bottom Screen (1080×1240)

**Rendering approach:** MAUI XAML layout. No SkiaSharp needed here — this is standard scrollable list + buttons.

```xml
<!-- Pseudo-XAML structure -->
<Grid RowDefinitions="Auto,*,Auto" BackgroundColor="#070C1A">

  <!-- Row 0: Header -->
  <Grid Padding="18,14,18,8">
    <Label Text="SAVE FILES" Style="{StaticResource SectionLabel}" />
    <Frame HorizontalOptions="End" Style="{StaticResource CountBadge}">
      <Label Text="5 saves" />
    </Frame>
  </Grid>

  <!-- Row 1: Scrollable save list -->
  <CollectionView Grid.Row="1" ItemsSource="{Binding Saves}" Padding="14,0">
    <CollectionView.ItemTemplate>
      <DataTemplate>
        <!-- Save card: see component spec below -->
      </DataTemplate>
    </CollectionView.ItemTemplate>
  </CollectionView>

  <!-- Row 2: Action bar -->
  <VerticalStackLayout Grid.Row="2" Padding="14,10,14,14" Spacing="10">
    <!-- Primary button -->
    <!-- Secondary 4-column grid -->
    <!-- Gamepad hints row -->
  </VerticalStackLayout>

</Grid>
```

**Note on CollectionView vs ScrollView:** Use `CollectionView` for the save list — it virtualizes and handles gamepad focus natively. Set `SelectionMode="Single"` and bind `SelectedItem` for D-pad navigation.

---

## 5. Component Specs

### Save Card (Bottom Screen)

```
┌─────────────────────────────────────────┐
│ ┃  [Badge]  Trainer Name         (●)    │
│ ┃  44×44    Game · Gen N                │
│ ┃           TID · boxes · time          │
└─────────────────────────────────────────┘
```

| Property | Value |
|---|---|
| Background | BgCard (#131B35) |
| Corner radius | 14dp |
| Padding | 12dp vertical, 14dp horizontal |
| Margin bottom | 8dp |
| Border | 1.5dp transparent (default), AccentBlue (focused) |
| Focused state | BgCardActive bg, AccentBlue border, scale 1.012x, shadow (0 0 20dp AccentBlue@10%, 0 4dp 16dp black@30%) |
| Accent stripe | 3dp wide, left edge, 18%-82% height, game color, visible only when focused |
| Game badge | 48×48dp, 13dp radius, gradient fill (game dark → light at 135°), white highlight overlay |
| Active dot | 9dp circle, AccentGreen, glow shadow 10dp AccentGreen@50%, pulsing animation (opacity 1→0.4→1 over 2s) |
| Entry animation | Slide from left (10dp) + fade, 0.3s ease-out, staggered 0.05s per card |

### Game Icon Badge

Rounded rectangle with a two-stop linear gradient at 135°. The top-left corner catches a white highlight (18% opacity overlay on top 50%).

```csharp
// SkiaSharp draw example for badge
using var paint = new SKPaint();
var rect = new SKRect(x, y, x + 48, y + 48);
float radius = 13f;

// Gradient fill
paint.Shader = SKShader.CreateLinearGradient(
    new SKPoint(rect.Left, rect.Top),
    new SKPoint(rect.Right, rect.Bottom),
    new[] { gameColor.Dark, gameColor.Light },
    SKShaderTileMode.Clamp);
canvas.DrawRoundRect(rect, radius, radius, paint);

// White highlight overlay
paint.Shader = SKShader.CreateLinearGradient(
    new SKPoint(rect.Left, rect.Top),
    new SKPoint(rect.Left, rect.MidY),
    new[] { new SKColor(255,255,255,46), SKColors.Transparent },
    SKShaderTileMode.Clamp);
canvas.DrawRoundRect(rect, radius, radius, paint);
```

If using MAUI XAML instead of SkiaSharp for the bottom screen cards, this badge can be a `Border` with `Background="{LinearGradientBrush}"` containing a centered `Label` for the game code text.

### Primary Action Button

| Property | Value |
|---|---|
| Background | Linear gradient 135°: AccentBlue → #4F6BF0 |
| Corner radius | 14dp |
| Padding | 14dp vertical |
| Text | "Open Boxes", Nunito ExtraBold 15dp, white |
| Icon | Box grid SVG, 20×20dp, white stroke |
| Shadow | 0 4dp 20dp AccentBlue@30% |
| Highlight | Top 50% has white 10% overlay (subtle glass effect) |

In MAUI, use a `Button` inside a `Border` with `Background` set to a `LinearGradientBrush`. The glass highlight can be a semi-transparent `BoxView` overlaid on the top half.

### Secondary Action Tiles

4-column grid. Each tile is:

| Property | Value |
|---|---|
| Size | Equal width, ~auto height |
| Background | BgCard |
| Border | 1dp BorderSubtle |
| Corner radius | 8dp |
| Padding | 12dp top, 4dp sides, 10dp bottom |
| Icon container | 34×34dp, 9dp radius, colored background (12% of accent), centered SVG 18×18dp |
| Label | 10dp, Nunito Bold, TextSecondary |

Icon color map:
- Search: AccentPurple background, AccentPurple icon
- Gifts: AccentPink background, AccentPink icon
- Export: AccentOrange background, AccentOrange icon
- Bank: AccentGreen background, AccentGreen icon

### Gamepad Hint Glyphs

Row of hint items, each is: `[glyph box] label`

| Property | Value |
|---|---|
| Glyph box | 20×20dp, 5dp radius, white 4% fill, white 6% border |
| Glyph text | 9dp, Nunito ExtraBold, TextSecondary (or colored for A/B/X) |
| Label text | 10dp, Nunito SemiBold, TextDim |

Color-coded glyphs:
- A button: AccentGreen text
- B button: AccentPink text
- X button: AccentPurple text
- D-pad: TextSecondary text

---

## 6. Gamepad Navigation Model

The app is gamepad-first. Here's how focus should flow on the Home screen bottom:

```
D-pad Up/Down  →  Move through save card list
A button       →  Select focused save (set as active) OR activate focused action button
B button       →  Back / exit
X button       →  Quick jump to Search
Y button       →  Context menu for focused save (future)
Start          →  Settings
L/R bumpers    →  Jump between list sections (saves ↔ action bar)
```

**Focus zones (L/R to cycle between):**
1. Save list (D-pad up/down scrolls)
2. Action bar (D-pad left/right moves between tiles, up goes to primary button)

The focused element must always be visually distinct — the blue border + glow + scale is the focus indicator. The MAUI `VisualStateManager` can handle `Focused` states, but for the SkiaSharp-rendered top screen, you'll need to drive the focus visuals from the same focus state.

---

## 7. Animations

All timing values use ease-out (`cubic-bezier(0.25, 0.46, 0.45, 0.94)`) unless noted.

| Animation | Duration | Easing | Details |
|---|---|---|---|
| Card entry (on page load) | 300ms | ease-out | TranslateX -10dp → 0, Opacity 0 → 1, staggered 50ms per card |
| Card focus change | 200ms | ease-out | Border color, background color, scale (1.0 → 1.012), shadow |
| Top screen preview slide-in | 400ms | ease-out | TranslateX 15dp → 0, Opacity 0 → 1 |
| Game glow color shift | 800ms | ease | Lerp between old and new game color |
| Active dot pulse | 2000ms | ease-in-out | Opacity 1 → 0.4 → 1, Scale 1 → 0.7 → 1, infinite loop |
| Pokéball rotation | 28000ms | linear | Full 360° rotation, infinite |
| Ambient particles | 8000-22000ms | linear | TranslateY from bottom to top, opacity fade in/out at edges |
| Hero background shift | 20000ms | ease-in-out | Slow hue-rotate ~12°, alternate direction |

For SkiaSharp animations, use a `SKCanvasView` with `EnableTouchEvents = false` and invalidate on a timer (target 60fps on top screen). Use `Stopwatch` + interpolation rather than frame-counting.

For MAUI animations on the bottom screen, use `ViewExtensions` (`.TranslateTo()`, `.FadeTo()`, `.ScaleTo()`) or the `Animation` class for custom curves.

---

## 8. Dual-Screen Abstraction Layer

Since the Thor's dual-screen API behavior is unconfirmed, build an abstraction:

```csharp
public interface ISecondaryDisplay
{
    bool IsAvailable { get; }
    void SetContent(View content);    // For MAUI content
    void SetContent(SKCanvasView canvas); // For SkiaSharp content
    void Show();
    void Hide();
}

// Thor implementation (uses Android Presentation API)
public class ThorSecondaryDisplay : ISecondaryDisplay { ... }

// Fallback (renders in top half of single screen)
public class SingleScreenFallback : ISecondaryDisplay { ... }
```

Register in DI and resolve at startup based on `DisplayManager.GetDisplays().Length > 1`.

---

## 9. Asset Requirements

| Asset | Format | Usage |
|---|---|---|
| Nunito font (Regular, Bold, ExtraBold) | .ttf | Body text, buttons, labels |
| Quicksand font (Bold, ExtraBold) | .ttf | Display titles, section headers |
| Game icon images (optional) | .png, 96×96 | Save card badges (fallback to letter code) |
| Box grid SVG icon | Vector path | Primary button icon |
| Search/Gift/Export/Bank SVG icons | Vector paths | Secondary action tiles |
| Pokéball wireframe | SKPath data | Top screen branding animation |

The SVG icons in the mockup are from Lucide-style line icons. For MAUI, convert to `PathGeometry` or use `SKPath.ParseSvgPathData()` in SkiaSharp.

---

## 10. What This Doc Does NOT Cover (Yet)

These screens need their own implementation docs once the Home screen is built:

- **Box Viewer (GamePage)** — Top: animated sprite + stat radar (wide layout is perfect for side-by-side). Bottom: 6×5 box grid + header with L/R box switching.
- **Pokémon Bank (BankPage)** — Similar to box viewer but with distinct visual identity (different accent color, background treatment).
- **Settings (SettingsPage)** — Bottom screen only, top screen idles or shows contextual help.
- **Stat Editor** — Complex form, likely bottom-screen-only with top showing live preview of changes.
- **Pokémon search/filter** — Bottom screen input, top screen results grid.

---

## 11. Quick Reference for Claude Code

When prompting Claude Code with this doc, include:

1. This markdown file (implementation spec)
2. The HTML mockup file (`pkhex-dualscreen-home-v2.html`) — open it in a browser for visual reference
3. The original design brief (the `PKHeX Android — UI Design Brief` document)

**Tell Claude Code:**
> "Build the PKHeX Home screen for .NET MAUI targeting Android. The top screen is a full SkiaSharp canvas (1920×1080 landscape) and the bottom screen is MAUI XAML (1080×1240). Use the implementation bridge doc for exact colors, typography, component specs, and animation timing. Use the HTML mockup as the visual target. Dual-screen routing should be abstracted behind an ISecondaryDisplay interface — build the single-screen fallback first, Thor-specific implementation later."
