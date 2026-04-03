namespace PKHeX.Mobile.Services;

/// <summary>
/// No-op implementation for single-screen devices.
/// GamePage owns its top row directly on single-screen hardware.
/// </summary>
public sealed class SingleScreenFallback : ISecondaryDisplay
{
    public bool IsAvailable => false;
    public void Show(ContentPage page) { }
    public void Hide() { }
}
