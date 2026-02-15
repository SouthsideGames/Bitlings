#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds/updates WorldEventSO assets and the WorldEventLibrarySO from a CSV.
///
/// CSV required columns (case-insensitive):
/// - id
/// - displayName
/// - tickerMessage
/// Optional columns:
/// - category (Job/Encounter/Meta/Flavor)
/// - weight (int)
/// - canRotate (true/false)
/// - scheduledOnly (true/false)
/// - startUnix (long)
/// - endUnix (long)
/// - minDaysBetween (float)
/// - isHoliday (true/false)
/// - effects (semicolon-separated)
///
/// Effects format (semicolon-separated):
///   Kind[:Target][:Value]
/// Examples:
///   DisableJobSite:Harbor
///   JobRateMultiplier:Harbor:0.70
///   JobStorageCapMultiplier:Harbor:0.75
///   JobCollectDisabled:Harbor:true
///   JobFatigueRateMultiplier:Harbor:1.25
///   DisableEncounters
///   EncounterEnergyCostMultiplier::0.85
///   BossCadenceMultiplier::0.70
///   ShopPriceMultiplier::1.15
///   ResourceGainMultiplier:TrainingVoucher:1.50
///
/// Assets:
/// - Events created/updated at: Assets/Resources/WorldEvents/Events/
/// - Library created/updated at: Assets/Resources/WorldEvents/WorldEventLibrary.asset
/// </summary>
public static class WorldEventCsvBuilder
{
    private const string EventsFolder = "Assets/Resources/WorldEvents/Events";
    private const string LibraryPath = "Assets/Resources/WorldEvents/WorldEventLibrary.asset";

