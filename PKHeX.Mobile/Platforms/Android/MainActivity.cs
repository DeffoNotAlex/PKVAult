using Android.App;
using Android.Content.PM;
using Android.Views;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null && GamepadRouter.Dispatch(e.KeyCode, e.Action))
            return true;
        return base.DispatchKeyEvent(e);
    }
}
