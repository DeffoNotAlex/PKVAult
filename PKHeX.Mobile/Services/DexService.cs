using System.Text.Json;
using PKHeX.Core;

namespace PKHeX.Mobile.Services;

public class DexService
{
    public const int MaxSpecies = 1025;
    private const string FileName = "dex_unlocked.json";

    private static string FilePath =>
        Path.Combine(FileSystem.AppDataDirectory, FileName);

    public static readonly (ushort Start, ushort End, string Name)[] Generations =
    [
        (  1, 151, "Kanto"),
        (152, 251, "Johto"),
        (252, 386, "Hoenn"),
        (387, 493, "Sinnoh"),
        (494, 649, "Unova"),
        (650, 721, "Kalos"),
        (722, 809, "Alola"),
        (810, 905, "Galar"),
        (906, 1025, "Paldea"),
    ];

    private HashSet<ushort> _unlocked;

    public DexService() => _unlocked = Load();

    public bool IsUnlocked(ushort species) => _unlocked.Contains(species);
    public int UnlockedCount => _unlocked.Count;

    /// <summary>Scans party and all boxes from a save. Returns newly unlocked count.</summary>
    public int ScanSave(SaveFile sav)
    {
        int before = _unlocked.Count;

        foreach (var pk in sav.PartyData)
            if (pk.Species > 0 && pk.Species <= MaxSpecies)
                _unlocked.Add((ushort)pk.Species);

        for (int b = 0; b < sav.BoxCount; b++)
            foreach (var pk in sav.GetBoxData(b))
                if (pk?.Species > 0 && pk.Species <= MaxSpecies)
                    _unlocked.Add((ushort)pk.Species);

        Save();
        return _unlocked.Count - before;
    }

    public (int caught, int total)[] GetStatsByGen()
    {
        var result = new (int, int)[Generations.Length];
        for (int g = 0; g < Generations.Length; g++)
        {
            var (start, end, _) = Generations[g];
            int caught = 0, total = 0;
            for (ushort s = start; s <= end; s++)
            {
                total++;
                if (_unlocked.Contains(s)) caught++;
            }
            result[g] = (caught, total);
        }
        return result;
    }

    private static HashSet<ushort> Load()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<ushort>>(json);
            if (list is { Count: > 0 }) return new HashSet<ushort>(list);
        }
        catch { }
        return [];
    }

    private void Save()
        => File.WriteAllText(FilePath, JsonSerializer.Serialize(_unlocked.OrderBy(x => x).ToList()));
}
