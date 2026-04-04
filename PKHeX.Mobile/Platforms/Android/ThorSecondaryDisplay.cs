using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Util;
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
/// Display detection is deferred to Show() so it works even if the second screen
/// isn't ready at app startup.
/// </summary>
public sealed class ThorSecondaryDisplay : ISecondaryDisplay, IDisposable
{
    private const string Tag = "ThorDisplay";

    private readonly IServiceProvider _services;
    private readonly SecondScreenPage _secondPage = new();
    private ThorPresentation?         _presentation;
    private Display?                  _cachedDisplay;

    public ThorSecondaryDisplay(IServiceProvider services) => _services = services;

    public bool IsAvailable => ResolveDisplay() is not null;

    public void Show()
    {
        var display = ResolveDisplay();
        if (display is null)
        {
            Log.Warn(Tag, "Show() called but no secondary display detected — skipping.");
            return;
        }

        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            Log.Warn(Tag, "Show() called but Platform.CurrentActivity is null.");
            return;
        }

        // Reuse existing presentation if still showing
        if (_presentation is not null && _presentation.IsShowing)
        {
            Log.Debug(Tag, "Show() — presentation already showing, skipping.");
            return;
        }

        _presentation?.Dismiss();
        _presentation = new ThorPresentation(activity, display, _secondPage, _services);

        try
        {
            _presentation.Show();
            Log.Info(Tag, $"Presentation shown on display id={display.DisplayId} name='{display.Name}'.");
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Presentation.Show() failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public void Hide()
    {
        // Dismiss (not just Hide) so IsShowing becomes false and the next
        // Show() call recreates the Presentation cleanly rather than skipping.
        try { _presentation?.Dismiss(); }
        catch { }
        _presentation = null;
    }

    public void UpdateBoxGrid(
        PKM[] box, int cursorSlot, int selectedSlot,
        bool moveMode, PKM? movePk, int moveSourceBox, int moveSourceSlot,
        int currentBoxIndex, string boxName, bool?[] legalityCache, bool showLegalityBadges)
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.UpdateBoxGrid(
            box, cursorSlot, selectedSlot,
            moveMode, movePk, moveSourceBox, moveSourceSlot,
            currentBoxIndex, boxName, legalityCache, showLegalityBadges));

    public void UpdateCursor(int cursorSlot, int selectedSlot, bool moveMode, PKM? movePk, int currentBoxIndex)
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.UpdateCursor(cursorSlot, selectedSlot, moveMode, movePk, currentBoxIndex));

    public void InvalidateBoxCanvas()
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.InvalidateBoxCanvas());

    public void ShowMainMenu(IList<object> saves, int cursorIndex)
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.ShowMainMenu(saves, cursorIndex));

    public void UpdateMainMenuState(int cursorIndex, int focusSection, int actionCursor)
        => MainThread.BeginInvokeOnMainThread(() => _secondPage.UpdateMainMenuState(cursorIndex, focusSection, actionCursor));

    public void Dispose()
    {
        try { _presentation?.Dismiss(); }
        catch { }
        _presentation = null;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Display detection — lazy, with full diagnostic dump + two-pass fallback
    // ──────────────────────────────────────────────────────────────────────

    private Display? ResolveDisplay()
    {
        if (_cachedDisplay is not null) return _cachedDisplay;

        var context = global::Android.App.Application.Context;
        var dm      = context.GetSystemService(Context.DisplayService) as DisplayManager;
        if (dm is null)
        {
            Log.Error(Tag, "Could not obtain DisplayManager.");
            return null;
        }

        // Dump every display the OS knows about so we can see what the Thor reports.
        var all = dm.GetDisplays() ?? [];
        Log.Info(Tag, $"GetDisplays() returned {all.Length} display(s):");
        foreach (var d in all)
            Log.Info(Tag, $"  id={d.DisplayId} name='{d.Name}' state={d.State} flags={d.Flags}");

        // Pass 1: devices that tag the second screen as a presentation display (HDMI, etc.)
        var presDisplays = dm.GetDisplays(DisplayManager.DisplayCategoryPresentation);
        Log.Info(Tag, $"PRESENTATION category: {presDisplays?.Length ?? 0} display(s).");
        if (presDisplays?.Length > 0)
        {
            _cachedDisplay = presDisplays[0];
            Log.Info(Tag, $"Using presentation display id={_cachedDisplay.DisplayId}.");
            return _cachedDisplay;
        }

        // Pass 2: devices (like Thor) whose second built-in screen isn't tagged — fall back
        // to index 1 in the full display list.
        if (all.Length > 1)
        {
            _cachedDisplay = all[1];
            Log.Info(Tag, $"Using fallback GetDisplays()[1] id={_cachedDisplay.DisplayId}.");
            return _cachedDisplay;
        }

        Log.Warn(Tag, "No secondary display detected (only one display in GetDisplays()).");
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

                // Kill the orange focus ring on every RecyclerView inside THIS
                // Presentation window. The main Activity's DecorView walk never
                // reaches here because this is a separate Android window.
                Window?.DecorView?.Post(() =>
                    StripFocusFromGroup(Window.DecorView as global::Android.Views.ViewGroup));

                Log.Info(Tag, "ContentPage inflated on second display.");
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"ToPlatform failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void StripFocusFromGroup(global::Android.Views.ViewGroup? group)
        {
            if (group is null) return;
            for (int i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    rv.Focusable = false;
                    rv.FocusableInTouchMode = false;
                    rv.DescendantFocusability = global::Android.Views.DescendantFocusability.BlockDescendants;
                }
                else if (child is global::Android.Views.ViewGroup vg)
                {
                    StripFocusFromGroup(vg);
                }
            }
        }
    }
}
