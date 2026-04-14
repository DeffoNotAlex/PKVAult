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

    /// <summary>Switch the bottom screen to main-menu mode and populate the save list.</summary>
    void ShowMainMenu(IList<object> saves, int cursorIndex);

    /// <summary>Update cursor/focus state on the main-menu bottom screen.</summary>
    void UpdateMainMenuState(int cursorIndex, int focusSection, int actionCursor);

    /// <summary>Switch the bottom screen to bank-grid mode (Mode 2 — view only).</summary>
    void ShowBankGrid(PKM?[] slots, int cursorSlot, string boxName, int boxIndex, int boxCount);

    /// <summary>Update only the cursor position on the bank grid (no slot reload).</summary>
    void UpdateBankCursor(int cursorSlot);

    /// <summary>Trigger a repaint of the bank grid canvas.</summary>
    void InvalidateBankCanvas();

    /// <summary>Switch the bottom screen to welcome-wizard mode and show the given step.</summary>
    /// <param name="step">0 = theme, 1 = emulator, 2 = done.</param>
    /// <param name="onEvent">Callback fired with event strings: "next", "skip", "finish",
    /// "theme:dark", "theme:light", "eden", "azahar", "melonds", "retroarch", "manual".</param>
    void ShowWelcomeStep(int step, Action<string> onEvent);

    /// <summary>Called when a save was found during step 1 scanning so the bottom screen can update its counter.</summary>
    void NotifyWelcomeSaveFound(string gameName);

    /// <summary>Dismiss the welcome panel and restore the bottom screen to idle/main-menu state.</summary>
    void HideWelcome();
}
