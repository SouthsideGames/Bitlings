// Assets/Editor/TagTrackImporter.cs
#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class TagTrackImporter
{
    // Where to create/update track assets
    private const string OUTPUT_DIR = "Assets/TagTracks";

    // Column names expected in the CSV/JSON rows
    private static readonly string[] TagCols   = { "Tag1", "Tag2", "Tag3", "Tag4", "Tag5" };
    private static readonly string[] UnlockCols= { "Unlock1", "Unlock2", "Unlock3", "Unlock4", "Unlock5" };

    // Fallback unlock curves by rarity (used if UnlockX columns are missing/empty)
    private static readonly Dictionary<string, int[]> DefaultUnlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Common",    new[]{ 3,10 } },
        { "Uncommon",  new[]{ 2,7,13 } },
        { "Rare",      new[]{ 2,6,10,14 } },
        { "Epic",      new[]{ 2,5,8,12,15 } },
        { "Legendary", new[]{ 1,4,8,12,15 } },
        { "Mythic",    new[]{ 1,3,6,10,15 } },
    };

    // ---- MENU ----

    [MenuItem("Tools/Tags/Import Tag Tracks (CSV)...")]
    private static void ImportCSVMenu()
    {
        string path = EditorUtility.OpenFilePanel("Select Tag Tracks CSV", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(path)) return;
        ImportFromCsv(path);
    }

    [MenuItem("Tools/Tags/Import Tag Tracks (JSON)...")]
    private static void ImportJSONMenu()
    {
        string path = EditorUtility.OpenFilePanel("Select Tag Tracks JSON", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;
        ImportFromJson(path);
    }

    // ---- CORE IMPORTERS ----

    public static void ImportFromCsv(string filePath)
    {
        if (!File.Exists(filePath)) { Debug.LogError($"[TagTrackImporter] CSV not found: {filePath}"); return; }
        var rows = ParseCsv(File.ReadAllText(filePath));
        ImportRows(rows);
    }

    public static void ImportFromJson(string filePath)
    {
        if (!File.Exists(filePath)) { Debug.LogError($"[TagTrackImporter] JSON not found: {filePath}"); return; }
        string json = File.ReadAllText(filePath);
        // JSON may be an array of row objects
        var array = JsonHelper.FromJson<Row>(json);
        if (array == null || array.Length == 0)
        {
            // try strict object { ... } form (array wrapped)
            var single = JsonUtility.FromJson<Row>(json);
            if (single != null) ImportRows(new List<Row> { single });
            else Debug.LogError("[TagTrackImporter] JSON was not parseable into rows.");
            return;
        }
        ImportRows(array.ToList());
    }

    // ---- IMPORT PIPELINE ----

    private static void ImportRows(List<Row> rows)
    {
        if (rows == null || rows.Count == 0) { Debug.LogWarning("[TagTrackImporter] No rows to import."); return; }

        if (TagLibrarySO.I == null)
        {
            Debug.LogWarning("[TagTrackImporter] TagLibrarySO.I is null. Make sure TagLibraryBootstrap runs in a boot scene, or locate a TagLibrarySO asset in Project and assign it to a singleton before importing.");
        }

        // Ensure output folder exists
        if (!AssetDatabase.IsValidFolder(OUTPUT_DIR))
        {
            string parent = "Assets";
            foreach (var part in OUTPUT_DIR.Split('/').Skip(1))
            {
                string soFar = $"{parent}/{part}";
                if (!AssetDatabase.IsValidFolder(soFar))
                    AssetDatabase.CreateFolder(parent, part);
                parent = soFar;
            }
        }

        int created = 0, updated = 0, wired = 0, tagMiss = 0, monMiss = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var row in rows)
            {
                string mid   = Safe(row.MonsterID);
                if (string.IsNullOrEmpty(mid))
                {
                    Debug.LogWarning("[TagTrackImporter] Skipping row with empty MonsterID.");
                    continue;
                }

                // Create/Load track asset
                string assetPath = $"{OUTPUT_DIR}/Track_{mid}.asset";
                var track = AssetDatabase.LoadAssetAtPath<MonsterTagTrackSO>(assetPath);
                if (track == null)
                {
                    track = ScriptableObject.CreateInstance<MonsterTagTrackSO>();
                    AssetDatabase.CreateAsset(track, assetPath);
                    created++;
                }
                else updated++;

                // Populate tags
                var tagIds = ReadTagIds(row);
                var tagList = new List<TagSO>();
                foreach (var tid in tagIds)
                {
                    if (string.IsNullOrEmpty(tid)) { tagList.Add(null); continue; }
                    TagSO tag = TagLibrarySO.I ? TagLibrarySO.I.GetById(tid) : null;
                    if (tag == null)
                    {
                        tag = FindTagAssetById(tid); // fallback project-wide search by ID
                    }
                    if (tag == null)
                    {
                        tagMiss++;
                        Debug.LogWarning($"[TagTrackImporter] TagSO not found for id '{tid}' (Monster: {mid}). Ensure it exists in TagLibrarySO or in project.");
                    }
                    tagList.Add(tag);
                }

                // Populate unlock levels
                var unlocks = ReadUnlocks(row, Safe(row.Rarity), tagList.Count);

                // Apply to track
                track.maxLevel = 15;
                track.tags = tagList.ToArray();
                track.unlockLevels = unlocks;

                EditorUtility.SetDirty(track);

                // Wire to MonsterDataSO (by Monster ID)
                var monster = MonsterLibraryLocator.GetById(mid);
                if (monster != null)
                {
                    var so = new SerializedObject(monster);
                    var prop = so.FindProperty("tagTrack");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = track;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(monster);
                        wired++;
                    }
                }
                else
                {
                    monMiss++;
                    // Not fatal—maybe the monster def uses a different ID or lives in another package.
                    Debug.LogWarning($"[TagTrackImporter] MonsterDataSO not found for ID '{mid}'.");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[TagTrackImporter] Done. Created:{created} Updated:{updated} Wired:{wired} MissingTags:{tagMiss} MissingMonsters:{monMiss}");
    }

    // ---- Utilities ----

    private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

    private static List<string> ReadTagIds(Row r)
    {
        var list = new List<string>();
        foreach (var col in TagCols)
        {
            var v = Get(r, col);
            if (!string.IsNullOrEmpty(v)) list.Add(v.Trim());
        }
        return list;
    }

    private static int[] ReadUnlocks(Row r, string rarity, int tagCount)
    {
        var vals = new List<int>();
        // Prefer explicit UnlockX columns if present
        bool hadAny = false;
        foreach (var col in UnlockCols)
        {
            var s = Get(r, col);
            if (!string.IsNullOrEmpty(s))
            {
                if (int.TryParse(s, out var lv)) { vals.Add(Mathf.Max(1, lv)); hadAny = true; }
            }
        }

        if (!hadAny)
        {
            if (!DefaultUnlocks.TryGetValue(rarity ?? "", out var curve))
                curve = DefaultUnlocks["Common"];
            for (int i = 0; i < tagCount; i++)
                vals.Add(i < curve.Length ? curve[i] : curve.Last());
        }
        else
        {
            // Trim/pad to number of tags
            while (vals.Count < tagCount) vals.Add(vals.LastOrDefault() > 0 ? vals.Last() : 15);
            if (vals.Count > tagCount) vals.RemoveRange(tagCount, vals.Count - tagCount);
        }

        return vals.ToArray();
    }

    private static string Get(Row r, string fieldName)
    {
        if (r == null) return null;
        return fieldName switch
        {
            "MonsterID" => r.MonsterID,
            "MonsterName" => r.MonsterName,
            "Type" => r.Type,
            "Rarity" => r.Rarity,
            "Tag1" => r.Tag1,
            "Tag2" => r.Tag2,
            "Tag3" => r.Tag3,
            "Tag4" => r.Tag4,
            "Tag5" => r.Tag5,
            "Unlock1" => r.Unlock1,
            "Unlock2" => r.Unlock2,
            "Unlock3" => r.Unlock3,
            "Unlock4" => r.Unlock4,
            "Unlock5" => r.Unlock5,
            _ => null
        };
    }

    private static TagSO FindTagAssetById(string tagId)
    {
        // Slow but handy fallback: scan all TagSO assets and read their id field
        var guids = AssetDatabase.FindAssets("t:TagSO");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var so = AssetDatabase.LoadAssetAtPath<TagSO>(path);
            if (so != null && string.Equals(so.id, tagId, StringComparison.Ordinal))
                return so;
        }
        return null;
    }

    // Minimal row DTO that matches the sheet columns we generated
    [Serializable]
    public class Row
    {
        public string MonsterID;
        public string MonsterName;
        public string Type;
        public string Rarity;

        public string Tag1;
        public string Tag2;
        public string Tag3;
        public string Tag4;
        public string Tag5;

        public string Unlock1;
        public string Unlock2;
        public string Unlock3;
        public string Unlock4;
        public string Unlock5;
    }

    // Tiny helper to parse a JSON array with JsonUtility
    private static class JsonHelper
    {
        [Serializable] private class Wrapper<T> { public T[] array; }
        public static T[] FromJson<T>(string json)
        {
            json = json.Trim();
            if (json.StartsWith("[")) json = "{\"array\":" + json + "}";
            try { return JsonUtility.FromJson<Wrapper<T>>(json)?.array; }
            catch { return null; }
        }
    }

    // Very basic CSV parser (assumes commas as separators, double-quotes for escaping)
    private static List<Row> ParseCsv(string csv)
    {
        var rows = new List<Row>();
        if (string.IsNullOrEmpty(csv)) return rows;

        // Split lines
        var lines = csv.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (lines.Length == 0) return rows;

        // Header
        var headers = SplitCsvLine(lines[0]);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++) map[headers[i]] = i;

        for (int li = 1; li < lines.Length; li++)
        {
            var line = lines[li];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = SplitCsvLine(line);
            var r = new Row
            {
                MonsterID   = ReadCol(cols, map, "MonsterID"),
                MonsterName = ReadCol(cols, map, "MonsterName"),
                Type        = ReadCol(cols, map, "Type"),
                Rarity      = ReadCol(cols, map, "Rarity"),
                Tag1        = ReadCol(cols, map, "Tag1"),
                Tag2        = ReadCol(cols, map, "Tag2"),
                Tag3        = ReadCol(cols, map, "Tag3"),
                Tag4        = ReadCol(cols, map, "Tag4"),
                Tag5        = ReadCol(cols, map, "Tag5"),
                Unlock1     = ReadCol(cols, map, "Unlock1"),
                Unlock2     = ReadCol(cols, map, "Unlock2"),
                Unlock3     = ReadCol(cols, map, "Unlock3"),
                Unlock4     = ReadCol(cols, map, "Unlock4"),
                Unlock5     = ReadCol(cols, map, "Unlock5"),
            };
            // Ignore empty MonsterID rows
            if (!string.IsNullOrEmpty(r.MonsterID)) rows.Add(r);
        }
        return rows;
    }

    private static string ReadCol(List<string> cols, Dictionary<string,int> map, string key)
    {
        if (!map.TryGetValue(key, out var idx)) return null;
        if (idx < 0 || idx >= cols.Count) return null;
        var s = cols[idx] ?? "";
        return s.Trim();
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(line)) { result.Add(""); return result; }

        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    // Escaped quote?
                    bool nextIsQuote = (i + 1 < line.Length && line[i + 1] == '"');
                    if (nextIsQuote) { cur.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cur.Append(c);
            }
            else
            {
                if (c == ',') { result.Add(cur.ToString()); cur.Length = 0; }
                else if (c == '"') inQuotes = true;
                else cur.Append(c);
            }
        }
        result.Add(cur.ToString());
        return result;
    }
}
#endif
