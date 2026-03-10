using System.Text;
using PKHeX.Core;
using static PKHeX.Core.Species;

namespace PKHeX.Drawing.Mobile.Sprites;

/// <summary>
/// Logic for generating sprite resource key names compatible with PKHeX.Drawing.PokeSprite file naming.
/// </summary>
public static class SpriteName
{
    /// <summary>When false, shiny suffix is never appended — normal sprite is used instead.</summary>
    public static bool AllowShinySprite { get; set; } = true;

    private const char Separator = '_';
    private const char Cosplay = 'c';
    private const char Shiny = 's';
    private const char GGStarter = 'p';

    public static string GetResourceStringBall(byte ball) => $"_ball{ball}";

    public static string GetResourceStringSprite(ushort species, byte form, byte gender, uint formarg,
        EntityContext context = EntityContext.None, bool shiny = false)
    {
        if (SpeciesDefaultFormSprite.Contains(species))
            form = 0;

        if (species == (ushort)Xerneas && context == EntityContext.Gen9a)
            form = 1;

        var sb = new StringBuilder(12);
        sb.Append(Separator).Append(species);

        if (form != 0)
        {
            sb.Append(Separator).Append(form);

            if (species == (ushort)Pikachu)
            {
                if (context == EntityContext.Gen6)
                    sb.Append(Cosplay);
                else if (form == 8)
                    sb.Append(GGStarter);
            }
            else if (species == (ushort)Eevee)
            {
                if (form == 1)
                    sb.Append(GGStarter);
            }
        }

        if (gender == 1 && SpeciesGenderedSprite.Contains(species))
            sb.Append('f');

        if (species == (ushort)Alcremie)
        {
            if (form == 0)
                sb.Append(Separator).Append(form);
            sb.Append(Separator).Append(formarg);
        }

        if (shiny && AllowShinySprite)
            sb.Append(Shiny);

        return sb.ToString();
    }

    private static ReadOnlySpan<ushort> SpeciesDefaultFormSprite =>
    [
        (ushort)Mothim,
        (ushort)Scatterbug,
        (ushort)Spewpa,
        (ushort)Rockruff,
        (ushort)Mimikyu,
        (ushort)Sinistea,
        (ushort)Polteageist,
        (ushort)Urshifu,
        (ushort)Dudunsparce,
        (ushort)Poltchageist,
        (ushort)Sinistcha,
    ];

    private static ReadOnlySpan<ushort> SpeciesGenderedSprite =>
    [
        (ushort)Hippopotas,
        (ushort)Hippowdon,
        (ushort)Unfezant,
        (ushort)Frillish,
        (ushort)Jellicent,
        (ushort)Pyroar,
    ];
}