    [MenuItem("Bitlings/World Events/Build From CSV...")]
    public static void BuildFromCsvMenu()
    {
        string path = EditorUtility.OpenFilePanel("World Events CSV", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            BuildFromCsvPath(path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WorldEventCsvBuilder] Failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public static void BuildFromCsvPath(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            Debug.LogError($"[WorldEventCsvBuilder] CSV not found: {csvPath}");
            return;
        }

        EnsureFolders();

        string[] lines = File.ReadAllLines(csvPath);
        if (lines == null || lines.Length < 2)
        {
            Debug.LogWarning("[WorldEventCsvBuilder] CSV has no data rows.");
            return;
        }

        // Header
        var header = SplitCsvLine(lines[0]);
        var col = BuildColumnMap(header);

        RequireColumn(col, "id");
        RequireColumn(col, "displayname");
        RequireColumn(col, "tickermessage");

        var createdOrUpdated = new List<WorldEventSO>();

        for (int i = 1; i < lines.Length; i++)
        {
            string raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var cells = SplitCsvLine(raw);
            string id = Get(col, cells, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            string assetPath = $"{EventsFolder}/WorldEvent_{SanitizeFileName(id)}.asset";
            var evt = AssetDatabase.LoadAssetAtPath<WorldEventSO>(assetPath);
            bool isNew = false;
            if (!evt)
            {
                evt = ScriptableObject.CreateInstance<WorldEventSO>();
                AssetDatabase.CreateAsset(evt, assetPath);
                isNew = true;
            }

            ApplyRow(evt, col, cells);

            EditorUtility.SetDirty(evt);
            createdOrUpdated.Add(evt);

            if (isNew)
                Debug.Log($"[WorldEventCsvBuilder] Created {assetPath}");
        }

        // Library
        var lib = AssetDatabase.LoadAssetAtPath<WorldEventLibrarySO>(LibraryPath);
        if (!lib)
        {
            lib = ScriptableObject.CreateInstance<WorldEventLibrarySO>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
            Debug.Log($"[WorldEventCsvBuilder] Created library: {LibraryPath}");
        }

        lib.events ??= new List<WorldEventSO>();
        lib.events.Clear();
        lib.events.AddRange(createdOrUpdated);

        EditorUtility.SetDirty(lib);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[WorldEventCsvBuilder] Done. Events: {createdOrUpdated.Count}");
    }

    private static void ApplyRow(WorldEventSO evt, Dictionary<string, int> col, List<string> cells)
    {
        evt.id = Get(col, cells, "id");
        evt.displayName = Get(col, cells, "displayname");
        evt.tickerMessage = Get(col, cells, "tickermessage");

        // Optional
        if (TryGet(col, cells, "category", out string catRaw))
        {
            if (Enum.TryParse(catRaw, true, out WorldEventCategory cat))
                evt.category = cat;
        }

        if (TryGet(col, cells, "weight", out string wRaw) && int.TryParse(wRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w))
            evt.weight = Mathf.Max(0, w);

        if (TryGetBool(col, cells, "canrotate", out bool canRotate))
            evt.canRotate = canRotate;

        if (TryGetBool(col, cells, "scheduledonly", out bool scheduledOnly))
            evt.scheduledOnly = scheduledOnly;

        if (TryGet(col, cells, "startunix", out string su) && long.TryParse(su, NumberStyles.Integer, CultureInfo.InvariantCulture, out long startUnix))
            evt.startUnix = startUnix;

        if (TryGet(col, cells, "endunix", out string eu) && long.TryParse(eu, NumberStyles.Integer, CultureInfo.InvariantCulture, out long endUnix))
            evt.endUnix = endUnix;

        if (TryGet(col, cells, "mindaysbetween", out string md) && float.TryParse(md, NumberStyles.Float, CultureInfo.InvariantCulture, out float minDays))
            evt.minDaysBetween = Mathf.Max(0f, minDays);

        if (TryGetBool(col, cells, "isholiday", out bool holiday))
            evt.isHoliday = holiday;

        // Effects
        evt.effects ??= new List<WorldEventEffect>();
        evt.effects.Clear();

        if (TryGet(col, cells, "effects", out string fxRaw) && !string.IsNullOrWhiteSpace(fxRaw))
        {
            var parts = fxRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var fx = ParseEffect(parts[i].Trim());
                if (fx.kind != WorldEventEffectKind.None)
                    evt.effects.Add(fx);
            }
        }
    }

    private static WorldEventEffect ParseEffect(string token)
    {
        // Kind[:Target][:Value]
        var seg = token.Split(new[] { ':' }, StringSplitOptions.None);
        if (seg.Length <= 0) return default;

        if (!Enum.TryParse(seg[0].Trim(), true, out WorldEventEffectKind kind))
            return default;

        var fx = new WorldEventEffect { kind = kind, value = 1f };

        string target = seg.Length >= 2 ? seg[1].Trim() : string.Empty;
        string val = seg.Length >= 3 ? seg[2].Trim() : string.Empty;

        switch (kind)
        {
            // Job-targeted
            case WorldEventEffectKind.DisableJobSite:
            case WorldEventEffectKind.JobRateMultiplier:
            case WorldEventEffectKind.JobStorageCapMultiplier:
            case WorldEventEffectKind.JobCollectDisabled:
            case WorldEventEffectKind.JobFatigueRateMultiplier:
                if (!string.IsNullOrEmpty(target) && Enum.TryParse(target, true, out JobType job))
                    fx.job = job;

                if (!string.IsNullOrEmpty(val))
                {
                    if (kind == WorldEventEffectKind.JobCollectDisabled)
                    {
                        if (bool.TryParse(val, out bool b)) fx.flag = b;
                        else if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv)) fx.value = fv;
                        else fx.flag = true;
                    }
                    else if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                        fx.value = fv;
                }
                else
                {
                    if (kind == WorldEventEffectKind.JobCollectDisabled)
                        fx.flag = true;
                }
                break;

            // Resource-targeted
            case WorldEventEffectKind.ResourceGainMultiplier:
                if (!string.IsNullOrEmpty(target) && Enum.TryParse(target, true, out ResourceType rt))
                    fx.resource = rt;
                if (!string.IsNullOrEmpty(val) && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float rm))
                    fx.value = rm;
                break;

            // Global multipliers
            default:
                if (!string.IsNullOrEmpty(val) && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float mv))
                    fx.value = mv;
                break;
        }

        return fx;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        if (!AssetDatabase.IsValidFolder("Assets/Resources/WorldEvents"))
            AssetDatabase.CreateFolder("Assets/Resources", "WorldEvents");

        if (!AssetDatabase.IsValidFolder(EventsFolder))
            AssetDatabase.CreateFolder("Assets/Resources/WorldEvents", "Events");
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

    private static bool TryGetBool(Dictionary<string, int> col, List<string> cells, string key, out bool value)
    {
        value = false;
        if (!TryGet(col, cells, key, out string raw)) return false;
        if (bool.TryParse(raw, out bool b)) { value = b; return true; }
        if (int.TryParse(raw, out int i)) { value = i != 0; return true; }
        return false;
    }

    // Minimal CSV splitter with quote support.
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
                    // Escaped quote
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
