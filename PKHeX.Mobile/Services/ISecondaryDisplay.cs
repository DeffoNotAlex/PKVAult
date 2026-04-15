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

    // ── Intro reel (shown before wizard step 0) ───────────────────────────────

    /// <summary>
    /// Show a reel slide on the bottom screen: headline, subtext, progress dots, and a Skip button.
    /// <paramref name="slideIndex"/> is 0–5. <paramref name="onSkip"/> fires when Skip is tapped.
    /// </summary>
    void ShowReelSlide(int slideIndex, string headline, string subtext, Action onSkip);

    /// <summary>
    /// Brief transition overlay shown between reel slides (or between reel and wizard step 0).
    /// The bottom screen dims / shows a transition indicator.
    /// </summary>
    void ShowReelTransition();

    /// <summary>Dismiss the reel panel (called when the reel ends and the wizard begins).</summary>
    void HideReel();

    /// <summary>Switch the bottom screen to bank box management mode.</summary>
    /// <param name="boxIndex">Current box index (0-based).</param>
    /// <param name="boxName">Display name of the current box.</param>
    /// <param name="boxCount">Total number of boxes.</param>
    /// <param name="onAction">Callback fired with "rename", "add", or "remove".</param>
    void ShowBankManageMenu(int boxIndex, string boxName, int boxCount, Action<string> onAction);

    /// <summary>Dismiss the bank manage panel and return to the bank grid.</summary>
    void HideBankManageMenu();
}
