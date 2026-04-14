using PKHeX.Core;

namespace PKHeX.Mobile.Services;

/// <summary>No-op implementation for single-screen devices.</summary>
public sealed class SingleScreenFallback : ISecondaryDisplay
{
    public bool IsAvailable => false;
    public void Show() { }
    public void Hide() { }
    public void UpdateBoxGrid(
        PKM[] box, int cursorSlot, int selectedSlot,
        bool moveMode, PKM? movePk, int moveSourceBox, int moveSourceSlot,
        int currentBoxIndex, string boxName, bool?[] legalityCache, bool showLegalityBadges) { }
    public void UpdateCursor(int cursorSlot, int selectedSlot, bool moveMode, PKM? movePk, int currentBoxIndex) { }
    public void InvalidateBoxCanvas() { }
    public void ShowMainMenu(IList<object> saves, int cursorIndex) { }
    public void UpdateMainMenuState(int cursorIndex, int focusSection, int actionCursor) { }
    public void ShowBankGrid(PKM?[] slots, int cursorSlot, string boxName, int boxIndex, int boxCount) { }
    public void UpdateBankCursor(int cursorSlot) { }
    public void InvalidateBankCanvas() { }
    public void ShowWelcomeStep(int step, Action<string> onEvent) { }
    public void NotifyWelcomeSaveFound(string gameName) { }
    public void HideWelcome() { }
}
