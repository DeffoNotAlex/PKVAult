using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using AndroidX.Core.View;
using AndroidX.Window.Java.Layout;
using AndroidX.Window.Layout;
using Java.Util.Concurrent;
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
        ConfigChanges.Density |
        ConfigChanges.WindowLayout)]   // required for fold-state changes without recreation
public class MainActivity : MauiAppCompatActivity
{
    public const int RequestPickDirectory = 9001;

    // ── Jetpack WindowManager (dual-screen / foldable spanning detection) ─
    // Fired on the main thread when the spanning / fold state changes.
    // True = app is spanning both displays (FoldingFeature.IsSeparating is true).
    public static event Action<bool>? SpanningChanged;

    private WindowInfoTrackerCallbackAdapter? _windowTracker;
    private readonly WindowLayoutCallback     _layoutCallback = new();

    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetImmersiveMode();

        _windowTracker = new WindowInfoTrackerCallbackAdapter(
            WindowInfoTracker.Companion.GetOrCreate(this));
    }

    protected override void OnStart()
    {
        base.OnStart();
        _windowTracker?.AddWindowLayoutInfoListener(
            this,
            Executors.NewSingleThreadExecutor()!,
            _layoutCallback);
    }

    protected override void OnStop()
    {
        base.OnStop();
        _windowTracker?.RemoveWindowLayoutInfoListener(_layoutCallback);
    }

    private sealed class WindowLayoutCallback : Java.Lang.Object, IConsumer
    {
        public void Accept(Java.Lang.Object t)
        {
            if (t is not WindowLayoutInfo info) return;
            bool spanning = info.DisplayFeatures
                .OfType<FoldingFeature>()
                .Any(f => f.IsSeparating);
            System.Diagnostics.Debug.WriteLine($"[WM] spanning={spanning}, features={info.DisplayFeatures.Count}");
            MainThread.BeginInvokeOnMainThread(() => SpanningChanged?.Invoke(spanning));
        }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) SetImmersiveMode();
    }

    private void SetImmersiveMode()
    {
        if (Window?.DecorView is null) return;
        WindowCompat.SetDecorFitsSystemWindows(Window, false);
        var ctrl = WindowCompat.GetInsetsController(Window, Window.DecorView);
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
