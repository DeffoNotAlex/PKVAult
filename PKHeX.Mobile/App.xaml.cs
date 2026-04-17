using PKHeX.Mobile.Pages;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

public partial class App : Application
{
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
