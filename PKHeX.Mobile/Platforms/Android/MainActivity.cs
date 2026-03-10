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
    // Previous analog axis positions for edge detection
    private float _prevHatX, _prevHatY;
    private float _prevLX,   _prevLY;

    private const float AxisThreshold = 0.45f;

    // ── Key events (digital buttons + digital D-pad) ──────────────────────
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null
            && (e.Source & (InputSourceType.Gamepad | InputSourceType.Joystick)) != 0
            && GamepadRouter.Dispatch(e.KeyCode, e.Action))
            return true;

        return base.DispatchKeyEvent(e);
    }

    // ── Motion events (analog D-pad HAT axis + left stick) ───────────────
    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if (e is not null
            && e.Action == MotionEventActions.Move
            && (e.Source & (InputSourceType.Joystick | InputSourceType.Gamepad)) != 0)
        {
            // D-pad HAT axis (most gamepads, including many built-in gamepad buttons)
            FireAxis(e.GetAxisValue(Axis.HatX), ref _prevHatX, Keycode.DpadLeft, Keycode.DpadRight);
            FireAxis(e.GetAxisValue(Axis.HatY), ref _prevHatY, Keycode.DpadUp,   Keycode.DpadDown);

            // Left analog stick (for games/devices that route D-pad through stick)
            FireAxis(e.GetAxisValue(Axis.X),    ref _prevLX,   Keycode.DpadLeft, Keycode.DpadRight);
            FireAxis(e.GetAxisValue(Axis.Y),    ref _prevLY,   Keycode.DpadUp,   Keycode.DpadDown);

            return true;
        }

        return base.DispatchGenericMotionEvent(e);
    }

    /// <summary>
    /// Edge-detects an analog axis crossing the threshold and fires the
    /// corresponding D-pad keycode exactly once per crossing.
    /// </summary>
    private static void FireAxis(float value, ref float prev, Keycode neg, Keycode pos)
    {
        if      (value < -AxisThreshold && prev >= -AxisThreshold)
            GamepadRouter.Dispatch(neg, KeyEventActions.Down);
        else if (value >  AxisThreshold && prev <=  AxisThreshold)
            GamepadRouter.Dispatch(pos, KeyEventActions.Down);
        // Release: fire a synthetic Up when the stick returns to centre
        else if (Math.Abs(value) <= AxisThreshold && Math.Abs(prev) > AxisThreshold)
            GamepadRouter.Dispatch(Math.Sign(prev) < 0 ? neg : pos, KeyEventActions.Up);

        prev = value;
    }
}
