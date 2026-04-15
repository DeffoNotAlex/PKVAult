using PKHeX.Mobile.Pages;

namespace PKHeX.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(GamePage), typeof(GamePage));
        Routing.RegisterRoute(nameof(BankPage),     typeof(BankPage));
        Routing.RegisterRoute(nameof(BankViewPage), typeof(BankViewPage));
        Routing.RegisterRoute(nameof(PkmEditorPage), typeof(PkmEditorPage));
        Routing.RegisterRoute(nameof(DatabasePage), typeof(DatabasePage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(MysteryGiftDBPage), typeof(MysteryGiftDBPage));
        Routing.RegisterRoute(nameof(FolderManagerPage), typeof(FolderManagerPage));
        Routing.RegisterRoute(nameof(WelcomePage), typeof(WelcomePage));
        Routing.RegisterRoute(nameof(DexPage),     typeof(DexPage));
    }
}
