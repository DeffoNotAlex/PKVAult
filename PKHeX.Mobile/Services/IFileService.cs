namespace PKHeX.Mobile.Services;

/// <summary>
/// Abstracts platform-specific file I/O so PKHeX.Core never depends on Android APIs.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Opens the platform file picker and returns the selected file's bytes and name.
    /// Returns null if the user cancelled.
    /// </summary>
    Task<(Memory<byte> Data, string FileName)?> PickFileAsync();

    /// <summary>
    /// Exports data to the platform share sheet under the given file name.
    /// </summary>
    Task ExportFileAsync(byte[] data, string fileName);
}
