namespace PKHeX.Mobile.Services;

public record SaveEntry(
    string FileUri,
    string FileName,
    string DirectoryUri,
    PKHeX.Core.GameVersion Version,
    int Generation,
    string TrainerName,
    string TrainerID,
    string PlayTime,
    int BoxCount,
    int SlotCount,
    byte[] RawData);

public class SaveDirectoryService
{
    private const string PrefKey = "watched_dirs";
    private const char Sep = '|';

    public List<string> GetWatchedDirectories()
    {
        var raw = Preferences.Default.Get(PrefKey, "");
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return [.. raw.Split(Sep, StringSplitOptions.RemoveEmptyEntries)];
    }

    public void AddDirectory(string uri)
    {
        var dirs = GetWatchedDirectories();
        if (dirs.Contains(uri)) return;
        dirs.Add(uri);
        Preferences.Default.Set(PrefKey, string.Join(Sep, dirs));
    }

    public void RemoveDirectory(string uri)
    {
        var dirs = GetWatchedDirectories();
        dirs.Remove(uri);
        Preferences.Default.Set(PrefKey, string.Join(Sep, dirs));
    }

    public async Task<List<SaveEntry>> ScanAllAsync()
    {
        var result = new List<SaveEntry>();
        foreach (var dir in GetWatchedDirectories())
            result.AddRange(await ScanDirectoryAsync(dir));
        return result;
    }

    public async Task<List<SaveEntry>> ScanDirectoryAsync(string dirUri)
    {
        var entries = new List<SaveEntry>();
#if ANDROID
        await Task.Run(() =>
        {
            var context = Android.App.Application.Context;
            var treeUri = Android.Net.Uri.Parse(dirUri);
            var docDir = Android.Provider.DocumentFile.FromTreeUri(context, treeUri);
            if (docDir == null || !docDir.IsDirectory) return;

            foreach (var file in docDir.ListFiles())
            {
                if (file == null || !file.IsFile || file.Uri == null) continue;
                var name = file.Name ?? "";
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not ("" or ".sav" or ".bin" or ".dat" or ".gci" or ".dsv" or ".bak" or ".main")) continue;

                try
                {
                    using var stream = context.ContentResolver?.OpenInputStream(file.Uri);
                    if (stream == null) continue;
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var data = ms.ToArray();
                    if (data.Length == 0) continue;

                    if (!PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var sav)) continue;

                    entries.Add(new SaveEntry(
                        FileUri: file.Uri.ToString(),
                        FileName: name,
                        DirectoryUri: dirUri,
                        Version: sav.Version,
                        Generation: sav.Generation,
                        TrainerName: sav.OT,
                        TrainerID: $"{sav.TID16}/{sav.SID16}",
                        PlayTime: sav.PlayTimeString,
                        BoxCount: sav.BoxCount,
                        SlotCount: sav.SlotCount,
                        RawData: data));
                }
                catch { /* skip unreadable files */ }
            }
        });
#else
        await Task.Run(() =>
        {
            if (!Directory.Exists(dirUri)) return;
            foreach (var filePath in Directory.EnumerateFiles(dirUri))
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext is not ("" or ".sav" or ".bin" or ".dat" or ".gci" or ".dsv" or ".bak" or ".main")) continue;
                try
                {
                    var data = File.ReadAllBytes(filePath);
                    if (!PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var sav)) continue;
                    entries.Add(new SaveEntry(
                        FileUri: filePath,
                        FileName: Path.GetFileName(filePath),
                        DirectoryUri: dirUri,
                        Version: sav.Version,
                        Generation: sav.Generation,
                        TrainerName: sav.OT,
                        TrainerID: $"{sav.TID16}/{sav.SID16}",
                        PlayTime: sav.PlayTimeString,
                        BoxCount: sav.BoxCount,
                        SlotCount: sav.SlotCount,
                        RawData: data));
                }
                catch { }
            }
        });
#endif
        return entries;
    }
}
