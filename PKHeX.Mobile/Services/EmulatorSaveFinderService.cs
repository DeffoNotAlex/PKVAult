namespace PKHeX.Mobile.Services;

/// <summary>
/// Scans for Pokémon save files stored by Android emulators using SAF (Storage Access Framework).
/// Supports Eden (Switch), MelonDS (DS), and Azahar (3DS export folders).
/// </summary>
public static class EmulatorSaveFinderService
{
    // Known Pokémon Switch title IDs (case-insensitive match against Eden directory names)
    private static readonly Dictionary<string, string> PokemonSwitchGames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "010003F003A34000", "Let's Go Pikachu" },
            { "0100187003A36000", "Let's Go Eevee"   },
            { "0100ABF008968000", "Sword"             },
            { "01008DB008C2C000", "Shield"            },
            { "0100000011D90000", "Brilliant Diamond" },
            { "010018F003A18000", "Shining Pearl"     },
            { "01001F5010B28000", "Legends: Arceus"   },
            { "0100A3D008C5C000", "Scarlet"           },
            { "01008F6008C5E000", "Violet"            },
        };

    private static string SwitchGameName(string titleId) =>
        PokemonSwitchGames.TryGetValue(titleId, out var n) ? n : titleId;

    /// <summary>
    /// Scans an Eden emulator "files" root folder for Pokémon Switch saves.
    /// Tree: root/nand/user/save/0/&lt;uid_h&gt;/&lt;uid_l&gt;/&lt;title_id&gt;/main
    /// </summary>
    /// <param name="rootTreeUri">SAF tree URI the user granted (pointing at the eden/files level).</param>
    /// <returns>List of (content URI of "main" file, human-readable game name).</returns>
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

                // Walk the deterministic prefix: nand → user → save → 0
                var saveZeroDocId = NavigatePath(resolver, treeUri, rootDocId,
                    ["nand", "user", "save", "0"]);
                if (saveZeroDocId == null) return results;

                // Enumerate uid_high / uid_low / title_id
                foreach (var (hiDocId, _) in ListChildren(resolver, treeUri, saveZeroDocId))
                foreach (var (loDocId, _) in ListChildren(resolver, treeUri, hiDocId))
                foreach (var (titleDocId, titleName) in ListChildren(resolver, treeUri, loDocId))
                {
                    if (!PokemonSwitchGames.ContainsKey(titleName)) continue;

                    var mainDocId = FindChildDocId(resolver, treeUri, titleDocId, "main");
                    if (mainDocId == null) continue;

                    var mainUri = global::Android.Provider.DocumentsContract
                        .BuildDocumentUriUsingTree(treeUri, mainDocId);
                    if (mainUri == null) continue;

                    results.Add((mainUri.ToString()!, SwitchGameName(titleName)));
                }
            }
            catch { /* permission denied or unexpected structure — return what we have */ }
#endif
            return results;
        });
    }

    /// <summary>
    /// Scans an Azahar (Citra) emulator root folder for 3DS Pokémon save files.
    /// Tree: root/sdmc/Nintendo 3DS/&lt;ID0&gt;/&lt;ID1&gt;/title/00040000/&lt;game_id&gt;/data/00000001/main
    /// ID0 and ID1 are random hex dirs — enumerated automatically.
    /// All files found under 00040000 are tried against SaveUtil; non-Pokémon saves are silently ignored.
    /// </summary>
    /// <param name="rootTreeUri">SAF tree URI the user granted (pointing at the Azahar root, i.e. the folder that contains sdmc/).</param>
    /// <returns>List of (content URI of save file, "Azahar save") pairs that parsed successfully.</returns>
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

                // Navigate: sdmc → Nintendo 3DS
                var n3dsDocId = NavigatePath(resolver, treeUri, rootDocId, ["sdmc", "Nintendo 3DS"]);
                if (n3dsDocId == null) return results;

                // Enumerate ID0 → ID1 → title/00040000
                foreach (var (id0DocId, _) in ListChildren(resolver, treeUri, n3dsDocId))
                foreach (var (id1DocId, _) in ListChildren(resolver, treeUri, id0DocId))
                {
                    var retailDocId = NavigatePath(resolver, treeUri, id1DocId, ["title", "00040000"]);
                    if (retailDocId == null) continue;

                    // Enumerate all game title directories under 00040000
                    foreach (var (gameDocId, _) in ListChildren(resolver, treeUri, retailDocId))
                    {
                        // Navigate data/00000001
                        var dataDocId = NavigatePath(resolver, treeUri, gameDocId, ["data", "00000001"]);
                        if (dataDocId == null) continue;

                        // The save file is named "main" (no extension)
                        var saveDocId = FindChildDocId(resolver, treeUri, dataDocId, "main");
                        if (saveDocId == null) continue;

                        var saveUri = global::Android.Provider.DocumentsContract
                            .BuildDocumentUriUsingTree(treeUri, saveDocId);
                        if (saveUri == null) continue;

                        // Attempt to parse — silently skip non-Pokémon titles
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
                        catch { /* skip unreadable or non-Pokémon */ }
                    }
                }
            }
            catch { }
#endif
            return results;
        });
    }

    // ── Android SAF helpers ───────────────────────────────────────────────────

#if ANDROID
    /// <summary>
    /// Walks a SAF tree following a fixed sequence of child names.
    /// Returns the final document ID, or null if any segment is not found.
    /// </summary>
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

    /// <summary>Returns the document ID of the first child whose display name equals <paramref name="childName"/>.</summary>
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

    /// <summary>Returns (docId, displayName) pairs for all children of <paramref name="parentDocId"/>.</summary>
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
