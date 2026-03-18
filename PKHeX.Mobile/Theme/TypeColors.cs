using SkiaSharp;

namespace PKHeX.Mobile.Theme;

/// <summary>
/// Pokémon type → color mapping for type badges and glow effects.
/// Values from PKHEX_BOXImplementBridge.md §5.
/// </summary>
public static class TypeColors
{
    public static readonly Dictionary<string, SKColor> Map = new()
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
}
