using PKHeX.Core;
using SkiaSharp;

namespace PKHeX.Mobile.Theme;

/// <summary>
/// Per-game accent colors for badge gradients, accent stripes, and ambient glow.
/// Badge gradient: linear 135° from Dark → Light.
/// Accent stripe: solid Light color.
/// Hero glow: Light color at 10-12% opacity, gaussian blur ~60dp.
/// </summary>
public static class GameColors
{
    /// <summary>
    /// Optional third color used for duotone Moiré on games with a strong secondary accent
    /// (e.g. Black 2 blue / White 2 orange). Null for all other games.
    /// </summary>
    public static readonly Dictionary<GameVersion, SKColor> AccentMap = new()
    {
        [GameVersion.B2] = SKColor.Parse("#3A80FF"), // Kyurem Black / Zekrom blue
        [GameVersion.W2] = SKColor.Parse("#FF6B30"), // Kyurem White / Reshiram orange
    };

    public static readonly Dictionary<GameVersion, (SKColor Dark, SKColor Light)> Map = new()
    {
        // Gen 1
        [GameVersion.RD] = (SKColor.Parse("#B82E2E"), SKColor.Parse("#E83F3F")),
        [GameVersion.GN] = (SKColor.Parse("#2D8B57"), SKColor.Parse("#50C878")),
        [GameVersion.BU] = (SKColor.Parse("#2855A0"), SKColor.Parse("#4A7FD0")),
        [GameVersion.YW] = (SKColor.Parse("#C8A008"), SKColor.Parse("#E8C820")),

        // Gen 2
        [GameVersion.GD] = (SKColor.Parse("#B88D1C"), SKColor.Parse("#DAA520")),
        [GameVersion.SI] = (SKColor.Parse("#8C8CA0"), SKColor.Parse("#B0B0C8")),
        [GameVersion.C]  = (SKColor.Parse("#5AA0C0"), SKColor.Parse("#7EC8E3")),

        // Gen 3
        [GameVersion.R]  = (SKColor.Parse("#B82E2E"), SKColor.Parse("#E83F3F")),
        [GameVersion.S]  = (SKColor.Parse("#5070C0"), SKColor.Parse("#7090E0")),
        [GameVersion.E]  = (SKColor.Parse("#2D8B57"), SKColor.Parse("#50C878")),
        [GameVersion.FR] = (SKColor.Parse("#D05020"), SKColor.Parse("#F07038")),
        [GameVersion.LG] = (SKColor.Parse("#58A028"), SKColor.Parse("#78C038")),

        // Gen 4
        [GameVersion.D]  = (SKColor.Parse("#4858A0"), SKColor.Parse("#6878C0")),
        [GameVersion.P]  = (SKColor.Parse("#A868B8"), SKColor.Parse("#C888D8")),
        [GameVersion.Pt] = (SKColor.Parse("#888898"), SKColor.Parse("#A8A8B8")),
        [GameVersion.HG] = (SKColor.Parse("#B88D1C"), SKColor.Parse("#DAA520")),
        [GameVersion.SS] = (SKColor.Parse("#8C8CA0"), SKColor.Parse("#B0B0C8")),

        // Gen 5
        [GameVersion.B]  = (SKColor.Parse("#3A3A4A"), SKColor.Parse("#5A5A6A")),
        [GameVersion.W]  = (SKColor.Parse("#C0C0D0"), SKColor.Parse("#E0E0F0")),
        [GameVersion.B2] = (SKColor.Parse("#3A3A4A"), SKColor.Parse("#5A5A6A")),
        [GameVersion.W2] = (SKColor.Parse("#C0C0D0"), SKColor.Parse("#E0E0F0")),

        // Gen 6
        [GameVersion.X]  = (SKColor.Parse("#4868A8"), SKColor.Parse("#6888C8")),
        [GameVersion.Y]  = (SKColor.Parse("#C8384A"), SKColor.Parse("#E85D75")),
        [GameVersion.OR] = (SKColor.Parse("#C85020"), SKColor.Parse("#E87038")),
        [GameVersion.AS] = (SKColor.Parse("#3860A0"), SKColor.Parse("#5880C0")),

        // Gen 7
        [GameVersion.SN] = (SKColor.Parse("#D08020"), SKColor.Parse("#F0A030")),
        [GameVersion.MN] = (SKColor.Parse("#5050A8"), SKColor.Parse("#7070C8")),
        [GameVersion.US] = (SKColor.Parse("#D67028"), SKColor.Parse("#FF8C42")),
        [GameVersion.UM] = (SKColor.Parse("#5040A0"), SKColor.Parse("#7060C0")),

        // Let's Go
        [GameVersion.GP] = (SKColor.Parse("#D0A020"), SKColor.Parse("#F0C030")),
        [GameVersion.GE] = (SKColor.Parse("#9B6830"), SKColor.Parse("#C08848")),

        // Gen 8
        [GameVersion.SW] = (SKColor.Parse("#2878B8"), SKColor.Parse("#4898D8")),
        [GameVersion.SH] = (SKColor.Parse("#C03068"), SKColor.Parse("#E05088")),
        [GameVersion.BD] = (SKColor.Parse("#4858A0"), SKColor.Parse("#6878C0")),
        [GameVersion.SP] = (SKColor.Parse("#A868B8"), SKColor.Parse("#C888D8")),
        [GameVersion.PLA] = (SKColor.Parse("#5A7848"), SKColor.Parse("#7A9868")),

        // Gen 9
        [GameVersion.SL] = (SKColor.Parse("#B82E2E"), SKColor.Parse("#E83F3F")),
        [GameVersion.VL] = (SKColor.Parse("#7B52A8"), SKColor.Parse("#9B72CF")),
    };

    /// <summary>
    /// Gets the game color pair for a version, falling back to a neutral blue if unmapped.
    /// </summary>
    public static (SKColor Dark, SKColor Light) Get(GameVersion version)
    {
        if (Map.TryGetValue(version, out var colors))
            return colors;
        return (SKColor.Parse("#3A5080"), SKColor.Parse("#5A70A0"));
    }

    /// <summary>
    /// Returns the duotone accent color for B2/W2, or null for all other games.
    /// </summary>
    public static SKColor? GetAccent(GameVersion version)
        => AccentMap.TryGetValue(version, out var c) ? c : null;
}
