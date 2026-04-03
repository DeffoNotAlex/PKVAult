using PKHeX.Core;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Abstraction for the AYN Thor's second AMOLED display (bottom screen).
/// The bottom screen mirrors the interactive box grid; the top screen (primary)
/// shows the info panel (trainer card, Pokémon detail) directly via GamePage Row 0.
/// On single-screen devices all methods are no-ops.
/// </summary>
public interface ISecondaryDisplay
{
    bool IsAvailable { get; }
    void Show();
    void Hide();

    /// <summary>Push current box state to the bottom screen grid.</summary>
    void UpdateBoxGrid(
        PKM[] box, int cursorSlot, int selectedSlot,
        bool moveMode, PKM? movePk, int moveSourceBox, int moveSourceSlot,
        int currentBoxIndex, string boxName, bool?[] legalityCache, bool showLegalityBadges);

    /// <summary>Push updated cursor/selection state without resending the full box array.</summary>
    void UpdateCursor(int cursorSlot, int selectedSlot, bool moveMode, PKM? movePk, int currentBoxIndex);

    /// <summary>Trigger a repaint of the bottom screen grid (called by the cursor pulse timer).</summary>
    void InvalidateBoxCanvas();
}
