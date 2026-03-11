namespace PKHeX.Mobile.Services;

public interface ISaveFilePicker
{
    /// <summary>Returns a content URI for a single picked file, or null if cancelled.</summary>
    Task<string?> PickFileAsync();
}
