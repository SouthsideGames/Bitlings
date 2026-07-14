// Assets/Editor/ArenaDataExporter.cs
// Unity Editor tool that exports monster, title, and type chart data to JSON
// for use by the C# Cloud Code module.
//
// Menu: Tools → Arena → Export Reference Data
//       Tools → Arena → Upload Catalogs to Cloud (pushes to Cloud Save for anti-cheat)

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEditor;
using UnityEngine;

public static class ArenaDataExporter
{
    private const string OutputFolder = "Assets/Resources/ArenaCatalogs";

    [MenuItem("Tools/Arena/Export Reference Data")]
    public static void ExportAll()
    {
        EnsureOutputFolder();

        int monsterCount = ExportMonsterCatalog();
        int titleCount = ExportTitleCatalog();
        int typeChartCount = ExportTypeChart();

        AssetDatabase.Refresh();

        Debug.Log($"[ArenaDataExporter] Export complete: {monsterCount} monsters, " +
                  $"{titleCount} titles, {typeChartCount} type chart entries → {OutputFolder}/");
    }

    // ═════════════════════════════════════════════════════════════
    //  Monster Catalog
    // ═════════════════════════════════════════════════════════════

    [MenuItem("Tools/Arena/Export Monster Catalog")]
    public static int ExportMonsterCatalog()
    {
        EnsureOutputFolder();

        var guids = AssetDatabase.FindAssets("t:MonsterDataSO");
        var catalog = new MonsterCatalogData();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(path);
            if (so == null || string.IsNullOrEmpty(so.id)) continue;

            var entry = new MonsterCatalogEntry
            {
                id = so.id,
                displayName = so.displayName,
                type = (int)so.type,
                rarity = (int)so.rarity,
                baseHP = so.baseHP,
                baseAttack = so.baseAttack,
                baseDefense = so.baseDefense,
                baseSpeed = so.baseSpeed,
                arenaScore = so.arenaScore,
                basicAttackName = so.basicAttackName ?? so.displayName,
                isBoss = so.isBoss,
                uncatchable = so.uncatchable,
                defaultAlwaysOnTitleIds = new List<string>(),
                ironTitleIds = new List<string>()
            };

            if (so.defaultAlwaysOnTitles != null)
            {
                foreach (var t in so.defaultAlwaysOnTitles)
                {
                    if (t != null && !string.IsNullOrEmpty(t.titleId))
                        entry.defaultAlwaysOnTitleIds.Add(t.titleId);
                }
            }

            if (so.ironTitles != null)
            {
                foreach (var t in so.ironTitles)
                {
                    if (t != null && !string.IsNullOrEmpty(t.titleId))
                        entry.ironTitleIds.Add(t.titleId);
                }
            }

            catalog.monsters.Add(entry);
        }

        string json = JsonUtility.ToJson(catalog, true);
        File.WriteAllText(Path.Combine(OutputFolder, "monster_catalog.json"), json);

