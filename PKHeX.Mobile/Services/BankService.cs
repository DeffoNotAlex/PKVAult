using System.Text.Json;
using PKHeX.Core;

namespace PKHeX.Mobile.Services;

public class BankService
{
    public const int SlotsPerBox = 30;
    private const string FileName = "bank.json";

    private static string FilePath =>
        Path.Combine(FileSystem.AppDataDirectory, FileName);

    private List<BankBox> _boxes;
    public IReadOnlyList<BankBox> Boxes => _boxes;

    public BankService() => _boxes = Load();

    // ── Persistence ───────────────────────────────────────────────

    private static List<BankBox> Load()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<List<BankBox>>(json);
                if (data is { Count: > 0 }) return data;
            }
            catch { }
        }
        return DefaultBoxes();
    }

    public void Save()
        => File.WriteAllText(FilePath, JsonSerializer.Serialize(_boxes));

    private static List<BankBox> DefaultBoxes()
        => Enumerable.Range(0, 5).Select(i => new BankBox { Name = $"Bank {i + 1}" }).ToList();

    // ── Box data ──────────────────────────────────────────────────

    /// <summary>Deserialise one bank box into PKM? array for rendering.</summary>
    public PKM?[] GetBoxData(int box)
    {
        var result = new PKM?[SlotsPerBox];
        if (box >= _boxes.Count) return result;
        var slots = _boxes[box].Slots;
        for (int i = 0; i < Math.Min(slots.Count, SlotsPerBox); i++)
            result[i] = slots[i]?.ToPKM();
        return result;
    }

    // ── Deposit / withdraw ────────────────────────────────────────

    public void Deposit(int box, int slot, PKM pk)
    {
        EnsureBox(box);
        EnsureSlots(_boxes[box]);
        _boxes[box].Slots[slot] = BankSlot.FromPKM(pk);
        AutoExpand();
        Save();
    }

    public void ClearSlot(int box, int slot)
    {
        if (box >= _boxes.Count) return;
        EnsureSlots(_boxes[box]);
        _boxes[box].Slots[slot] = null;
        Save();
    }

    public BankSlot? GetSlot(int box, int slot)
    {
        if (box >= _boxes.Count) return null;
        EnsureSlots(_boxes[box]);
        return slot < _boxes[box].Slots.Count ? _boxes[box].Slots[slot] : null;
    }

    /// <summary>Returns index of the first empty slot in the box (0 if box is full).</summary>
    public int FindFirstEmpty(int box)
    {
        if (box >= _boxes.Count) return 0;
        EnsureSlots(_boxes[box]);
        var idx = _boxes[box].Slots.FindIndex(s => s == null);
        return idx < 0 ? 0 : idx;
    }

    // ── Box management ────────────────────────────────────────────

    public void CreateBox(string name)
    {
        _boxes.Add(new BankBox { Name = name });
        Save();
    }

    public void RenameBox(int box, string name)
    {
        if (box >= _boxes.Count) return;
        _boxes[box].Name = name;
        Save();
    }

    // ── Helpers ───────────────────────────────────────────────────

    private void EnsureBox(int box)
    {
        while (_boxes.Count <= box)
            _boxes.Add(new BankBox { Name = $"Bank {_boxes.Count + 1}" });
    }

    private static void EnsureSlots(BankBox box)
    {
        while (box.Slots.Count < SlotsPerBox)
            box.Slots.Add(null);
    }

    private void AutoExpand()
    {
        var last = _boxes[^1];
        EnsureSlots(last);
        if (last.Slots.All(s => s != null))
            _boxes.Add(new BankBox { Name = $"Bank {_boxes.Count + 1}" });
    }
}

// ── Data model ─────────────────────────────────────────────────────

public class BankBox
{
    public string Name { get; set; } = "";
    public List<BankSlot?> Slots { get; set; } = [];
}

public class BankSlot
{
    public byte[]  Data        { get; set; } = [];
    public string  TypeExt     { get; set; } = "pk9"; // e.g. "pk9", "pa8", "pb7"
    public int     Species     { get; set; }
    public int     Level       { get; set; }
    public bool    IsShiny     { get; set; }
    public string  Nickname    { get; set; } = "";
    /// <summary>UTC timestamp when this slot was deposited (ISO 8601). Null for legacy entries.</summary>
    public string? DepositedAt { get; set; }

    public static BankSlot FromPKM(PKM pk) => new()
    {
        Data        = pk.Data.ToArray(),
        TypeExt     = pk.GetType().Name.ToLower(),
        Species     = pk.Species,
        Level       = pk.CurrentLevel,
        IsShiny     = pk.IsShiny,
        Nickname    = pk.Nickname,
        DepositedAt = DateTime.UtcNow.ToString("o"),
    };

    public PKM? ToPKM()
    {
        try
        {
            FileUtil.TryGetPKM(Data.AsMemory(), out var pk, ("." + TypeExt).AsSpan());
            return pk;
        }
        catch { return null; }
    }
}
