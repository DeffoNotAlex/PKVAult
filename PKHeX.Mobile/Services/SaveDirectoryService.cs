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
    /// <summary>
    /// SAV4HGSS.Version always returns HGSS. Probe party then box 1 for a
    /// Pokémon with a native HG or SS origin to resolve the actual game.
    /// </summary>
    private static PKHeX.Core.GameVersion ResolveVersion(PKHeX.Core.SaveFile sav)
    {
        if (sav.Version != PKHeX.Core.GameVersion.HGSS) return sav.Version;
        var probe = sav.PartyData.FirstOrDefault(p => p.Species > 0 &&
                        (p.Version == PKHeX.Core.GameVersion.HG || p.Version == PKHeX.Core.GameVersion.SS))
                 ?? sav.GetBoxData(0).FirstOrDefault(p => p.Species > 0 &&
                        (p.Version == PKHeX.Core.GameVersion.HG || p.Version == PKHeX.Core.GameVersion.SS));
        return probe?.Version ?? PKHeX.Core.GameVersion.HGSS;
    }

    private const string PrefKey     = "watched_dirs";
    private const string FilePrefKey = "watched_files";
    private const char   Sep         = '|';

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

    public List<string> GetWatchedFiles()
    {
        var raw = Preferences.Default.Get(FilePrefKey, "");
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return [.. raw.Split(Sep, StringSplitOptions.RemoveEmptyEntries)];
    }

    public void AddFile(string uri)
    {
        var files = GetWatchedFiles();
        if (files.Contains(uri)) return;
        files.Add(uri);
        Preferences.Default.Set(FilePrefKey, string.Join(Sep, files));
    }

    public void RemoveFile(string uri)
    {
        var files = GetWatchedFiles();
        files.Remove(uri);
        Preferences.Default.Set(FilePrefKey, string.Join(Sep, files));
    }

    public async Task<List<SaveEntry>> ScanAllAsync()
    {
        var result = new List<SaveEntry>();
        foreach (var dir in GetWatchedDirectories())
            result.AddRange(await ScanDirectoryAsync(dir));
        foreach (var file in GetWatchedFiles())
        {
            var entry = await ScanFileAsync(file);
            if (entry != null) result.Add(entry);
        }
        return result;
    }

    public async Task<SaveEntry?> ScanFileAsync(string fileUri)
    {
#if ANDROID
        return await Task.Run(() =>
        {
            try
            {
                var context = Android.App.Application.Context;
                var uri = global::Android.Net.Uri.Parse(fileUri);
                if (uri == null) return null;

                using var stream = context.ContentResolver?.OpenInputStream(uri);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var data = ms.ToArray();
                if (data.Length == 0) return null;

                // Copy before TryGetSaveFile: Switch (Gen 8/9) saves are decrypted
                // in-place by SwishCrypto, which would corrupt RawData for re-parsing.
                var rawData = data.ToArray();
                if (!PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var sav)) return null;

                // Query display name
                string[] proj = [global::Android.Provider.IOpenableColumns.DisplayName];
                var name = "";
                using var cursor = context.ContentResolver?.Query(uri, proj, null, null, null);
                if (cursor != null && cursor.MoveToFirst())
                    name = cursor.GetString(0) ?? "";

                return new SaveEntry(
                    FileUri: fileUri,
                    FileName: name,
                    DirectoryUri: fileUri,
                    Version: ResolveVersion(sav),
                    Generation: sav.Generation,
                    TrainerName: sav.OT,
                    TrainerID: $"{sav.TID16}/{sav.SID16}",
                    PlayTime: sav.PlayTimeString,
                    BoxCount: sav.BoxCount,
                    SlotCount: sav.SlotCount,
                    RawData: rawData);
            }
            catch { return null; }
        });
#else
        return await Task.Run(() =>
        {
            try
            {
                var data = File.ReadAllBytes(fileUri);
                var rawData = data.ToArray();
                if (!PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var sav)) return null;
                return new SaveEntry(
                    FileUri: fileUri,
                    FileName: Path.GetFileName(fileUri),
                    DirectoryUri: fileUri,
                    Version: ResolveVersion(sav),
                    Generation: sav.Generation,
                    TrainerName: sav.OT,
                    TrainerID: $"{sav.TID16}/{sav.SID16}",
                    PlayTime: sav.PlayTimeString,
                    BoxCount: sav.BoxCount,
                    SlotCount: sav.SlotCount,
                    RawData: rawData);
            }
            catch { return null; }
        });
#endif
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
                if (ext is not ("" or ".sav" or ".srm" or ".bin" or ".dat" or ".gci" or ".dsv" or ".bak" or ".main")) continue;

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

                    var rawData = data.ToArray();
                    if (!PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var sav)) continue;

                    entries.Add(new SaveEntry(
                        FileUri: fileUri.ToString()!,
                        FileName: name,
                        DirectoryUri: dirUri,
                        Version: ResolveVersion(sav),
                        Generation: sav.Generation,
                        TrainerName: sav.OT,
                        TrainerID: $"{sav.TID16}/{sav.SID16}",
                        PlayTime: sav.PlayTimeString,
                        BoxCount: sav.BoxCount,
                        SlotCount: sav.SlotCount,
                        RawData: rawData));
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
                if (ext is not ("" or ".sav" or ".srm" or ".bin" or ".dat" or ".gci" or ".dsv" or ".bak" or ".main")) continue;
                try
                {
                    var data = File.ReadAllBytes(filePath);
                    var rawData = data.ToArray();
                    if (!PKHeX.Core.SaveUtil.TryGetSaveFile(data, out var sav)) continue;
                    entries.Add(new SaveEntry(
                        FileUri: filePath,
                        FileName: Path.GetFileName(filePath),
                        DirectoryUri: dirUri,
                        Version: ResolveVersion(sav),
                        Generation: sav.Generation,
                        TrainerName: sav.OT,
                        TrainerID: $"{sav.TID16}/{sav.SID16}",
                        PlayTime: sav.PlayTimeString,
                        BoxCount: sav.BoxCount,
                        SlotCount: sav.SlotCount,
                        RawData: rawData));
                }
                catch { }
            }
        });
#endif
        return entries;
    }
}
