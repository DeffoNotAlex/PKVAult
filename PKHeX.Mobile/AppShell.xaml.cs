using PKHeX.Mobile.Pages;

namespace PKHeX.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(BoxPage), typeof(BoxPage));
        Routing.RegisterRoute(nameof(PkmEditorPage), typeof(PkmEditorPage));
        Routing.RegisterRoute(nameof(DatabasePage), typeof(DatabasePage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(MysteryGiftDBPage), typeof(MysteryGiftDBPage));
    }
}
