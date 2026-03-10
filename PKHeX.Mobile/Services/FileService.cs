namespace PKHeX.Mobile.Services;

/// <summary>
/// MAUI implementation of <see cref="IFileService"/> using the platform file picker and share sheet.
/// </summary>
public sealed class FileService : IFileService
{
    public async Task<(Memory<byte> Data, string FileName)?> PickFileAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a Pokémon save file",
        });

        if (result is null)
            return null;

        await using var stream = await result.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        return (ms.ToArray(), result.FileName);
    }

    public async Task ExportFileAsync(byte[] data, string fileName)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, data);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export save file",
            File = new ShareFile(path),
        });
    }
}
