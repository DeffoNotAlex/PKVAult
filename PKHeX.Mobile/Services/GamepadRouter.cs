#if ANDROID
using Android.Views;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Static event bridge routing Android gamepad/key events from MainActivity
/// to whichever MAUI page is currently active.
/// GamePage subscribes in OnAppearing and unsubscribes in OnDisappearing.
/// </summary>
public static class GamepadRouter
{
    public static event Action<Keycode, KeyEventActions>? KeyReceived;

    /// <summary>Called by MainActivity.DispatchKeyEvent before base handling.</summary>
    public static bool Dispatch(Keycode keyCode, KeyEventActions action)
    {
        if (KeyReceived is null)
            return false;
        KeyReceived.Invoke(keyCode, action);
        return true;
    }
}
#endif
