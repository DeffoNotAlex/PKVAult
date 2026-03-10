using PKHeX.Core;
using PKHeX.Mobile.Pages;

namespace PKHeX.Mobile;

public partial class App : Application
{
    /// <summary>The currently loaded save file, set by MainPage after a successful parse.</summary>
    public static SaveFile? ActiveSave { get; set; }

    /// <summary>Original filename of the loaded save, used for export.</summary>
    public static string ActiveSaveFileName { get; set; } = "save.bin";

    public App()
    {
        InitializeComponent();
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
