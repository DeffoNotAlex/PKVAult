using PKHeX.Core;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Singleton service that owns all mutable cross-page application state,
/// replacing the static properties that previously lived on <c>App</c>.
/// </summary>
public class SessionState
{
    /// <summary>The currently loaded save file, set by MainPage after a successful parse.</summary>
    public SaveFile? ActiveSave { get; set; }

    /// <summary>All save entries discovered by the last directory scan.</summary>
    public List<SaveEntry> LoadedSaves { get; set; } = [];

    /// <summary>Original filename of the loaded save, used for export.</summary>
    public string ActiveSaveFileName { get; set; } = "save.bin";

    /// <summary>SAF content URI of the loaded save, used for write-back.</summary>
    public string ActiveSaveFileUri { get; set; } = "";

    // ── Cross-page Pokémon move (box ↔ bank) ──────────────────────────────────

    /// <summary>PKM currently being carried across a bank/box swap.</summary>
    public PKM? PendingMove { get; set; }

    /// <summary>Box index the move originated from (-1 = bank).</summary>
    public int PendingSourceBox { get; set; } = -1;

    /// <summary>Slot index the move originated from.</summary>
    public int PendingSourceSlot { get; set; } = -1;

    /// <summary>True when the move originated from the bank (withdraw), false when from a game box (deposit).</summary>
    public bool PendingFromBank { get; set; }

    /// <summary>Direction of the last bank swap: -1 = L1 (bank enters from left), +1 = R1 (bank enters from right).</summary>
    public int BankSlideDir { get; set; } = -1;

    /// <summary>Set to true by SettingsPage/WelcomePage when watched directories change so MainPage re-scans on next appearance.</summary>
    public bool RescanNeeded { get; set; } = true;
}
