using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Views;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using PKHeX.Core;
using PKHeX.Mobile.Pages;
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
/// Display detection tries DISPLAY_CATEGORY_PRESENTATION first, then falls back to
/// GetDisplays()[1] (index-based) in case the Thor's firmware doesn't tag its second
/// screen with the presentation category. Detection is deferred to Show() so it picks
/// up the display even if it isn't ready at app startup.
/// </summary>
public sealed class ThorSecondaryDisplay : ISecondaryDisplay, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly SecondScreenPage _secondPage = new();
    private ThorPresentation?         _presentation;
    private Display?                  _cachedDisplay;

    public ThorSecondaryDisplay(IServiceProvider services) => _services = services;

    /// <summary>
    /// Returns true when a secondary display is detectable at check time.
    /// Caches the result after the first successful detection.
    /// </summary>
    public bool IsAvailable => ResolveDisplay() is not null;

    public void Show()
    {
        var display = ResolveDisplay();
        if (display is null) return;

        var activity = Platform.CurrentActivity;
        if (activity is null) return;

        // Reuse existing presentation if still showing
        if (_presentation is not null && _presentation.IsShowing)
            return;

        _presentation?.Dismiss();
        _presentation = new ThorPresentation(activity, display, _secondPage, _services);

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
        catch { }
    }

    public void UpdateTrainer(SaveFile sav, string boxName, int filled, int total)
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.UpdateTrainer(sav, boxName, filled, total));

    public void UpdateBoxInfo(string boxName, int filled, int total)
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.UpdateBoxInfo(boxName, filled, total));

    public void UpdatePokemon(PKM pk)
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.UpdatePokemon(pk));

    public void ClearPokemon()
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.ClearPokemon());

    public void Dispose()
    {
        try { _presentation?.Dismiss(); }
        catch { }
        _presentation = null;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Display detection — lazy, with two-pass fallback
    // ──────────────────────────────────────────────────────────────────────

    private Display? ResolveDisplay()
    {
        if (_cachedDisplay is not null) return _cachedDisplay;

        var context = global::Android.App.Application.Context;
        var dm      = context.GetSystemService(Context.DisplayService) as DisplayManager;
        if (dm is null) return null;

        // Pass 1: devices that tag the second screen as a presentation display (HDMI, etc.)
        var presDisplays = dm.GetDisplays(DisplayManager.DisplayCategoryPresentation);
        if (presDisplays?.Length > 0)
        {
            _cachedDisplay = presDisplays[0];
            System.Diagnostics.Debug.WriteLine($"[Thor] Second display found via PRESENTATION category: id={_cachedDisplay.DisplayId}");
            return _cachedDisplay;
        }

        // Pass 2: devices (like Thor) whose second built-in screen isn't tagged with the
        // presentation category — fall back to index 1 in the full display list.
        var allDisplays = dm.GetDisplays();
        if (allDisplays?.Length > 1)
        {
            _cachedDisplay = allDisplays[1];
            System.Diagnostics.Debug.WriteLine($"[Thor] Second display found via GetDisplays()[1]: id={_cachedDisplay.DisplayId}");
            return _cachedDisplay;
        }

        System.Diagnostics.Debug.WriteLine("[Thor] No secondary display detected.");
        return null;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Inner Presentation
    // ──────────────────────────────────────────────────────────────────────

    private sealed class ThorPresentation : Presentation
    {
        private readonly ContentPage      _page;
        private readonly IServiceProvider _services;

        public ThorPresentation(Activity activity, Display display, ContentPage page, IServiceProvider services)
            : base(activity, display)
        {
            _page     = page;
            _services = services;
        }

        protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Window?.AddFlags(WindowManagerFlags.Fullscreen);
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

            // Use the Activity as the Android context (not the Presentation's dialog context)
            // so that MAUI handlers can resolve fonts and drawables registered against the Activity.
            try
            {
                var activity = Platform.CurrentActivity
                               ?? throw new InvalidOperationException("No current Activity");

                var mauiContext = new MauiContext(_services, activity);
                var nativeView  = _page.ToPlatform(mauiContext);
                SetContentView(nativeView);

                System.Diagnostics.Debug.WriteLine("[Thor] ContentPage inflated on second display.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Thor] ToPlatform failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
