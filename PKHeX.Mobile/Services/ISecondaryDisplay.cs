namespace PKHeX.Mobile.Services;

/// <summary>
/// Abstraction for rendering content on a secondary display.
/// On the AYN Thor, the top 1920×1080 screen is the secondary display.
/// On single-screen devices, this falls back to rendering in the top Grid row.
/// </summary>
public interface ISecondaryDisplay
{
    /// <summary>Whether a physical secondary display is connected and available.</summary>
    bool IsAvailable { get; }

    /// <summary>Set MAUI content (XAML views) to render on the secondary display.</summary>
    void SetContent(View content);

    /// <summary>Show the secondary display window (or make the content visible).</summary>
    void Show();

    /// <summary>Hide the secondary display window (or collapse the content).</summary>
    void Hide();
}
