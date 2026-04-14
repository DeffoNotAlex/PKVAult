namespace PKHeX.Mobile.Services;

/// <summary>
/// Scans for Pokémon save files stored by Android emulators using SAF (Storage Access Framework).
/// Supports Eden (Switch), MelonDS (DS), and Azahar (3DS export folders).
/// </summary>
public static class EmulatorSaveFinderService
{
    // Known Pokémon Switch title IDs — used only for display names.
    private static readonly Dictionary<string, string> PokemonSwitchGames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "010003F003A34000", "Let's Go Pikachu" },
            { "0100187003A36000", "Let's Go Eevee"   },
            { "0100ABF008968000", "Sword"             },
            { "01008DB008C2C000", "Shield"            },
            { "0100000011D90000", "Brilliant Diamond" },
            { "010018E011D92000", "Shining Pearl"     },
            { "01001F5010B28000", "Legends: Arceus"   },
            { "0100A3D008C5C000", "Scarlet"           },
            { "01008F6008C5E000", "Violet"            },
        };

    /// <summary>
    /// Scans an Eden emulator "files" root folder for Pokémon Switch saves.
    /// Navigates to nand/user/save/ then recursively searches for files named
    /// "main", trying SaveUtil on each one. No assumptions about uid/title layout.
    /// </summary>
    public static async Task<List<(string FileUri, string GameName)>> ScanEdenAsync(string rootTreeUri)
    {
        return await Task.Run(() =>
        {
            var results = new List<(string, string)>();
#if ANDROID
            try
            {
                var context  = global::Android.App.Application.Context;
                var resolver = context.ContentResolver;
                if (resolver == null) return results;

                var treeUri = global::Android.Net.Uri.Parse(rootTreeUri);
                if (treeUri == null) return results;

                var rootDocId = global::Android.Provider.DocumentsContract.GetTreeDocumentId(treeUri);
                if (rootDocId == null) return results;

                // Navigate to the save directory. Try several path variants because the
                // user may have picked the 'files/' root, the 'nand/' folder, or 'user/'.
                string[][] candidates =
                [
                    ["nand", "user", "save"],   // root = eden files/
                    ["user", "save"],            // root = eden files/nand/
                    ["save"],                    // root = eden files/nand/user/
                ];
                string? saveDocId = null;
                foreach (var path in candidates)
                {
                    saveDocId = NavigatePath(resolver, treeUri, rootDocId, path);
                    if (saveDocId != null) break;
                }
                if (saveDocId == null) return results;

                // Collect every "main" or "*.bin" file found up to 8 directories deep.
                // BDSP/BD saves may appear as .bin rather than the standard extensionless "main".
                var mainDocIds = new List<string>();
                FindMatchingFiles(resolver, treeUri, saveDocId,
                    name => name == "main" || name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase),
                    8, mainDocIds);

                // Try SaveUtil on each candidate
                foreach (var mainDocId in mainDocIds)
                {
                    var mainUri = global::Android.Provider.DocumentsContract
                        .BuildDocumentUriUsingTree(treeUri, mainDocId);
                    if (mainUri == null) continue;

                    try
                    {
                        using var stream = resolver.OpenInputStream(mainUri);
                        if (stream == null) continue;
                        using var ms = new System.IO.MemoryStream();
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length == 0 || !PKHeX.Core.SaveUtil.TryGetSaveFile(data, out _))
                            continue;

                        // Try to extract a friendly name from the parent directory name
                        // (the title ID folder), falling back to "Switch save"
                        var gameName = GuessEdenGameName(mainDocId);
                        results.Add((mainUri.ToString()!, gameName));
                    }
                    catch { }
                }
            }
            catch { }
#endif
            return results;
        });
    }

    /// <summary>
    /// Scans an Azahar (Citra) emulator root folder for 3DS Pokémon save files.
    /// Tree: root/sdmc/Nintendo 3DS/&lt;ID0&gt;/&lt;ID1&gt;/title/00040000/&lt;game_id&gt;/data/00000001/main
    /// </summary>
    public static async Task<List<(string FileUri, string GameName)>> ScanAzaharAsync(string rootTreeUri)
    {
        return await Task.Run(() =>
        {
            var results = new List<(string, string)>();
#if ANDROID
            try
            {
                var context  = global::Android.App.Application.Context;
                var resolver = context.ContentResolver;
                if (resolver == null) return results;

                var treeUri = global::Android.Net.Uri.Parse(rootTreeUri);
                if (treeUri == null) return results;

                var rootDocId = global::Android.Provider.DocumentsContract.GetTreeDocumentId(treeUri);
                if (rootDocId == null) return results;

                var n3dsDocId = NavigatePath(resolver, treeUri, rootDocId, ["sdmc", "Nintendo 3DS"]);
                if (n3dsDocId == null) return results;

                foreach (var (id0DocId, _) in ListChildren(resolver, treeUri, n3dsDocId))
                foreach (var (id1DocId, _) in ListChildren(resolver, treeUri, id0DocId))
                {
                    var retailDocId = NavigatePath(resolver, treeUri, id1DocId, ["title", "00040000"]);
                    if (retailDocId == null) continue;

                    foreach (var (gameDocId, _) in ListChildren(resolver, treeUri, retailDocId))
                    {
                        var dataDocId = NavigatePath(resolver, treeUri, gameDocId, ["data", "00000001"]);
                        if (dataDocId == null) continue;

                        var saveDocId = FindChildDocId(resolver, treeUri, dataDocId, "main");
                        if (saveDocId == null) continue;

                        var saveUri = global::Android.Provider.DocumentsContract
                            .BuildDocumentUriUsingTree(treeUri, saveDocId);
                        if (saveUri == null) continue;

                        try
                        {
                            using var stream = resolver.OpenInputStream(saveUri);
                            if (stream == null) continue;
                            using var ms = new System.IO.MemoryStream();
                            stream.CopyTo(ms);
                            var data = ms.ToArray();
                            if (data.Length > 0 && PKHeX.Core.SaveUtil.TryGetSaveFile(data, out _))
                                results.Add((saveUri.ToString()!, "Azahar save"));
                        }
                        catch { }
                    }
                }
            }
            catch { }
#endif
            return results;
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to derive a game name from the document ID of a "main" file.
    /// The document ID for external storage is path-based, so the parent segment
    /// is likely the title ID directory name.
    /// </summary>
    private static string GuessEdenGameName(string mainDocId)
    {
        // docId looks like "primary:Android/data/.../nand/user/save/0/uid/uid/0100ABF008968000/main"
        // Walk backwards to find the first segment that matches a known title ID.
        var parts = mainDocId.Replace('\\', '/').Split('/');
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            if (PokemonSwitchGames.TryGetValue(parts[i], out var name))
                return name;
        }
        return "Switch save";
    }

    // ── Android SAF helpers ───────────────────────────────────────────────────

#if ANDROID
    /// <summary>
    /// Recursively finds all files matching <paramref name="match"/> under a document tree node,
    /// up to <paramref name="maxDepth"/> directory levels deep.
    /// </summary>
    private static void FindMatchingFiles(
        global::Android.Content.ContentResolver resolver,
        global::Android.Net.Uri treeUri,
        string parentDocId,
        Func<string, bool> match,
        int maxDepth,
        List<string> results)
    {
        if (maxDepth <= 0) return;

        var childrenUri = global::Android.Provider.DocumentsContract
            .BuildChildDocumentsUriUsingTree(treeUri, parentDocId);
        if (childrenUri == null) return;

        string[] projection =
        [
            global::Android.Provider.DocumentsContract.Document.ColumnDocumentId,
            global::Android.Provider.DocumentsContract.Document.ColumnDisplayName,
            global::Android.Provider.DocumentsContract.Document.ColumnMimeType,
        ];

        using var cursor = resolver.Query(childrenUri, projection, null, null, null);
        if (cursor == null) return;

        while (cursor.MoveToNext())
        {
            var docId    = cursor.GetString(0);
            var name     = cursor.GetString(1);
            var mimeType = cursor.GetString(2);
            if (docId == null || name == null) continue;

            bool isDir = mimeType == global::Android.Provider.DocumentsContract.Document.MimeTypeDir;

            if (!isDir && match(name))
                results.Add(docId);
            else if (isDir)
                FindMatchingFiles(resolver, treeUri, docId, match, maxDepth - 1, results);
        }
    }

    private static string? NavigatePath(
        global::Android.Content.ContentResolver resolver,
        global::Android.Net.Uri treeUri,
        string rootDocId,
        string[] path)
    {
        var current = rootDocId;
        foreach (var segment in path)
        {
            current = FindChildDocId(resolver, treeUri, current, segment)!;
            if (current == null) return null;
        }
        return current;
    }

    private static string? FindChildDocId(
        global::Android.Content.ContentResolver resolver,
        global::Android.Net.Uri treeUri,
        string parentDocId,
        string childName)
    {
        var childrenUri = global::Android.Provider.DocumentsContract
            .BuildChildDocumentsUriUsingTree(treeUri, parentDocId);
        if (childrenUri == null) return null;

        string[] projection =
        [
            global::Android.Provider.DocumentsContract.Document.ColumnDocumentId,
            global::Android.Provider.DocumentsContract.Document.ColumnDisplayName,
        ];

        using var cursor = resolver.Query(childrenUri, projection, null, null, null);
        if (cursor == null) return null;

        while (cursor.MoveToNext())
        {
            if (cursor.GetString(1) == childName)
                return cursor.GetString(0);
        }
        return null;
    }

    private static List<(string DocId, string Name)> ListChildren(
        global::Android.Content.ContentResolver resolver,
        global::Android.Net.Uri treeUri,
        string parentDocId)
    {
        var result = new List<(string, string)>();
        var childrenUri = global::Android.Provider.DocumentsContract
            .BuildChildDocumentsUriUsingTree(treeUri, parentDocId);
        if (childrenUri == null) return result;

        string[] projection =
        [
            global::Android.Provider.DocumentsContract.Document.ColumnDocumentId,
            global::Android.Provider.DocumentsContract.Document.ColumnDisplayName,
        ];

        using var cursor = resolver.Query(childrenUri, projection, null, null, null);
        if (cursor == null) return result;

        while (cursor.MoveToNext())
        {
            var docId = cursor.GetString(0);
            var name  = cursor.GetString(1);
            if (docId != null && name != null)
                result.Add((docId, name));
        }
        return result;
    }
#endif
}
