using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Views;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Platforms.Android;

/// <summary>
/// AYN Thor dual-screen implementation.
///
/// The Thor exposes its second AMOLED (1920×1080 landscape) as a standard Android
/// secondary display — the same mechanism as an HDMI monitor. It is NOT a foldable
/// and does NOT use Jetpack WindowManager's WindowAreaController (which requires OEM
/// firmware extensions that the Thor does not implement).
///
/// The correct API is DisplayManager.GetDisplays(DISPLAY_CATEGORY_PRESENTATION),
/// which the Thor's firmware registers the second screen under. Content is hosted in
/// an Android Presentation window on that display.
/// </summary>
public sealed class ThorSecondaryDisplay : ISecondaryDisplay, IDisposable
{
    private readonly Display? _secondaryDisplay;
    private ThorPresentation? _presentation;
    private Microsoft.Maui.Controls.View? _pendingContent;

    public bool IsAvailable => _secondaryDisplay is not null;

    public ThorSecondaryDisplay()
    {
        var context = global::Android.App.Application.Context;
        var dm = context.GetSystemService(Context.DisplayService) as DisplayManager;

        // Use DISPLAY_CATEGORY_PRESENTATION — the Thor registers its second screen
        // under this category. This is more reliable than index-based access.
        var displays = dm?.GetDisplays(DisplayManager.DisplayCategoryPresentation);
        _secondaryDisplay = displays?.FirstOrDefault();
    }

    public void SetContent(Microsoft.Maui.Controls.View content)
    {
        _pendingContent = content;

        if (_presentation is not null && content.Handler?.PlatformView is global::Android.Views.View nativeView)
            _presentation.SetContentView(nativeView);
    }

    public void Show()
    {
        if (_secondaryDisplay is null) return;

        var activity = Platform.CurrentActivity;
        if (activity is null) return;

        // Recreate presentation if dismissed
        if (_presentation is null || !_presentation.IsShowing)
        {
            _presentation?.Dismiss();
            _presentation = new ThorPresentation(activity, _secondaryDisplay, _pendingContent);
        }

        try
        {
            _presentation.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Thor] Presentation.Show() failed: {ex.Message}");
        }
    }

    public void Hide()
    {
        try { _presentation?.Hide(); }
        catch { /* ignore teardown errors */ }
    }

    public void Dispose()
    {
        try { _presentation?.Dismiss(); }
        catch { }
        _presentation = null;
    }

    private sealed class ThorPresentation : Presentation
    {
        private readonly Microsoft.Maui.Controls.View? _initialContent;

        public ThorPresentation(Context context, Display display, Microsoft.Maui.Controls.View? initialContent)
            : base(context, display)
        {
            _initialContent = initialContent;
        }

        protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Full-screen, no system bars
            Window?.AddFlags(WindowManagerFlags.Fullscreen);
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

            // Attach content here — this is the correct time, not after Show()
            if (_initialContent?.Handler?.PlatformView is global::Android.Views.View nativeView)
                SetContentView(nativeView);
        }
    }
}
