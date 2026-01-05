#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports AchievementEntrySO assets from a CSV file and rebuilds AchievementLibrarySO.
/// Place this script under Assets/Editor/
///
/// Expected CSV headers (case-insensitive):
/// id, displayName, description, trigger, goal,
/// useTypeFilter, typeFilter,
/// useResourceFilter, resourceFilter,
/// secretUntilUnlocked,
/// iconAssetPath,
/// category (optional; ignored by importer)
///
/// Trigger must match AchievementTrigger enum names.
/// typeFilter must match MonsterType enum names.
/// resourceFilter must match ResourceType enum names.
/// </summary>
public static class AchievementCsvImporter
{
    private const string DefaultEntriesFolder = "Assets/Data/Achievements/Entries";
    private const string DefaultLibraryPath   = "Assets/Resources/Achievements/AchievementLibrary.asset";

    [MenuItem("Bitlings/Achievements/Import CSV (Create/Update SOs)")]
    public static void ImportCsvMenu()
    {
        string csvPath = EditorUtility.OpenFilePanel("Select Achievements CSV", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(csvPath))
            return;

        string entriesFolder = EditorUtility.SaveFolderPanel("Select Output Folder for AchievementEntrySO Assets",
            Application.dataPath, "Achievements_Entries");

        // If user cancels folder dialog, fall back to default path under Assets/
        if (string.IsNullOrEmpty(entriesFolder))
            entriesFolder = AbsoluteToAssetPath(DefaultEntriesFolder);
        else
            entriesFolder = AbsoluteToAssetPath(entriesFolder);

        if (string.IsNullOrEmpty(entriesFolder) || !entriesFolder.StartsWith("Assets", StringComparison.Ordinal))
        {
            EditorUtility.DisplayDialog("Import Failed",
                "Output folder must be inside the project's Assets/ directory.", "OK");
            return;
        }

        try
        {
            Import(csvPath, entriesFolder, DefaultLibraryPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AchievementCsvImporter] Import failed: {ex}");
            EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK");
        }
    }

    public static void Import(string csvAbsolutePath, string entriesFolderAssetPath, string libraryAssetPath)
    {
        if (!File.Exists(csvAbsolutePath))
            throw new FileNotFoundException("CSV file not found.", csvAbsolutePath);

        EnsureFolder(entriesFolderAssetPath);
        EnsureFolder(Path.GetDirectoryName(libraryAssetPath)?.Replace("\\", "/"));

        string[] lines = File.ReadAllLines(csvAbsolutePath, Encoding.UTF8);
        if (lines.Length < 2)
            throw new InvalidDataException("CSV must contain a header row and at least one data row.");

        // Parse header
        var header = ParseCsvLine(lines[0]);
        var col = BuildColumnMap(header);

        RequireColumn(col, "id");
        RequireColumn(col, "displayname");
        RequireColumn(col, "description");
        RequireColumn(col, "trigger");
        RequireColumn(col, "goal");

        // Optional columns
        bool hasUseType     = col.ContainsKey("usetypefilter");
        bool hasTypeFilter  = col.ContainsKey("typefilter");
        bool hasUseRes      = col.ContainsKey("useresourcefilter");
        bool hasResFilter   = col.ContainsKey("resourcefilter");
        bool hasSecret      = col.ContainsKey("secretuntilunlocked");
        bool hasIconPath    = col.ContainsKey("iconassetpath");

        var createdOrUpdated = new List<AchievementEntrySO>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int updated = 0;
        int created = 0;
        int skipped = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            string raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.TrimStart().StartsWith("#")) continue;

            var fields = ParseCsvLine(raw);

