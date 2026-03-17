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

    /// <summary>Fired when the right stick crosses the scroll threshold. -1 = prev box, +1 = next box.</summary>
    public static event Action<int>? BoxScrollRequested;

    /// <summary>Called by MainActivity for digital button/key events.</summary>
    public static bool Dispatch(Keycode keyCode, KeyEventActions action)
    {
        if (KeyReceived is null)
            return false;

        // Normalise common aliases so pages only handle canonical codes
        keyCode = keyCode switch
        {
            Keycode.DpadCenter   => Keycode.ButtonA,  // D-pad click = A
            Keycode.ButtonThumbl => Keycode.ButtonA,  // left-stick click = A on some pads
            _ => keyCode,
        };

        KeyReceived.Invoke(keyCode, action);
        return true;
    }

    /// <summary>Called by MainActivity when the right stick crosses the scroll threshold.</summary>
    public static void DispatchBoxScroll(int direction) => BoxScrollRequested?.Invoke(direction);
}
#endif
