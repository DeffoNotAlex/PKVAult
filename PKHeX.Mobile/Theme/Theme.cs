using SkiaSharp;

namespace PKHeX.Mobile.Theme;

/// <summary>
/// Shared design-system color tokens and corner radii.
/// Every value comes from the UI mockup (PKHEX_HomeImplementationBridge.md §2).
/// </summary>
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
    public static readonly SKColor BorderSubtle = new(255, 255, 255, 13);   // ~5% white
    public static readonly SKColor BorderFocus  = new(59, 139, 255, 153);   // ~60% accent blue

    // Corner radii (dp — scale for density)
    public const float RadiusSm = 8f;
    public const float RadiusMd = 14f;
    public const float RadiusLg = 20f;
}
