#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ExchangeBuilder
{
    private const string RequestsFolder = "Assets/Data/Exchange";
    private const string LibraryPath = "Assets/Data/Exchange/ExchangeRequestLibrary.asset";

    [MenuItem("Bitlings/Exchange/Build From CSV...")]
    public static void BuildFromCsvMenu()
    {
        string path = EditorUtility.OpenFilePanel("Exchange Requests CSV", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            BuildFromCsvPath(path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ExchangeBuilder] Failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public static void BuildFromCsvPath(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            Debug.LogError($"[ExchangeBuilder] CSV not found: {csvPath}");
            return;
        }

        EnsureFolders();

        string[] lines = File.ReadAllLines(csvPath);
        if (lines == null || lines.Length < 2)
        {
            Debug.LogWarning("[ExchangeBuilder] CSV has no data rows.");
            return;
        }

        // Header
        var header = SplitCsvLine(lines[0]);
        var col = BuildColumnMap(header);

        RequireColumn(col, "id");
        RequireColumn(col, "displayname");

        var createdOrUpdated = new List<ExchangeRequestSO>();

        for (int i = 1; i < lines.Length; i++)
        {
            string raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var cells = SplitCsvLine(raw);
            string id = Get(col, cells, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            string assetPath = $"{RequestsFolder}/ExchangeRequest_{SanitizeFileName(id)}.asset";
            var req = AssetDatabase.LoadAssetAtPath<ExchangeRequestSO>(assetPath);
            bool isNew = false;
            if (!req)
            {
                req = ScriptableObject.CreateInstance<ExchangeRequestSO>();
                AssetDatabase.CreateAsset(req, assetPath);
                isNew = true;
            }

            ApplyRow(req, col, cells);

            EditorUtility.SetDirty(req);
            createdOrUpdated.Add(req);

            if (isNew)
                Debug.Log($"[ExchangeBuilder] Created {assetPath}");
        }

        // Library
        var lib = AssetDatabase.LoadAssetAtPath<ExchangeRequestLibrarySO>(LibraryPath);
        if (!lib)
        {
            lib = ScriptableObject.CreateInstance<ExchangeRequestLibrarySO>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
            Debug.Log($"[ExchangeBuilder] Created library: {LibraryPath}");
        }

        lib.requests ??= new List<ExchangeRequestSO>();
        lib.requests.Clear();
        lib.requests.AddRange(createdOrUpdated);

        EditorUtility.SetDirty(lib);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ExchangeBuilder] Done. Requests: {createdOrUpdated.Count}");
    }

    private static void ApplyRow(ExchangeRequestSO req, Dictionary<string, int> col, List<string> cells)
    {
        req.requestId = Get(col, cells, "id");
        req.displayName = Get(col, cells, "displayname");

        if (TryGet(col, cells, "flavortext", out string flavor))
            req.flavorText = flavor;

        // Required species — look up MonsterDataSO by id
        if (TryGet(col, cells, "requiredspecies", out string speciesId) && !string.IsNullOrWhiteSpace(speciesId))
        {
            var monster = FindMonsterData(speciesId);
            if (monster)
                req.requiredSpecies = monster;
            else
                Debug.LogWarning($"[ExchangeBuilder] MonsterDataSO not found for species '{speciesId}' on request '{req.requestId}'");
        }
        else
        {
            req.requiredSpecies = null;
        }

        // Required type (for generic requests)
        if (TryGet(col, cells, "requiredtype", out string typeRaw))
        {
            if (Enum.TryParse(typeRaw, true, out MonsterType mt))
                req.requiredType = mt;
        }

        // Required minimum rarity (for generic requests)
        if (TryGet(col, cells, "requiredminrarity", out string rarityRaw))
        {
            if (Enum.TryParse(rarityRaw, true, out Rarity r))
                req.requiredMinRarity = r;
        }

        // Credit reward
        if (TryGet(col, cells, "creditreward", out string crRaw) &&
            int.TryParse(crRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cr))
            req.creditReward = Mathf.Max(1, cr);

        // Bonus resource type
        if (TryGet(col, cells, "bonusresourcetype", out string brtRaw))
        {
            if (Enum.TryParse(brtRaw, true, out ResourceType brt))
                req.bonusResourceType = brt;
        }

        // Bonus resource amount
        if (TryGet(col, cells, "bonusresourceamount", out string braRaw) &&
            int.TryParse(braRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bra))
            req.bonusResourceAmount = Mathf.Max(0, bra);

        // Weight
        if (TryGet(col, cells, "weight", out string wRaw) &&
            int.TryParse(wRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w))
            req.weight = Mathf.Max(1, w);

        // Duration hours
        if (TryGet(col, cells, "durationhours", out string dhRaw) &&
            int.TryParse(dhRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dh))
            req.durationHours = Mathf.Max(1, dh);
    }

    private static MonsterDataSO FindMonsterData(string speciesId)
    {
        string[] guids = AssetDatabase.FindAssets($"t:MonsterDataSO {speciesId}");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(path);
            if (data && string.Equals(data.id, speciesId, StringComparison.OrdinalIgnoreCase))
                return data;
        }
        return null;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        if (!AssetDatabase.IsValidFolder("Assets/Data/Exchange"))
            AssetDatabase.CreateFolder("Assets/Data", "Exchange");
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            string key = (header[i] ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(key)) continue;
            if (!map.ContainsKey(key)) map.Add(key, i);
        }
        return map;
    }

    private static void RequireColumn(Dictionary<string, int> col, string key)
    {
        if (!col.ContainsKey(key))
            throw new Exception($"Missing required column: {key}");
    }

    private static string Get(Dictionary<string, int> col, List<string> cells, string key)
    {
        if (!col.TryGetValue(key, out int idx)) return string.Empty;
        if (idx < 0 || idx >= cells.Count) return string.Empty;
        return (cells[idx] ?? string.Empty).Trim();
    }

    private static bool TryGet(Dictionary<string, int> col, List<string> cells, string key, out string value)
    {
        value = string.Empty;
        if (!col.TryGetValue(key, out int idx)) return false;
        if (idx < 0 || idx >= cells.Count) return false;
        value = (cells[idx] ?? string.Empty).Trim();
        return true;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
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
                result.Add(cur.ToString());
                cur.Length = 0;
                continue;
            }

            cur.Append(c);
        }

        result.Add(cur.ToString());
        return result;
    }

    private static string SanitizeFileName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
#endif
