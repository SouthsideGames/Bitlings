#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class MonsterCsvImporterWindow : EditorWindow
{
    [Header("CSV Input")]
    [Tooltip("Drag the CSV from the Project window. Supports DefaultAsset (.csv) or TextAsset.")]
    [SerializeField] private UnityEngine.Object csvAsset;
    [SerializeField] private string csvPathOverride = "";

    [Header("Output Root Folder")]
    [Tooltip("Root folder that contains per-type folders (e.g., Assets/Data/Monsters).")]
    [SerializeField] private DefaultAsset outputRootFolder;

    [Header("Create / Update")]
    [SerializeField] private bool createIfMissing = true;
    [SerializeField] private bool updateExisting = true;

    [Header("Type Folder Routing")]
    [SerializeField] private bool routeByTypeFolder = true;
    [SerializeField] private bool moveExistingToTypeFolder = true;

    [Header("Asset Naming")]
    [Tooltip("If enabled, asset files are renamed to Monster_<NoSpacesName>.asset")]
    [SerializeField] private bool renameAssetsToMonsterName = true;

    [Header("Optional Resolution")]
    [SerializeField] private bool resolveEvolutionForm = true;
    [SerializeField] private bool resolvePersonality = true;
    [SerializeField] private bool fillDefaultAlwaysOnTitlesFromCsv = false;
    [SerializeField] private bool logVerbose = false;

    // IMPORTANT: Case-insensitive dictionary => no duplicate casing keys.
    private static readonly Dictionary<string, string> COL = new(StringComparer.OrdinalIgnoreCase)
    {
        {"ID","ID"},
        {"Monster ID","ID"},

        {"Name","Name"},
        {"Display Name","Name"},

        {"Type","Type"},
        {"Rarity","Rarity"},

        {"Spawn Weight","Spawn Weight"},
        {"spawnWeight","Spawn Weight"},

        {"Personality","Personality"},

        {"Base HP","Base HP"},
        {"Base Attack","Base Attack"},
        {"Base Defense","Base Defense"},
        {"baseDefense","Base Defense"},
        {"Base Speed","Base Speed"},

        {"Hp Regen Per Hour","Hp Regen Per Hour"},
        {"Fatigue Rate Per Hour","Fatigue Rate Per Hour"},
        {"Fatigue Cooldown Hours","Fatigue Cooldown Hours"},

        {"Job Skill","Job Skill"},

        {"Evolution Stage","Evolution Stage"},
        {"Evolution Level","Evolution Level"},
        {"Evolution Form Id","Evolution Form Id"},

        {"Attack Name","Attack Name"},
        {"Description","Description"},

        {"Title Track","Title Track"},
    };

    [MenuItem("Bitlings/Import/Monsters From CSV")]
    public static void Open()
    {
        var win = GetWindow<MonsterCsvImporterWindow>("Monster CSV Importer");
        win.minSize = new Vector2(560, 650);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Monster CSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        csvAsset = EditorGUILayout.ObjectField("CSV Asset (Project)", csvAsset, typeof(UnityEngine.Object), false);
        csvPathOverride = EditorGUILayout.TextField("CSV Path Override (optional)", csvPathOverride);

        EditorGUILayout.Space();
        outputRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Root Folder", outputRootFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space();
        createIfMissing = EditorGUILayout.Toggle("Create If Missing", createIfMissing);
        updateExisting = EditorGUILayout.Toggle("Update Existing", updateExisting);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Type Folder Routing", EditorStyles.boldLabel);
        routeByTypeFolder = EditorGUILayout.Toggle("Route Assets By Type Folder", routeByTypeFolder);
        using (new EditorGUI.DisabledScope(!routeByTypeFolder))
        {
            moveExistingToTypeFolder = EditorGUILayout.Toggle("Move Existing Assets To Type Folder", moveExistingToTypeFolder);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Asset Naming", EditorStyles.boldLabel);
        renameAssetsToMonsterName = EditorGUILayout.Toggle("Rename Assets To Monster Name", renameAssetsToMonsterName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Optional Resolution", EditorStyles.boldLabel);
        resolveEvolutionForm = EditorGUILayout.Toggle("Resolve Evolution Form (2nd pass)", resolveEvolutionForm);
        resolvePersonality = EditorGUILayout.Toggle("Resolve Personality (by asset name)", resolvePersonality);
        fillDefaultAlwaysOnTitlesFromCsv = EditorGUILayout.Toggle("Fill defaultAlwaysOnTitles from 'Title Track' pipe list", fillDefaultAlwaysOnTitlesFromCsv);
        logVerbose = EditorGUILayout.Toggle("Verbose Logs", logVerbose);

        EditorGUILayout.Space(16);

        using (new EditorGUI.DisabledScope(!CanRun()))
        {
            if (GUILayout.Button("Import / Update Monsters", GUILayout.Height(38)))
                Import();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Creates/updates MonsterDataSO from CSV.\n" +
            "Routing:\n  <Root>/<TypeName>/Monster_<...>.asset\n" +
            "Renaming:\n  Monster_<NoSpacesName>.asset (done AFTER batch edit to ensure it applies)\n",
            MessageType.Info
        );
    }

    private bool CanRun()
    {
        if (outputRootFolder == null) return false;

        string rootPath = AssetDatabase.GetAssetPath(outputRootFolder);
        if (!AssetDatabase.IsValidFolder(rootPath)) return false;

        if (csvAsset == null && string.IsNullOrWhiteSpace(csvPathOverride)) return false;
        return true;
    }

    private void Import()
    {
        string rootPath = AssetDatabase.GetAssetPath(outputRootFolder);
        if (!AssetDatabase.IsValidFolder(rootPath))
        {
            Debug.LogError("[MonsterCsvImporter] Output root folder is invalid.");
            return;
        }

        if (!TryReadCsvText(out string csvText, out string readErr))
        {
            Debug.LogError($"[MonsterCsvImporter] CSV read failed: {readErr}");
            return;
        }

        var table = ParseCsv(csvText);
        if (table.Headers.Count == 0 || table.Rows.Count == 0)
        {
            Debug.LogError("[MonsterCsvImporter] CSV appears empty or failed to parse.");
            return;
        }

        var headerMap = BuildHeaderMap(table.Headers);

        // Index ALL existing monsters under root (including subfolders)
        var existingById = IndexExistingMonsters(rootPath);

        Dictionary<string, MonsterPersonalitySO> personalityByName = null;
        if (resolvePersonality)
            personalityByName = IndexPersonalities();

        Dictionary<string, TitleSO> titleById = null;
        if (fillDefaultAlwaysOnTitlesFromCsv)
            titleById = IndexTitles();

        var pendingEvolution = new Dictionary<MonsterDataSO, string>();

        // Defer asset operations that are flaky during StartAssetEditing (especially rename)
        var deferredMoves = new List<(MonsterDataSO monster, string desiredFolder)>();
        var deferredRenames = new List<MonsterDataSO>();

        int created = 0, updated = 0, skipped = 0, errors = 0, moved = 0, renamed = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var row in table.Rows)
            {
                try
                {
                    string id = Get(row, headerMap, "ID").Trim();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        skipped++;
                        continue;
                    }

                    MonsterType parsedType = MonsterType.None;
                    TryParseEnum(Get(row, headerMap, "Type"), out parsedType);

                    bool has = existingById.TryGetValue(id, out var monster);

                    if (!has && !createIfMissing) { skipped++; continue; }
                    if (has && !updateExisting) { skipped++; continue; }

                    if (!has)
                    {
                        string typeFolder = routeByTypeFolder ? EnsureTypeFolder(rootPath, parsedType) : rootPath;
                        monster = CreateMonsterAsset(typeFolder, id);
                        existingById[id] = monster;
                        created++;
                    }
                    else
                    {
                        updated++;
                    }

                    Undo.RecordObject(monster, "Import Monster From CSV");

                    // Identity
                    monster.id = id;

                    string name = Get(row, headerMap, "Name").Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        monster.displayName = name;

                    // Type / rarity
                    if (parsedType != MonsterType.None)
                        monster.type = parsedType;

                    if (TryParseEnum(Get(row, headerMap, "Rarity"), out Rarity rr))
                        monster.rarity = rr;

                    // Defer move until after batch (more reliable)
                    if (routeByTypeFolder && moveExistingToTypeFolder)
                    {
                        string desiredFolder = EnsureTypeFolder(rootPath, monster.type);
                        deferredMoves.Add((monster, desiredFolder));
                    }

                    // Defer rename until after batch (KEY FIX)
                    if (renameAssetsToMonsterName)
                        deferredRenames.Add(monster);

                    // Encounter
                    if (TryFloat(Get(row, headerMap, "Spawn Weight"), out float sw))
                        monster.spawnWeight = Mathf.Max(0f, sw);

                    // Stats
                    if (TryInt(Get(row, headerMap, "Base HP"), out int hp)) monster.baseHP = Mathf.Max(1, hp);
                    if (TryInt(Get(row, headerMap, "Base Attack"), out int atk)) monster.baseAttack = Mathf.Max(1, atk);
                    if (TryInt(Get(row, headerMap, "Base Defense"), out int def)) monster.baseDefense = Mathf.Max(0, def);
                    if (TryInt(Get(row, headerMap, "Base Speed"), out int spd)) monster.baseSpeed = Mathf.Max(0, spd);

                    // Regen / fatigue
                    if (TryFloat(Get(row, headerMap, "Hp Regen Per Hour"), out float regen)) monster.hpRegenPerHour = Mathf.Max(0f, regen);
                    if (TryFloat(Get(row, headerMap, "Fatigue Rate Per Hour"), out float fr)) monster.fatigueRatePerHour = Mathf.Clamp(fr, 0f, 0.20f);
                    if (TryFloat(Get(row, headerMap, "Fatigue Cooldown Hours"), out float fcd)) monster.fatigueCooldownHours = Mathf.Clamp(fcd, 0f, 48f);

                    // Job skill
                    if (TryFloat(Get(row, headerMap, "Job Skill"), out float js)) monster.jobSkill = Mathf.Clamp(js, 0.5f, 3f);

                    // Evolution stage/level (form resolved later)
                    if (TryInt(Get(row, headerMap, "Evolution Stage"), out int es)) monster.evolutionStage = Mathf.Max(1, es);
                    if (TryInt(Get(row, headerMap, "Evolution Level"), out int el)) monster.evolutionLevel = Mathf.Max(0, el);

                    if (resolveEvolutionForm)
                    {
                        string evoId = Get(row, headerMap, "Evolution Form Id").Trim();
                        pendingEvolution[monster] = evoId;
                    }

                    // Personality
                    if (resolvePersonality && personalityByName != null)
                    {
                        string p = Get(row, headerMap, "Personality").Trim();
                        if (!string.IsNullOrWhiteSpace(p) && personalityByName.TryGetValue(p, out var pso))
                            monster.Personality = pso;
                        else if (!string.IsNullOrWhiteSpace(p) && logVerbose)
                            Debug.LogWarning($"[MonsterCsvImporter] Personality '{p}' not found (Monster {id})");
                    }

                    // Attack name / description
                    string atkName = Get(row, headerMap, "Attack Name");
                    if (!string.IsNullOrWhiteSpace(atkName)) monster.basicAttackName = atkName.Trim();

                    string desc = Get(row, headerMap, "Description");
                    if (!string.IsNullOrWhiteSpace(desc)) monster.description = desc.Trim();

                    // Titles (optional)
                    if (fillDefaultAlwaysOnTitlesFromCsv && titleById != null)
                    {
                        string pipe = Get(row, headerMap, "Title Track").Trim();
                        if (!string.IsNullOrWhiteSpace(pipe))
                        {
                            var parts = pipe.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                            var list = new List<TitleSO>();

                            foreach (var raw in parts)
                            {
                                string tid = raw.Trim();
                                if (string.IsNullOrWhiteSpace(tid)) continue;

                                if (titleById.TryGetValue(tid, out var tso))
                                    list.Add(tso);
                                else if (logVerbose)
                                    Debug.LogWarning($"[MonsterCsvImporter] Title not found for id '{tid}' (Monster {id})");
                            }

                            monster.defaultAlwaysOnTitles = list.ToArray();
                        }
                    }

                    EditorUtility.SetDirty(monster);

                    if (logVerbose)
                        Debug.Log($"[MonsterCsvImporter] Imported {monster.displayName} ({monster.id})");
                }
                catch (Exception exRow)
                {
                    errors++;
                    Debug.LogError($"[MonsterCsvImporter] Row failed: {exRow}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // Save the data edits first
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Apply deferred moves (if any)
        if (routeByTypeFolder && moveExistingToTypeFolder && deferredMoves.Count > 0)
        {
            foreach (var m in deferredMoves)
            {
                if (m.monster == null) continue;

                if (TryMoveAssetToFolder(m.monster, m.desiredFolder, out string moveErr))
                {
                    moved++;
                }
                else if (!string.IsNullOrWhiteSpace(moveErr))
                {
                    Debug.LogWarning($"[MonsterCsvImporter] Move failed for {m.monster.id}: {moveErr}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Apply deferred renames (KEY FIX)
        if (renameAssetsToMonsterName && deferredRenames.Count > 0)
        {
            foreach (var monster in deferredRenames)
            {
                if (monster == null) continue;

                if (TryRenameAssetToMatchMonster(monster, out string renameErr))
                {
                    renamed++;
                }
                else if (!string.IsNullOrWhiteSpace(renameErr))
                {
                    Debug.LogWarning($"[MonsterCsvImporter] Rename failed for {monster.id}: {renameErr}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Second pass: resolve evolution forms
        if (resolveEvolutionForm && pendingEvolution.Count > 0)
        {
            int linked = 0;

            // Re-index after moves/renames so evolution resolution is accurate
            existingById = IndexExistingMonsters(rootPath);

            foreach (var kv in pendingEvolution)
            {
                var monster = kv.Key;
                string evoId = (kv.Value ?? "").Trim();
                if (monster == null) continue;

                if (string.IsNullOrWhiteSpace(evoId))
                {
                    if (monster.evolutionForm != null)
                    {
                        monster.evolutionForm = null;
                        EditorUtility.SetDirty(monster);
                    }
                    continue;
                }

                if (existingById.TryGetValue(evoId, out var evo))
                {
                    monster.evolutionForm = evo;
                    EditorUtility.SetDirty(monster);
                    linked++;
                }
                else if (logVerbose)
                {
                    Debug.LogWarning($"[MonsterCsvImporter] Evolution Form Id '{evoId}' not found for monster {monster.id}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MonsterCsvImporter] Evolution linking complete: {linked} links set.");
        }

        Debug.Log($"[MonsterCsvImporter] Done. Created={created}, Updated={updated}, Moved={moved}, Renamed={renamed}, Skipped={skipped}, Errors={errors}");
    }

    // ---------------- CSV Reading ----------------

    private bool TryReadCsvText(out string csvText, out string error)
    {
        csvText = null;
        error = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(csvPathOverride))
            {
                if (!File.Exists(csvPathOverride))
                {
                    error = $"Override path not found: {csvPathOverride}";
                    return false;
                }

                csvText = File.ReadAllText(csvPathOverride, Encoding.UTF8);
                return true;
            }

            if (csvAsset == null)
            {
                error = "No CSV asset assigned.";
                return false;
            }

            if (csvAsset is TextAsset ta)
            {
                csvText = ta.text;
                return true;
            }

            string assetPath = AssetDatabase.GetAssetPath(csvAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                error = "Could not resolve asset path for CSV asset.";
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string abs = Path.Combine(projectRoot, assetPath);

            if (!File.Exists(abs))
            {
                error = $"Resolved CSV path not found: {abs}";
                return false;
            }

            csvText = File.ReadAllText(abs, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---------------- Type Folder Routing ----------------

    private static string EnsureTypeFolder(string rootPath, MonsterType type)
    {
        string typeName = (type == MonsterType.None) ? "Unsorted" : type.ToString();
        string desired = $"{rootPath}/{typeName}";

        if (!AssetDatabase.IsValidFolder(desired))
            AssetDatabase.CreateFolder(rootPath, typeName);

        return desired;
    }

    private static bool TryMoveAssetToFolder(UnityEngine.Object asset, string desiredFolder, out string error)
    {
        error = null;
        if (asset == null) return false;

        string assetPath = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(assetPath)) return false;

        if (!AssetDatabase.IsValidFolder(desiredFolder))
        {
            error = $"Desired folder does not exist: {desiredFolder}";
            return false;
        }

        string currentFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.Equals(currentFolder, desiredFolder, StringComparison.OrdinalIgnoreCase))
            return false;

        string fileName = Path.GetFileName(assetPath);
        string targetPath = AssetDatabase.GenerateUniqueAssetPath($"{desiredFolder}/{fileName}");

        string moveErr = AssetDatabase.MoveAsset(assetPath, targetPath);
        if (!string.IsNullOrWhiteSpace(moveErr))
        {
            error = moveErr;
            return false;
        }

        return true;
    }

    // ---------------- Asset renaming (Monster_<NoSpacesName>) ----------------

    private static bool TryRenameAssetToMatchMonster(MonsterDataSO monster, out string error)
    {
        error = null;
        if (monster == null) return false;

        string display = monster.displayName ?? "";
        if (string.IsNullOrWhiteSpace(display)) return false;

        string assetPath = AssetDatabase.GetAssetPath(monster);
        if (string.IsNullOrWhiteSpace(assetPath)) return false;

        string desiredName = $"Monster_{ToAssetName(display)}";
        string currentName = Path.GetFileNameWithoutExtension(assetPath);

        if (string.Equals(currentName, desiredName, StringComparison.Ordinal))
            return false;

        string renameErr = AssetDatabase.RenameAsset(assetPath, desiredName);
        if (!string.IsNullOrWhiteSpace(renameErr))
        {
            error = renameErr;
            return false;
        }

        return true;
    }

    // Requirement: remove spaces entirely; capitalize first character.
    private static string ToAssetName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "";

        foreach (char c in Path.GetInvalidFileNameChars())
            displayName = displayName.Replace(c.ToString(), "");

        // Remove ALL whitespace (spaces, tabs, etc.)
        displayName = string.Concat(displayName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        if (displayName.Length == 0) return "";

        return char.ToUpperInvariant(displayName[0]) + displayName.Substring(1);
    }

    // ---------------- Asset creation / Indexing ----------------

    private static MonsterDataSO CreateMonsterAsset(string folderPath, string id)
    {
        string safe = id.Replace("/", "_").Replace("\\", "_").Trim();
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/Monster_{safe}.asset");

        var so = ScriptableObject.CreateInstance<MonsterDataSO>();
        so.id = id;

        AssetDatabase.CreateAsset(so, assetPath);
        return so;
    }

    private static Dictionary<string, MonsterDataSO> IndexExistingMonsters(string rootFolderPath)
    {
        var dict = new Dictionary<string, MonsterDataSO>(StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets("t:MonsterDataSO", new[] { rootFolderPath });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var so = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(p);
            if (so == null) continue;

            if (!string.IsNullOrWhiteSpace(so.id))
                dict[so.id.Trim()] = so;
        }

        return dict;
    }

    private static Dictionary<string, MonsterPersonalitySO> IndexPersonalities()
    {
        var dict = new Dictionary<string, MonsterPersonalitySO>(StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets("t:MonsterPersonalitySO");
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var so = AssetDatabase.LoadAssetAtPath<MonsterPersonalitySO>(p);
            if (so == null) continue;

            dict[so.name.Trim()] = so;
        }

        return dict;
    }

    private static Dictionary<string, TitleSO> IndexTitles()
    {
        var dict = new Dictionary<string, TitleSO>(StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets("t:TitleSO");
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var so = AssetDatabase.LoadAssetAtPath<TitleSO>(p);
            if (so == null) continue;

            var idField = so.GetType().GetField("id");
            if (idField == null) continue;

            string id = idField.GetValue(so) as string;
            if (string.IsNullOrWhiteSpace(id)) continue;

            dict[id.Trim()] = so;
        }

        return dict;
    }

    // ---------------- CSV parsing ----------------

    private sealed class CsvTable
    {
        public readonly List<string> Headers = new();
        public readonly List<Dictionary<string, string>> Rows = new();
    }

    private static CsvTable ParseCsv(string text)
    {
        var t = new CsvTable();
        using var sr = new StringReader(text);

        string headerLine = sr.ReadLine();
        if (headerLine == null) return t;

        var headersRaw = SplitCsvLine(headerLine);
        foreach (var h in headersRaw)
            t.Headers.Add((h ?? "").Trim());

        string line;
        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            line = line.TrimEnd('\r');

            var cells = SplitCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int n = Mathf.Min(t.Headers.Count, cells.Count);
            for (int i = 0; i < n; i++)
                row[t.Headers[i]] = cells[i];

            t.Rows.Add(row);
        }

        return t;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
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
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        result.Add(sb.ToString());
        return result;
    }

    // ---------------- header / cell helpers ----------------

    private static Dictionary<string, string> BuildHeaderMap(List<string> headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in headers)
        {
            string key = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (COL.TryGetValue(key, out var normalized))
            {
                if (!map.ContainsKey(normalized))
                    map[normalized] = key;
            }
            else
            {
                if (!map.ContainsKey(key))
                    map[key] = key;
            }
        }

        return map;
    }

    private static string Get(Dictionary<string, string> row, Dictionary<string, string> headerMap, string normalized)
    {
        if (!headerMap.TryGetValue(normalized, out var actualHeader)) return "";
        if (!row.TryGetValue(actualHeader, out var val)) return "";
        return val ?? "";
    }

    private static bool TryInt(string s, out int v)
    {
        s = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) { v = 0; return false; }
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryFloat(string s, out float v)
    {
        s = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) { v = 0; return false; }

        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return true;

        s = s.Replace(",", ".");
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryParseEnum<T>(string s, out T value) where T : struct
    {
        s = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            value = default;
            return false;
        }

        return Enum.TryParse(s, true, out value);
    }
}
#endif
