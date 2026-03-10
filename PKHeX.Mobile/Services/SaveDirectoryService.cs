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
            var treeUri = global::Android.Net.Uri.Parse(dirUri);
            if (treeUri == null) return;

            var treeDocId = global::Android.Provider.DocumentsContract.GetTreeDocumentId(treeUri);
            if (treeDocId == null) return;

            var childrenUri = global::Android.Provider.DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, treeDocId);
            if (childrenUri == null) return;

            string[] projection =
            [
                global::Android.Provider.DocumentsContract.Document.ColumnDocumentId,
                global::Android.Provider.DocumentsContract.Document.ColumnDisplayName,
                global::Android.Provider.DocumentsContract.Document.ColumnMimeType,
            ];

            using var cursor = context.ContentResolver?.Query(childrenUri, projection, null, null, null);
            if (cursor == null) return;

            while (cursor.MoveToNext())
            {
                var childDocId = cursor.GetString(0);
                var name      = cursor.GetString(1) ?? "";
                var mimeType  = cursor.GetString(2) ?? "";

                if (mimeType == global::Android.Provider.DocumentsContract.Document.MimeTypeDir) continue;
                if (childDocId == null) continue;

                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not ("" or ".sav" or ".bin" or ".dat" or ".gci" or ".dsv" or ".bak" or ".main")) continue;

                var fileUri = global::Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(treeUri, childDocId);
                if (fileUri == null) continue;

                try
                {
                    using var stream = context.ContentResolver?.OpenInputStream(fileUri);
                    if (stream == null) continue;
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var data = ms.ToArray();
                    if (data.Length == 0) continue;

                    if (!PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var sav)) continue;

                    entries.Add(new SaveEntry(
                        FileUri: fileUri.ToString(),
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
