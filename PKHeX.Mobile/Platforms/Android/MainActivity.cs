using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using AndroidX.Core.View;
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
    public const int RequestPickDirectory = 9001;

    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetImmersiveMode();
    }

    protected override void OnStop()
    {
        base.OnStop();
        // Dismiss the secondary-screen Presentation so it doesn't stay
        // visible on the bottom screen when the Home button sends the app
        // to the background. OnAppearing on each page calls Show() again
        // when the user returns, so no explicit re-show is needed here.
        var secondary = IPlatformApplication.Current?.Services
            .GetService<Services.ISecondaryDisplay>();
        secondary?.Hide();
    }

    /// <summary>
    /// Strips the Android focus highlight from every RecyclerView in the window.
    /// We intercept all gamepad keys at DispatchKeyEvent so view focus is unused.
    /// Posted to the main looper so it runs after MAUI inflates its content.
    /// </summary>
    private void DisableRecyclerViewFocusHighlight()
    {
        Window?.DecorView?.Post(() =>
        {
            StripFocusFromGroup(Window.DecorView as Android.Views.ViewGroup);
        });
    }

    private static void StripFocusFromGroup(Android.Views.ViewGroup? group)
    {
        if (group is null) return;
        for (int i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                rv.Focusable = false;
                rv.FocusableInTouchMode = false;
                // Block focus from ever reaching item views inside the RecyclerView —
                // this is the definitive kill for the orange selection ring.
                rv.DescendantFocusability = Android.Views.DescendantFocusability.BlockDescendants;
            }
            else if (child is Android.Views.ViewGroup vg)
            {
                StripFocusFromGroup(vg);
            }
        }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (!hasFocus) return;
        SetImmersiveMode();
        // Re-run every time the window gains focus so RecyclerViews created
        // after navigation (lazy page inflation) are also caught.
        DisableRecyclerViewFocusHighlight();
    }

    private void SetImmersiveMode()
    {
        if (Window?.DecorView is null) return;
        WindowCompat.SetDecorFitsSystemWindows(Window, false);
        var ctrl = WindowCompat.GetInsetsController(Window, Window.DecorView);
        if (ctrl is null) return;
        ctrl.Hide(WindowInsetsCompat.Type.StatusBars());
        ctrl.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
    }


    public const int RequestPickFile      = 9002;
    public static event Action<Android.Net.Uri?>? DirectoryPickResult;
    public static event Action<Android.Net.Uri?>? FilePickResult;

    // Previous analog axis positions for edge detection
    private float _prevHatX, _prevHatY;
    private float _prevLX,   _prevLY;
    private float _prevRX;   // right stick horizontal

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

            // Right stick horizontal → box scroll
            FireRightStick(e.GetAxisValue(Axis.Z), ref _prevRX);

            return true;
        }

        return base.DispatchGenericMotionEvent(e);
    }

    private static void FireRightStick(float value, ref float prev)
    {
        if      (value < -AxisThreshold && prev >= -AxisThreshold)
            GamepadRouter.DispatchBoxScroll(-1);
        else if (value >  AxisThreshold && prev <=  AxisThreshold)
            GamepadRouter.DispatchBoxScroll(+1);
        prev = value;
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

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == RequestPickDirectory)
            DirectoryPickResult?.Invoke(resultCode == Result.Ok ? data?.Data : null);
        else if (requestCode == RequestPickFile)
            FilePickResult?.Invoke(resultCode == Result.Ok ? data?.Data : null);
    }
}
