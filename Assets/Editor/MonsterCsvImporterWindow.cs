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

    [Header("Output Root Folder (Monsters)")]
    [Tooltip("Root folder that contains per-type folders (e.g., Assets/Data/Monsters).")]
    [SerializeField] private DefaultAsset outputMonsterRootFolder;

    [Header("Roots (Project)")]
    [Tooltip("Titles are stored here (Assets/Data/Title).")]
    [SerializeField] private DefaultAsset titlesRootFolder;            // Assets/Data/Title
    [Tooltip("Title Tracks are stored here (Assets/Data/TitleTracks).")]
    [SerializeField] private DefaultAsset titleTracksRootFolder;       // Assets/Data/TitleTracks
    [Tooltip("Type icons (sprites) are stored here (Assets/Art/Types).")]
    [SerializeField] private DefaultAsset typeIconsRootFolder;         // Assets/Art/Types

    [Header("Create / Update")]
    [SerializeField] private bool createIfMissing = true;
    [SerializeField] private bool updateExisting = true;

    [Header("Monster Folder Routing")]
    [SerializeField] private bool routeMonstersByTypeFolder = true;
    [SerializeField] private bool moveExistingMonstersToTypeFolder = true;

    [Header("Monster Asset Naming")]
    [Tooltip("If enabled, monster asset files are renamed to Monster_<NoSpacesName>.asset")]
    [SerializeField] private bool renameMonsterAssetsToMonsterName = true;

    [Header("Type Icon")]
    [Tooltip("If enabled, assigns MonsterDataSO.typeIcon using sprites in Assets/Art/Types based on type mapping.")]
    [SerializeField] private bool autoAssignTypeIcon = true;

    [Header("Title Track Sync")]
    [Tooltip("If enabled, review & update the monster's TitleTrackSO to match CSV.")]
    [SerializeField] private bool reviewUpdateTitleTrack = true;

    [Tooltip("If a monster has no titleTrack, create one and assign it.")]
    [SerializeField] private bool createTitleTrackIfMissing = true;

    [Tooltip("If enabled, move title tracks into the correct type folder under TitleTracks root.")]
    [SerializeField] private bool moveTitleTracksToTypeFolder = true;

    [Tooltip("If enabled, rename title track asset files to TitleTrack_<NoSpacesMonsterName>.asset")]
    [SerializeField] private bool renameTitleTracksToMonsterName = true;

    [Tooltip("If enabled, update tiers on the TitleTrackSO to match CSV Title Track + Title Track Levels.")]
    [SerializeField] private bool syncTitleTrackTiersFromCsv = true;

    [Header("Optional Resolution")]
    [SerializeField] private bool resolveEvolutionForm = true;
    [SerializeField] private bool resolvePersonality = true;

    [SerializeField] private bool logVerbose = false;

    // Column normalization map (case-insensitive)
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

        // Title Track sync
        {"Title Track","Title Track"},                 // pipe list of Title IDs (TitleSO.titleId)
        {"Title Track Titles","Title Track"},          // alias
        {"Title Track Levels","Title Track Levels"},   // pipe list of ints
        {"Title Levels","Title Track Levels"},         // alias
    };

    // Your type icon filename mapping (FileName -> Type)
    // We store as MonsterType -> FileName.
    private static readonly Dictionary<MonsterType, string> TYPE_ICON_FILE = new()
    {
        { MonsterType.Bug, "Bug" },
        { MonsterType.Clash, "Clash" },
        { MonsterType.Umbral, "Dark" },
        { MonsterType.Wyrm, "Dragon" },
        { MonsterType.Electric, "Electric" },
        { MonsterType.Fire, "Fire" },
        { MonsterType.Grass, "Grass" },
        { MonsterType.Ground, "Ground" },
        { MonsterType.Ice, "Ice" },
        { MonsterType.Oracle, "Mystic" },
        { MonsterType.Corrupt, "Poison" },
        { MonsterType.Rock, "Rock" },
        { MonsterType.Sky, "Sky" },
        { MonsterType.Specter, "Spirit" },
        { MonsterType.Alloy, "Steel" },
        { MonsterType.Water, "Water" },
    };

    [MenuItem("Bitlings/Import/Monsters From CSV")]
    public static void Open()
    {
        var win = GetWindow<MonsterCsvImporterWindow>("Monster CSV Importer");
        win.minSize = new Vector2(700, 860);
    }

    private void OnEnable()
    {
        // Auto-fill roots if they exist (non-destructive)
        if (titlesRootFolder == null)
            titlesRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data/Title");
        if (titleTracksRootFolder == null)
            titleTracksRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data/TitleTracks");
        if (typeIconsRootFolder == null)
            typeIconsRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Art/Types");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Monster CSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        csvAsset = EditorGUILayout.ObjectField("CSV Asset (Project)", csvAsset, typeof(UnityEngine.Object), false);
        csvPathOverride = EditorGUILayout.TextField("CSV Path Override (optional)", csvPathOverride);

        EditorGUILayout.Space();
        outputMonsterRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Monster Root Folder", outputMonsterRootFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Known Roots", EditorStyles.boldLabel);
        titlesRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Titles Root (Assets/Data/Title)", titlesRootFolder, typeof(DefaultAsset), false);
        titleTracksRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("TitleTracks Root (Assets/Data/TitleTracks)", titleTracksRootFolder, typeof(DefaultAsset), false);
        typeIconsRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Type Icons Root (Assets/Art/Types)", typeIconsRootFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Create / Update", EditorStyles.boldLabel);
        createIfMissing = EditorGUILayout.Toggle("Create If Missing", createIfMissing);
        updateExisting = EditorGUILayout.Toggle("Update Existing", updateExisting);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Monster Folder Routing", EditorStyles.boldLabel);
        routeMonstersByTypeFolder = EditorGUILayout.Toggle("Route Monsters By Type Folder", routeMonstersByTypeFolder);
        using (new EditorGUI.DisabledScope(!routeMonstersByTypeFolder))
        {
            moveExistingMonstersToTypeFolder = EditorGUILayout.Toggle("Move Existing Monsters To Type Folder", moveExistingMonstersToTypeFolder);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Monster Asset Naming", EditorStyles.boldLabel);
        renameMonsterAssetsToMonsterName = EditorGUILayout.Toggle("Rename Monster Assets", renameMonsterAssetsToMonsterName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Type Icon", EditorStyles.boldLabel);
        autoAssignTypeIcon = EditorGUILayout.Toggle("Auto-Assign typeIcon", autoAssignTypeIcon);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Title Track Sync", EditorStyles.boldLabel);
        reviewUpdateTitleTrack = EditorGUILayout.Toggle("Review/Update Title Track", reviewUpdateTitleTrack);
        using (new EditorGUI.DisabledScope(!reviewUpdateTitleTrack))
        {
            createTitleTrackIfMissing = EditorGUILayout.Toggle("Create Track If Missing", createTitleTrackIfMissing);
            moveTitleTracksToTypeFolder = EditorGUILayout.Toggle("Move Tracks To Type Folder", moveTitleTracksToTypeFolder);
            renameTitleTracksToMonsterName = EditorGUILayout.Toggle("Rename Track Assets", renameTitleTracksToMonsterName);
            syncTitleTrackTiersFromCsv = EditorGUILayout.Toggle("Sync Track Tiers From CSV", syncTitleTrackTiersFromCsv);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Optional", EditorStyles.boldLabel);
        resolveEvolutionForm = EditorGUILayout.Toggle("Resolve Evolution Form (2nd pass)", resolveEvolutionForm);
        resolvePersonality = EditorGUILayout.Toggle("Resolve Personality (by asset name)", resolvePersonality);
        logVerbose = EditorGUILayout.Toggle("Verbose Logs", logVerbose);

        EditorGUILayout.Space(18);

        using (new EditorGUI.DisabledScope(!CanRun()))
        {
            if (GUILayout.Button("Import / Update Monsters", GUILayout.Height(42)))
                Import();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Title Track Sync expects:\n" +
            "  Title Track:        T-001|T-014|T-022\n" +
            "  Title Track Levels: 3|7|12\n\n" +
            "Titles loaded from: Assets/Data/Title\n" +
            "Tracks stored under: Assets/Data/TitleTracks/<TypeName>/TitleTrack_<NoSpacesMonsterName>.asset\n" +
            "Type Icons loaded from: Assets/Art/Types (your mapping).",
            MessageType.Info
        );
    }

    private bool CanRun()
    {
        if (outputMonsterRootFolder == null) return false;
        if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(outputMonsterRootFolder))) return false;

        if (reviewUpdateTitleTrack)
        {
            if (titleTracksRootFolder == null) return false;
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(titleTracksRootFolder))) return false;

            if (titlesRootFolder == null) return false;
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(titlesRootFolder))) return false;
        }

        if (autoAssignTypeIcon)
        {
            if (typeIconsRootFolder == null) return false;
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(typeIconsRootFolder))) return false;
        }

        if (csvAsset == null && string.IsNullOrWhiteSpace(csvPathOverride)) return false;
        return true;
    }

    private void Import()
    {
        string monsterRootPath = AssetDatabase.GetAssetPath(outputMonsterRootFolder);
        string titlesRootPath = titlesRootFolder ? AssetDatabase.GetAssetPath(titlesRootFolder) : null;
        string trackRootPath = titleTracksRootFolder ? AssetDatabase.GetAssetPath(titleTracksRootFolder) : null;
        string typeIconsPath = typeIconsRootFolder ? AssetDatabase.GetAssetPath(typeIconsRootFolder) : null;

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
        var existingById = IndexExistingMonsters(monsterRootPath);

        Dictionary<string, MonsterPersonalitySO> personalityByName = null;
        if (resolvePersonality)
            personalityByName = IndexPersonalities();

        Dictionary<string, TitleSO> titleByTitleId = null;
        if (reviewUpdateTitleTrack)
            titleByTitleId = IndexTitlesByTitleIdInFolder(titlesRootPath);

        Dictionary<MonsterType, Sprite> typeIconCache = null;
        if (autoAssignTypeIcon)
            typeIconCache = BuildTypeIconCache(typeIconsPath);

        var pendingEvolution = new Dictionary<MonsterDataSO, string>();

        // Defer file ops (move/rename) until after StopAssetEditing
        var deferredMonsterMoves = new List<(MonsterDataSO monster, string desiredFolder)>();
        var deferredMonsterRenames = new List<MonsterDataSO>();

        var deferredTrackMoves = new List<(TitleTrackSO track, string desiredFolder)>();
        var deferredTrackRenames = new List<(TitleTrackSO track, string desiredName)>();
        var deferredTrackTierSync = new List<(TitleTrackSO track, List<(int level, List<TitleSO> titles)> desiredTiers)>();

        int created = 0, updated = 0, skipped = 0, errors = 0;
        int movedMonsters = 0, renamedMonsters = 0;
        int movedTracks = 0, renamedTracks = 0, updatedTracks = 0, createdTracks = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var row in table.Rows)
            {
                try
                {
                    string id = Get(row, headerMap, "ID").Trim();
                    if (string.IsNullOrWhiteSpace(id)) { skipped++; continue; }

                    MonsterType parsedType = MonsterType.None;
                    TryParseEnum(Get(row, headerMap, "Type"), out parsedType);

                    bool has = existingById.TryGetValue(id, out var monster);
                    if (!has && !createIfMissing) { skipped++; continue; }
                    if (has && !updateExisting) { skipped++; continue; }

                    if (!has)
                    {
                        string folder = routeMonstersByTypeFolder ? EnsureTypeFolder(monsterRootPath, parsedType) : monsterRootPath;
                        monster = CreateMonsterAsset(folder, id);
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

                    // Type icon assignment
                    if (autoAssignTypeIcon && typeIconCache != null)
                    {
                        if (typeIconCache.TryGetValue(monster.type, out var sprite) && sprite != null)
                            monster.typeIcon = sprite;
                    }

                    // Defer monster move/rename
                    if (routeMonstersByTypeFolder && moveExistingMonstersToTypeFolder)
                    {
                        string desiredFolder = EnsureTypeFolder(monsterRootPath, monster.type);
                        deferredMonsterMoves.Add((monster, desiredFolder));
                    }

                    if (renameMonsterAssetsToMonsterName)
                        deferredMonsterRenames.Add(monster);

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

                    // Title Track sync (strongly typed now)
                    if (reviewUpdateTitleTrack)
                    {
                        if (string.IsNullOrWhiteSpace(trackRootPath) || !AssetDatabase.IsValidFolder(trackRootPath))
                            throw new Exception("TitleTracks root folder is missing/invalid.");

                        var track = monster.titleTrack;

                        if (track == null && createTitleTrackIfMissing)
                        {
                            string typeFolder = EnsureTypeFolder(trackRootPath, monster.type);
                            track = CreateTitleTrackAssetTyped(typeFolder, monster.displayName);
                            monster.titleTrack = track;
                            createdTracks++;
                        }

                        if (track != null)
                        {
                            if (moveTitleTracksToTypeFolder)
                            {
                                string desiredFolder = EnsureTypeFolder(trackRootPath, monster.type);
                                deferredTrackMoves.Add((track, desiredFolder));
                            }

                            if (renameTitleTracksToMonsterName)
                            {
                                string desiredName = $"TitleTrack_{ToAssetName(monster.displayName)}";
                                deferredTrackRenames.Add((track, desiredName));
                            }

                            if (syncTitleTrackTiersFromCsv)
                            {
                                var desiredTiers = BuildDesiredTitleTiersFromCsv(row, headerMap, titleByTitleId, id, logVerbose);
                                if (desiredTiers != null && desiredTiers.Count > 0)
                                    deferredTrackTierSync.Add((track, desiredTiers));
                            }
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

        // Save core edits first
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Apply deferred monster moves
        if (routeMonstersByTypeFolder && moveExistingMonstersToTypeFolder && deferredMonsterMoves.Count > 0)
        {
            foreach (var m in deferredMonsterMoves)
            {
                if (m.monster == null) continue;
                if (TryMoveAssetToFolder(m.monster, m.desiredFolder, out string moveErr))
                    movedMonsters++;
                else if (!string.IsNullOrWhiteSpace(moveErr))
                    Debug.LogWarning($"[MonsterCsvImporter] Monster move failed for {m.monster.id}: {moveErr}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Apply deferred monster renames
        if (renameMonsterAssetsToMonsterName && deferredMonsterRenames.Count > 0)
        {
            foreach (var monster in deferredMonsterRenames)
            {
                if (monster == null) continue;
                if (TryRenameAsset(monster, $"Monster_{ToAssetName(monster.displayName)}", out string renameErr))
                    renamedMonsters++;
                else if (!string.IsNullOrWhiteSpace(renameErr))
                    Debug.LogWarning($"[MonsterCsvImporter] Monster rename failed for {monster.id}: {renameErr}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Apply deferred track moves
        if (reviewUpdateTitleTrack && moveTitleTracksToTypeFolder && deferredTrackMoves.Count > 0)
        {
            foreach (var t in deferredTrackMoves)
            {
                if (t.track == null) continue;
                if (TryMoveAssetToFolder(t.track, t.desiredFolder, out string moveErr))
                    movedTracks++;
                else if (!string.IsNullOrWhiteSpace(moveErr))
                    Debug.LogWarning($"[MonsterCsvImporter] TitleTrack move failed: {moveErr}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Apply deferred track renames
        if (reviewUpdateTitleTrack && renameTitleTracksToMonsterName && deferredTrackRenames.Count > 0)
        {
            foreach (var r in deferredTrackRenames)
            {
                if (r.track == null) continue;
                if (TryRenameAsset(r.track, r.desiredName, out string renameErr))
                    renamedTracks++;
                else if (!string.IsNullOrWhiteSpace(renameErr))
                    Debug.LogWarning($"[MonsterCsvImporter] TitleTrack rename failed: {renameErr}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Apply track tier sync (strongly typed, safe)
        if (reviewUpdateTitleTrack && syncTitleTrackTiersFromCsv && deferredTrackTierSync.Count > 0)
        {
            foreach (var item in deferredTrackTierSync)
            {
                if (item.track == null) continue;

                if (ApplyTitleTrackIfDifferent(item.track, item.desiredTiers))
                {
                    updatedTracks++;
                    EditorUtility.SetDirty(item.track);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Second pass: resolve evolution forms (re-index after moves/renames)
        if (resolveEvolutionForm && pendingEvolution.Count > 0)
        {
            var refreshedById = IndexExistingMonsters(monsterRootPath);
            int linked = 0;

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

                if (refreshedById.TryGetValue(evoId, out var evo))
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

        Debug.Log(
            "[MonsterCsvImporter] Done. " +
            $"Created={created}, Updated={updated}, Skipped={skipped}, Errors={errors}. " +
            $"MonsterMoved={movedMonsters}, MonsterRenamed={renamedMonsters}. " +
            $"TracksCreated={createdTracks}, TracksMoved={movedTracks}, TracksRenamed={renamedTracks}, TracksUpdated={updatedTracks}."
        );
    }

    // ---------------- Title Track (typed) ----------------

    private static TitleTrackSO CreateTitleTrackAssetTyped(string folderPath, string monsterDisplayName)
    {
        string desiredName = $"TitleTrack_{ToAssetName(monsterDisplayName)}";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{desiredName}.asset");

        var so = ScriptableObject.CreateInstance<TitleTrackSO>();
        AssetDatabase.CreateAsset(so, assetPath);
        return so;
    }

    // Build desired tiers from CSV Title Track + Title Track Levels.
    // - Titles pipe list uses TitleSO.titleId
    // - Levels pipe list aligns by index; default level = 3 if missing.
    // - Titles sharing same level become choices in the same tier.
    private static List<(int level, List<TitleSO> titles)> BuildDesiredTitleTiersFromCsv(
        Dictionary<string, string> row,
        Dictionary<string, string> headerMap,
        Dictionary<string, TitleSO> titleById,
        string monsterId,
        bool logVerbose
    )
    {
        if (titleById == null) return null;

        string titlesPipe = Get(row, headerMap, "Title Track").Trim();
        if (string.IsNullOrWhiteSpace(titlesPipe))
            return null;

        string levelsPipe = Get(row, headerMap, "Title Track Levels").Trim();

        var titleIds = titlesPipe.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        string[] levelParts = string.IsNullOrWhiteSpace(levelsPipe)
            ? Array.Empty<string>()
            : levelsPipe.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

        var orderedLevels = new List<int>();
        var levelToTitles = new Dictionary<int, List<TitleSO>>();

        for (int i = 0; i < titleIds.Length; i++)
        {
            string tid = titleIds[i].Trim();
            if (string.IsNullOrWhiteSpace(tid)) continue;

            if (!titleById.TryGetValue(tid, out var title) || title == null)
            {
                if (logVerbose)
                    Debug.LogWarning($"[MonsterCsvImporter] TitleTrack: titleId '{tid}' not found (Monster {monsterId}).");
                continue;
            }

            int level = 3;
            if (i < levelParts.Length && TryInt(levelParts[i], out int parsed))
                level = Mathf.Max(1, parsed);

            if (!levelToTitles.TryGetValue(level, out var list))
            {
                list = new List<TitleSO>();
                levelToTitles[level] = list;
                orderedLevels.Add(level);
            }

            if (!list.Contains(title))
                list.Add(title);
        }

        if (orderedLevels.Count == 0)
            return null;

        var result = new List<(int level, List<TitleSO> titles)>();
        foreach (var lvl in orderedLevels)
            result.Add((lvl, levelToTitles[lvl]));

        return result;
    }

    private static bool ApplyTitleTrackIfDifferent(TitleTrackSO track, List<(int level, List<TitleSO> titles)> desired)
    {
        if (track == null || desired == null) return false;

        // Compare strictly (tier order and title order)
        if (TitleTrackEquals(track, desired))
            return false;

        track.tiers ??= new List<TitleTier>();
        track.tiers.Clear();

        foreach (var d in desired)
        {
            var tier = new TitleTier
            {
                levelRequired = Mathf.Max(1, d.level),
                unlockChoices = new List<TitleSO>()
            };

            if (d.titles != null)
                tier.unlockChoices.AddRange(d.titles);

            track.tiers.Add(tier);
        }

        return true;
    }

    private static bool TitleTrackEquals(TitleTrackSO track, List<(int level, List<TitleSO> titles)> desired)
    {
        if (track == null) return false;

        var a = track.tiers ?? new List<TitleTier>();
        if (a.Count != desired.Count) return false;

        for (int i = 0; i < desired.Count; i++)
        {
            var tier = a[i];
            if (tier == null) return false;

            if (tier.levelRequired != Mathf.Max(1, desired[i].level)) return false;

            var aChoices = tier.unlockChoices ?? new List<TitleSO>();
            var bChoices = desired[i].titles ?? new List<TitleSO>();

            if (aChoices.Count != bChoices.Count) return false;

            for (int j = 0; j < aChoices.Count; j++)
            {
                if (aChoices[j] != bChoices[j]) return false;
            }
        }

        return true;
    }

    // ---------------- Type Icon cache ----------------

    private static Dictionary<MonsterType, Sprite> BuildTypeIconCache(string typeIconsFolderPath)
    {
        var cache = new Dictionary<MonsterType, Sprite>();

        foreach (var kv in TYPE_ICON_FILE)
        {
            string fileName = kv.Value;
            Sprite sprite = FindSpriteByExactName(fileName, typeIconsFolderPath);
            if (sprite != null)
                cache[kv.Key] = sprite;
        }

        return cache;
    }

    private static Sprite FindSpriteByExactName(string name, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
            return null;

        string[] guids = AssetDatabase.FindAssets($"t:Sprite {name}", new[] { folder });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            if (s != null && string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase))
                return s;
        }

        if (guids.Length > 0)
        {
            string p = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }

        return null;
    }

    // ---------------- Generic rename/move helpers ----------------

    private static bool TryRenameAsset(UnityEngine.Object asset, string desiredName, out string error)
    {
        error = null;
        if (asset == null) return false;

        string assetPath = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(assetPath)) return false;

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

    // ---------------- Naming ----------------

    private static string ToAssetName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "";

        foreach (char c in Path.GetInvalidFileNameChars())
            displayName = displayName.Replace(c.ToString(), "");

        displayName = string.Concat(displayName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        if (displayName.Length == 0) return "";
        return char.ToUpperInvariant(displayName[0]) + displayName.Substring(1);
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

    // ---------------- Folder Routing ----------------

    private static string EnsureTypeFolder(string rootPath, MonsterType type)
    {
        string typeName = (type == MonsterType.None) ? "Unsorted" : type.ToString();
        string desired = $"{rootPath}/{typeName}";

        if (!AssetDatabase.IsValidFolder(desired))
            AssetDatabase.CreateFolder(rootPath, typeName);

        return desired;
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

    private static Dictionary<string, TitleSO> IndexTitlesByTitleIdInFolder(string titlesRootPath)
    {
        var dict = new Dictionary<string, TitleSO>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(titlesRootPath) || !AssetDatabase.IsValidFolder(titlesRootPath))
            return dict;

        string[] guids = AssetDatabase.FindAssets("t:TitleSO", new[] { titlesRootPath });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var so = AssetDatabase.LoadAssetAtPath<TitleSO>(p);
            if (so == null) continue;

            if (string.IsNullOrWhiteSpace(so.titleId)) continue;
            dict[so.titleId.Trim()] = so;
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
