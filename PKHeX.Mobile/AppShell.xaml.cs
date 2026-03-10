using PKHeX.Mobile.Pages;

namespace PKHeX.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(BoxPage), typeof(BoxPage));
    }
}
