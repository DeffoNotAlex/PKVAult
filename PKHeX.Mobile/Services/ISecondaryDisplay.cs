namespace PKHeX.Mobile.Services;

/// <summary>
/// Abstraction for rendering content on a secondary display.
/// On the AYN Thor, the second AMOLED screen is exposed as a standard Android presentation display.
/// On single-screen devices this is a no-op — GamePage owns its top row directly.
/// </summary>
public interface ISecondaryDisplay
{
    /// <summary>Whether a physical secondary display is connected and available.</summary>
    bool IsAvailable { get; }

    /// <summary>Show the given ContentPage on the secondary display window.</summary>
    void Show(ContentPage page);

    /// <summary>Hide the secondary display window.</summary>
    void Hide();
}