        Debug.Log($"[ArenaDataExporter] Exported {catalog.monsters.Count} monsters.");
        return catalog.monsters.Count;
    }

    // ═════════════════════════════════════════════════════════════
    //  Title Catalog
    // ═════════════════════════════════════════════════════════════

    [MenuItem("Tools/Arena/Export Title Catalog")]
    public static int ExportTitleCatalog()
    {
        EnsureOutputFolder();

        var guids = AssetDatabase.FindAssets("t:TitleSO");
        var catalog = new TitleCatalogData();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<TitleSO>(path);
            if (so == null || string.IsNullOrEmpty(so.titleId)) continue;

            catalog.titles.Add(new TitleCatalogEntry
            {
                titleId = so.titleId,
                displayName = !string.IsNullOrEmpty(so.displayName) ? so.displayName : so.titleId,
                arenaScore = so.arenaScore
            });
        }

        string json = JsonUtility.ToJson(catalog, true);
        File.WriteAllText(Path.Combine(OutputFolder, "title_catalog.json"), json);

        Debug.Log($"[ArenaDataExporter] Exported {catalog.titles.Count} titles.");
        return catalog.titles.Count;
    }

    // ═════════════════════════════════════════════════════════════
    //  Type Chart
    // ═════════════════════════════════════════════════════════════

    [MenuItem("Tools/Arena/Export Type Chart")]
    public static int ExportTypeChart()
    {
        EnsureOutputFolder();

        var chart = new TypeChartData();

        // Iterate all MonsterType pairs, only store non-neutral (1.0) entries
        var types = System.Enum.GetValues(typeof(MonsterType));
        foreach (MonsterType atk in types)
        {
            if (atk == MonsterType.None) continue;
            foreach (MonsterType def in types)
            {
                if (def == MonsterType.None) continue;
                float mult = BattleTypeChart.GetMultiplier(atk, def);
                if (!Mathf.Approximately(mult, 1f))
                {
                    chart.entries.Add(new TypeChartEntry
                    {
                        attackerType = (int)atk,
                        defenderType = (int)def,
                        multiplier = mult
                    });
                }
            }
        }

        string json = JsonUtility.ToJson(chart, true);
        File.WriteAllText(Path.Combine(OutputFolder, "type_chart.json"), json);

        Debug.Log($"[ArenaDataExporter] Exported {chart.entries.Count} type chart entries.");
        return chart.entries.Count;
    }

    // ═════════════════════════════════════════════════════════════

    private static void EnsureOutputFolder()
    {
        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);
    }

    // ═════════════════════════════════════════════════════════════
    //  Upload catalogs to Cloud Save (via ArenaModule.UploadCatalogs)
    // ═════════════════════════════════════════════════════════════

    [MenuItem("Tools/Arena/Upload Catalogs to Cloud")]
    public static async void UploadCatalogsToCloud()
    {
        string monsterPath = Path.Combine(OutputFolder, "monster_catalog.json");
        string titlePath = Path.Combine(OutputFolder, "title_catalog.json");

        if (!File.Exists(monsterPath) || !File.Exists(titlePath))
        {
            Debug.LogError("[ArenaDataExporter] Catalog files not found. Run 'Export Reference Data' first.");
            return;
        }

        string monsterJson = File.ReadAllText(monsterPath);
        string titleJson = File.ReadAllText(titlePath);

        // Type chart is needed server-side for synergy scoring; upload it too if present.
        string typeChartPath = Path.Combine(OutputFolder, "type_chart.json");
        string typeChartJson = File.Exists(typeChartPath) ? File.ReadAllText(typeChartPath) : null;
        if (string.IsNullOrEmpty(typeChartJson))
            Debug.LogWarning("[ArenaDataExporter] type_chart.json missing — server synergy scoring will be skipped. Run 'Export Reference Data' first.");

        if (UGSInitializer.I == null || !UGSInitializer.I.IsReady)
        {
            Debug.LogError("[ArenaDataExporter] UGS not initialized. Enter Play Mode first to authenticate, then try again.");
            return;
        }

        try
        {
            var args = new Dictionary<string, object>
            {
                { "monsterCatalogJson", monsterJson },
                { "titleCatalogJson", titleJson }
            };
            if (!string.IsNullOrEmpty(typeChartJson))
                args["typeChartJson"] = typeChartJson;

            var result = await CloudCodeService.Instance.CallModuleEndpointAsync<UploadCatalogsResponse>(
                "ArenaModule", "UploadCatalogs", args);

            if (result.success)
                Debug.Log("[ArenaDataExporter] Catalogs uploaded to Cloud Save successfully.");
            else
                Debug.LogError($"[ArenaDataExporter] Upload failed: {result.error}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ArenaDataExporter] Upload failed: {ex.Message}");
        }
    }

    [System.Serializable]
    private class UploadCatalogsResponse
    {
        public bool success;
        public string error;
    }
}
#endif
