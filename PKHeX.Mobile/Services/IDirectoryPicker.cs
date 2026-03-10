namespace PKHeX.Mobile.Services;

public interface IDirectoryPicker
{
    /// <summary>Returns a URI (tree URI on Android, path elsewhere), or null if cancelled.</summary>
    Task<string?> PickDirectoryAsync();
}
