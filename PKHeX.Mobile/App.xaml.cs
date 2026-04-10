using PKHeX.Core;
using PKHeX.Mobile.Pages;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

public partial class App : Application
{
    /// <summary>The currently loaded save file, set by MainPage after a successful parse.</summary>
    public static SaveFile? ActiveSave { get; set; }

    /// <summary>All save entries discovered by the last directory scan, used for compatibility checks.</summary>
    public static List<SaveDirectoryService.SaveEntry> LoadedSaves { get; set; } = [];

    /// <summary>Original filename of the loaded save, used for export.</summary>
    public static string ActiveSaveFileName { get; set; } = "save.bin";

    /// <summary>SAF content URI of the loaded save, used for write-back.</summary>
    public static string ActiveSaveFileUri { get; set; } = "";

    // ── Cross-page Pokémon move (box ↔ bank) ──────────────────────
    /// <summary>PKM currently being carried across a bank/box swap.</summary>
    public static PKHeX.Core.PKM? PendingMove      { get; set; }
    /// <summary>Box index the move originated from.</summary>
    public static int  PendingSourceBox             { get; set; } = -1;
    /// <summary>Slot index the move originated from.</summary>
    public static int  PendingSourceSlot            { get; set; } = -1;
    /// <summary>True when the move originated from the bank (withdraw), false when from game box (deposit).</summary>
    public static bool PendingFromBank              { get; set; }

    /// <summary>Direction of the last bank swap: -1 = L1 (bank enters from left), +1 = R1 (bank enters from right).</summary>
    public static int  BankSlideDir                 { get; set; } = -1;

    public App()
    {
        InitializeComponent();
        ThemeService.ApplyOnStartup();
        SettingsPage.ApplyOnStartup();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            return new Window(new AppShell());
        }
        catch (Exception ex)
        {
            var page = new ContentPage
            {
                Content = new Label
                {
                    Text = $"Startup error:\n{ex}",
                    Margin = new Thickness(20),
                    VerticalOptions = LayoutOptions.Center,
                },
            };
            return new Window(page);
        }
    }
}
