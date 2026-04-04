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

    public async Task WriteBackAsync(byte[] data, string fileUri)
    {
#if ANDROID
        await Task.Run(() =>
        {
            var context = Android.App.Application.Context;
            var uri = global::Android.Net.Uri.Parse(fileUri)
                ?? throw new InvalidOperationException("Invalid file URI.");
            using var stream = context.ContentResolver?.OpenOutputStream(uri, "wt")
                ?? throw new InvalidOperationException("Could not open output stream.");
            stream.Write(data, 0, data.Length);
            stream.Flush();
        });
#else
        await File.WriteAllBytesAsync(fileUri, data);
#endif
    }
}
