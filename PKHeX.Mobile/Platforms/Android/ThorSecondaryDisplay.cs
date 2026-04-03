using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Views;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
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
    private readonly Display?          _secondaryDisplay;
    private readonly IServiceProvider  _services;
    private ThorPresentation?          _presentation;

    public bool IsAvailable => _secondaryDisplay is not null;

    public ThorSecondaryDisplay(IServiceProvider services)
    {
        _services = services;

        var context = global::Android.App.Application.Context;
        var dm      = context.GetSystemService(Context.DisplayService) as DisplayManager;

        // DISPLAY_CATEGORY_PRESENTATION is the category the Thor registers its second screen
        // under. More reliable than index-based access (which would be [1]).
        var displays      = dm?.GetDisplays(DisplayManager.DisplayCategoryPresentation);
        _secondaryDisplay = displays?.FirstOrDefault();
    }

    public void Show(ContentPage page)
    {
        if (_secondaryDisplay is null) return;

        var activity = Platform.CurrentActivity;
        if (activity is null) return;

        // Reuse existing presentation if it is still showing — avoids re-inflating the page
        // (which would fail with "View already has a parent" on the second call).
        if (_presentation is not null && _presentation.IsShowing)
            return;

        _presentation?.Dismiss();
        _presentation = new ThorPresentation(activity, _secondaryDisplay, page, _services);

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

    // ──────────────────────────────────────────────────────────────────────
    //  Inner Presentation
    // ──────────────────────────────────────────────────────────────────────

    private sealed class ThorPresentation : Presentation
    {
        private readonly ContentPage      _page;
        private readonly IServiceProvider _services;

        public ThorPresentation(
            Context context,
            Display display,
            ContentPage page,
            IServiceProvider services)
            : base(context, display)
        {
            _page     = page;
            _services = services;
        }

        protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Full-screen, no system bars, keep display on
            Window?.AddFlags(WindowManagerFlags.Fullscreen);
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

            // Inflate the MAUI ContentPage into a native Android View.
            // We create a MauiContext scoped to this Presentation's Android context
            // so that display metrics (density, size) are correct for the second screen.
            try
            {
                var mauiContext = new MauiContext(_services, Context!);
                var nativeView  = _page.ToPlatform(mauiContext);
                SetContentView(nativeView);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Thor] ToPlatform failed: {ex.Message}");
            }
        }
    }
}