            string id = Get(fields, col, "id")?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                skipped++;
                Debug.LogWarning($"[AchievementCsvImporter] Row {i + 1}: missing id. Skipped.");
                continue;
            }

            if (!seenIds.Add(id))
            {
                skipped++;
                Debug.LogWarning($"[AchievementCsvImporter] Row {i + 1}: duplicate id '{id}'. Skipped.");
                continue;
            }

            string displayName = Get(fields, col, "displayname") ?? string.Empty;
            string description = Get(fields, col, "description") ?? string.Empty;
            string triggerStr  = Get(fields, col, "trigger") ?? string.Empty;
            string goalStr     = Get(fields, col, "goal") ?? "1";

            if (!int.TryParse(goalStr, out int goal))
                goal = 1;
            goal = Mathf.Max(1, goal);

            if (!TryParseEnum(triggerStr, out AchievementTrigger trigger))
            {
                skipped++;
                Debug.LogWarning($"[AchievementCsvImporter] Row {i + 1} ({id}): unknown trigger '{triggerStr}'. Skipped.");
                continue;
            }

            bool useTypeFilter = hasUseType && ParseBool(Get(fields, col, "usetypefilter"));
            bool useResourceFilter = hasUseRes && ParseBool(Get(fields, col, "useresourcefilter"));

            MonsterType typeFilter = default;
            ResourceType resourceFilter = default;

            if (useTypeFilter)
            {
                string tf = hasTypeFilter ? (Get(fields, col, "typefilter") ?? "") : "";
                if (!TryParseEnum(tf, out typeFilter))
                {
                    // If type filter is required but invalid, skip row
                    skipped++;
                    Debug.LogWarning($"[AchievementCsvImporter] Row {i + 1} ({id}): invalid typeFilter '{tf}'. Skipped.");
                    continue;
                }
            }

            if (useResourceFilter)
            {
                string rf = hasResFilter ? (Get(fields, col, "resourcefilter") ?? "") : "";
                if (!TryParseEnum(rf, out resourceFilter))
                {
                    skipped++;
                    Debug.LogWarning($"[AchievementCsvImporter] Row {i + 1} ({id}): invalid resourceFilter '{rf}'. Skipped.");
                    continue;
                }
            }

            bool secret = hasSecret && ParseBool(Get(fields, col, "secretuntilunlocked"));

            string iconAssetPath = hasIconPath ? (Get(fields, col, "iconassetpath") ?? "") : "";
            iconAssetPath = iconAssetPath.Trim();

            // Create or update asset
            string safeId = SanitizeFileName(id);
            string assetPath = $"{entriesFolderAssetPath}/Achievement_{safeId}.asset";

            var entry = AssetDatabase.LoadAssetAtPath<AchievementEntrySO>(assetPath);
            bool isNew = false;

            if (entry == null)
            {
                entry = ScriptableObject.CreateInstance<AchievementEntrySO>();
                AssetDatabase.CreateAsset(entry, assetPath);
                isNew = true;
                created++;
            }
            else
            {
                updated++;
            }

            entry.id = id;
            entry.displayName = displayName;
            entry.description = description;
            entry.trigger = trigger;
            entry.goal = goal;

            entry.useTypeFilter = useTypeFilter;
            if (useTypeFilter) entry.typeFilter = typeFilter;

            entry.useResourceFilter = useResourceFilter;
            if (useResourceFilter) entry.resourceFilter = resourceFilter;

            entry.secretUntilUnlocked = secret;

            // Icon: only overwrite if CSV provides a non-empty valid path
            if (!string.IsNullOrEmpty(iconAssetPath))
            {
                var spr = AssetDatabase.LoadAssetAtPath<Sprite>(iconAssetPath);
                if (spr != null)
                {
                    entry.icon = spr;
                }
                else
                {
                    Debug.LogWarning($"[AchievementCsvImporter] Row {i + 1} ({id}): icon not found at '{iconAssetPath}'. Keeping existing icon.");
                }
            }

            EditorUtility.SetDirty(entry);
            createdOrUpdated.Add(entry);

            // Ensure asset has the correct name in Project view
            if (isNew)
                entry.name = $"Achievement_{safeId}";
        }

        // Load or create library
        var library = AssetDatabase.LoadAssetAtPath<AchievementLibrarySO>(libraryAssetPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<AchievementLibrarySO>();
            AssetDatabase.CreateAsset(library, libraryAssetPath);
        }

        // Rebuild library list (stable by id)
        createdOrUpdated.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        library.entries = new List<AchievementEntrySO>(createdOrUpdated);
        EditorUtility.SetDirty(library);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Achievements Import Complete",
            $"Created: {created}\nUpdated: {updated}\nSkipped: {skipped}\n\nEntries folder:\n{entriesFolderAssetPath}\n\nLibrary:\n{libraryAssetPath}",
            "OK");

        Debug.Log($"[AchievementCsvImporter] Import complete. Created={created}, Updated={updated}, Skipped={skipped}. Library rebuilt with {createdOrUpdated.Count} entries.");
    }

    // ─────────────────────────────────────────────────────────────
    // CSV helpers
    // ─────────────────────────────────────────────────────────────

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '\"')
            {
                // Escaped quote
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                {
                    sb.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Length = 0;
                continue;
            }

            sb.Append(c);
        }

        result.Add(sb.ToString());
        return result;
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            string key = (header[i] ?? "").Trim();
            if (string.IsNullOrEmpty(key)) continue;

            key = key.Replace(" ", "").ToLowerInvariant();
            if (!map.ContainsKey(key))
                map.Add(key, i);
        }
        return map;
    }

    private static void RequireColumn(Dictionary<string, int> col, string keyLowerNoSpaces)
    {
        if (!col.ContainsKey(keyLowerNoSpaces))
            throw new InvalidDataException($"CSV missing required column: {keyLowerNoSpaces}");
    }

    private static string Get(List<string> fields, Dictionary<string, int> col, string keyLowerNoSpaces)
    {
        if (!col.TryGetValue(keyLowerNoSpaces, out int idx)) return null;
        if (idx < 0 || idx >= fields.Count) return null;
        return fields[idx];
    }

    private static bool ParseBool(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim().ToLowerInvariant();
        return s == "true" || s == "1" || s == "yes" || s == "y";
    }

    private static bool TryParseEnum<T>(string s, out T value) where T : struct
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            value = default;
            return false;
        }

        return Enum.TryParse(s.Trim(), ignoreCase: true, out value);
    }

    private static void EnsureFolder(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return;
        assetPath = assetPath.Replace("\\", "/");

        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        // Build folders recursively
        string[] parts = assetPath.Split('/');
        if (parts.Length == 0) return;

        string cur = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private static string AbsoluteToAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        path = path.Replace("\\", "/");

        // If already an asset path
        if (path.StartsWith("Assets/", StringComparison.Ordinal) || path == "Assets")
            return path;

        // Convert absolute path under project to Assets/...
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName?.Replace("\\", "/");
        if (string.IsNullOrEmpty(projectRoot)) return null;

        if (path.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            string rel = path.Substring(projectRoot.Length).TrimStart('/');
            if (!rel.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                rel = "Assets/" + rel;
            return rel.Replace("\\", "/");
        }

        return null;
    }

    private static string SanitizeFileName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "UNNAMED";

        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');

        // Also avoid slashes/colons etc
        s = s.Replace("/", "_").Replace("\\", "_").Replace(":", "_");

        return s;
    }
}
#endif