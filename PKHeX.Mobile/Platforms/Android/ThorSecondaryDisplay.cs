using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Views;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Platforms.Android;

/// <summary>
/// AYN Thor dual-screen implementation using Android's Presentation API.
/// The Thor has a second AMOLED display (1920×1080 landscape) accessible via
/// <see cref="DisplayManager.GetDisplays()"/>. This class creates a
/// <see cref="Presentation"/> window on that display and hosts MAUI content in it.
///
/// NOTE: This is a scaffold — the Thor's exact display routing behavior is unconfirmed.
/// The Presentation API approach is the most likely path but may need adjustment
/// once we have hardware to test against.
/// </summary>
public sealed class ThorSecondaryDisplay : ISecondaryDisplay, IDisposable
{
    private readonly DisplayManager? _displayManager;
    private readonly Display? _secondaryDisplay;
    private ThorPresentation? _presentation;
    private Microsoft.Maui.Controls.View? _currentContent;

    public bool IsAvailable => _secondaryDisplay is not null;

    public ThorSecondaryDisplay()
    {
        var context = global::Android.App.Application.Context;
        _displayManager = context.GetSystemService(Context.DisplayService) as DisplayManager;

        // Look for a secondary display beyond the default (index 0)
        var displays = _displayManager?.GetDisplays();
        if (displays is { Length: > 1 })
            _secondaryDisplay = displays[1];
    }

    public void SetContent(global::Microsoft.Maui.Controls.View content)
    {
        // Convert MAUI View → Android native View via the handler
        // This requires the view to have a handler attached (i.e., it must be part of the visual tree
        // or we need to create a handler manually). This is the part most likely to need adjustment.
        _currentContent = content;

        if (_presentation is not null && content.Handler?.PlatformView is global::Android.Views.View nativeView)
        {
            _presentation.SetContentView(nativeView);
        }
    }

    public void Show()
    {
        if (_secondaryDisplay is null) return;

        var activity = Platform.CurrentActivity;
        if (activity is null) return;

        _presentation ??= new ThorPresentation(activity, _secondaryDisplay);

        try
        {
            _presentation.Show();

            // If we have pending content, attach it
            if (_currentContent?.Handler?.PlatformView is global::Android.Views.View nativeView)
                _presentation.SetContentView(nativeView);
        }
        catch (Exception)
        {
            // Presentation may fail if display is unavailable or permissions are missing.
            // Fall through silently — the page will still render in single-screen mode.
        }
    }

    public void Hide()
    {
        try
        {
            _presentation?.Hide();
        }
        catch
        {
            // Ignore errors during teardown
        }
    }

    public void Dispose()
    {
        Hide();
        _presentation?.Dismiss();
        _presentation = null;
    }

    /// <summary>
    /// Android Presentation subclass for the Thor's secondary display.
    /// </summary>
    private sealed class ThorPresentation : Presentation
    {
        public ThorPresentation(Context outerContext, Display display)
            : base(outerContext, display)
        {
        }

        protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Full-screen, no system bars on the secondary display
            Window?.SetFlags(
                WindowManagerFlags.Fullscreen,
                WindowManagerFlags.Fullscreen);
        }
    }
}
