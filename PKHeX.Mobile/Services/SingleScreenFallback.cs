namespace PKHeX.Mobile.Services;

/// <summary>
/// Fallback implementation for single-screen devices.
/// Content is rendered in the top row of the page's Grid layout (the existing behavior).
/// Pages own their top-screen content directly; this service is a no-op pass-through.
/// </summary>
public sealed class SingleScreenFallback : ISecondaryDisplay
{
    /// <summary>Always false — no physical secondary display.</summary>
    public bool IsAvailable => false;

    private View? _content;
    private bool _visible = true;

    public void SetContent(View content)
    {
        _content = content;
        content.IsVisible = _visible;
    }

    public void Show()
    {
        _visible = true;
        if (_content is not null)
            _content.IsVisible = true;
    }

    public void Hide()
    {
        _visible = false;
        if (_content is not null)
            _content.IsVisible = false;
    }
}
