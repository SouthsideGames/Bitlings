#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class MonsterBuilder : EditorWindow
{
    // Scroll
    private Vector2 _scroll;

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

    [Header("Monster Sprite Convention (Recommended)")]
    [Tooltip("If enabled, assigns icon/back/shiny sprites automatically using:\nAssets/Art/Monsters/<Type>/<Rarity>/<Name>_(front/back/frontshiny/backshiny)")]
    [SerializeField] private bool autoAssignMonsterSpritesByConvention = true;

    [Tooltip("Root folder for monster sprites: Assets/Art/Monsters")]
    [SerializeField] private DefaultAsset monsterSpritesRootFolder;     // Assets/Art/Monsters

    [Tooltip("Suffix for front sprite (Sprite name is <NormalizedName><Suffix>).")]
    [SerializeField] private string spriteSuffixFront = "_front";
    [Tooltip("Suffix for back sprite (Sprite name is <NormalizedName><Suffix>).")]
    [SerializeField] private string spriteSuffixBack = "_back";
    [Tooltip("Suffix for shiny front sprite (Sprite name is <NormalizedName><Suffix>).")]
    [SerializeField] private string spriteSuffixFrontShiny = "_frontshiny";
    [Tooltip("Suffix for shiny back sprite (Sprite name is <NormalizedName><Suffix>).")]
    [SerializeField] private string spriteSuffixBackShiny = "_backshiny";

    [Header("Art Pipeline (Pre-Import)")]
    [SerializeField] private bool runRenameBitlingsPreImport = true;

    [Tooltip("Option A: migrate from legacy per-monster folders into flat Assets/Art/Monsters/<Type>/<Rarity>/")]
    [SerializeField] private bool fixMonsterArtFolderNames = true;

    [Tooltip("Renames/moves legacy files (front_nobg, etc.) into strict <Token>_(front/back/frontshiny/backshiny).")]
    [SerializeField] private bool renameMonsterArtFilesToConvention = true;

    [Tooltip("Validates required sprites exist in Assets/Art/Monsters/<Type>/<Rarity> before import.")]
    [SerializeField] private bool validateMonsterArtBeforeImport = true;

    [Tooltip("If enabled and validation finds missing art, import will abort.")]
    [SerializeField] private bool abortImportIfArtMissing = false;

    [Header("Create / Update")]
    [SerializeField] private bool createIfMissing = true;
    [SerializeField] private bool updateExisting = true;

    [Header("Monster Folder Routing")]
    [SerializeField] private bool routeMonstersByTypeFolder = true;
    [SerializeField] private bool moveExistingMonstersToTypeFolder = true;

    [Header("Pack Routing")]
    [SerializeField] private bool routeMonstersToPackFolders = false;

    [Tooltip("If true, reads Pack Name from CSV column 'Pack Name'. If false, uses Pack Name Override for all rows.")]
    [SerializeField] private bool packNameFromCsv = true;

    [Tooltip("Used when Pack Name From CSV is OFF (applies to all rows). Also used when Force Single Pack Folder is ON.")]
    [SerializeField] private string packNameOverride = "Season_01";

    [Tooltip("If ON, ALL pack-routed monsters go into ONE pack folder (Pack Name Override). CSV Pack Name is ignored for folder routing.")]
    [SerializeField] private bool forceSinglePackFolder = true;

    [Header("Monster Asset Naming")]
    [Tooltip("If enabled, monster asset files are renamed to Monster_<NoSpacesName>.asset")]
    [SerializeField] private bool renameMonsterAssetsToMonsterName = true;

    [Tooltip("If enabled, also sets the ScriptableObject's internal name (so it matches the asset filename).")]
    [SerializeField] private bool syncMonsterScriptableObjectNameToAsset = true;

    [Header("Type Icon")]
    [Tooltip("If enabled, assigns MonsterDataSO.typeIcon using sprites in Assets/Art/Types based on type mapping.")]
    [SerializeField] private bool autoAssignTypeIcon = true;

    [Header("Sprites (Legacy via Asset Path Columns)")]
    [Tooltip("Optional/legacy: uses Icon Path/Back Icon Path/Shiny Icon Path/Shiny Back Icon Path columns.\nIf autoAssignMonsterSpritesByConvention is ON, this is usually unnecessary.")]
    [SerializeField] private bool importIconSpritesByPath = true;

    [Header("Title Track Sync (Skipped for Boss rarity)")]
    [SerializeField] private bool reviewUpdateTitleTrack = true;
    [SerializeField] private bool createTitleTrackIfMissing = true;
    [SerializeField] private bool moveTitleTracksToTypeFolder = true;
    [SerializeField] private bool renameTitleTracksToMonsterName = true;

    [Tooltip("If enabled, sync title tiers from CSV Title 1..N columns (one tier per title).")]
    [SerializeField] private bool syncTitleTrackTiersFromCsv = true;

    [Tooltip("If enabled, also sets the TitleTrackSO internal name (so it matches the asset filename).")]
    [SerializeField] private bool syncTitleTrackScriptableObjectNameToAsset = true;

    [Header("Always-On Titles (MonsterDataSO.defaultAlwaysOnTitles) (Skipped for Boss rarity)")]
    [SerializeField] private bool syncAlwaysOnTitlesFromCsv = true;

    [Header("Iron Titles (MonsterDataSO.ironTitles) (Skipped for Boss rarity)")]
    [SerializeField] private bool syncIronTitlesFromCsv = true;

    [Header("Personality")]
    [Tooltip("If enabled, resolves MonsterPersonalitySO by asset name in Assets/Resources/MonsterPersonalities")]
    [SerializeField] private bool resolvePersonality = true;

    [Header("Evolution")]
    [SerializeField] private bool resolveEvolutionForm = true;

    [Header("Deterministic Basic Attack Prefab By Type")]
    [Tooltip("If enabled, sets basicAttackPrefab to Assets/Prefab/Effects/TypeEffects/<Type>.prefab (if found).")]
    [SerializeField] private bool setBasicAttackPrefabByType = true;

    [Tooltip("Deterministic prefab folder path.")]
    [SerializeField] private string typeEffectPrefabFolder = "Assets/Prefab/Effects/TypeEffects";

    [SerializeField] private bool logVerbose = false;

    private const int TITLE_SLOTS = 5;

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

    // Column normalization map (case-insensitive)
    private static readonly Dictionary<string, string> COL = new(StringComparer.OrdinalIgnoreCase)
    {
        {"ID","ID"},
        {"Monster ID","ID"},

        {"Name","Name"},
        {"Display Name","Name"},

        {"Type","Type"},
        {"Rarity","Rarity"},

        {"Pack Name","Pack Name"},
        {"PackName","Pack Name"},

        {"Spawn Weight","Spawn Weight"},
        {"spawnWeight","Spawn Weight"},

        {"Personality","Personality"},
        {"workProfile","workProfile"},

        {"Base HP","Base HP"},
        {"Base Attack","Base Attack"},
        {"Base Defense","Base Defense"},
        {"baseDefense","Base Defense"},
        {"Base Speed","Base Speed"},

        {"Hp Regen Per Hour","Hp Regen Per Hour"},
        {"Fatigue Rate Per Hour","Fatigue Rate Per Hour"},
        {"Fatigue Cooldown Hours","Fatigue Cooldown Hours"},

        {"Job Skill","Job Skill"},
        {"Job","Job"},

        {"Evolution Stage","Evolution Stage"},
        {"Evolution Level","Evolution Level"},
        {"Evolution Form Id","Evolution Form Id"},

        {"Attack Name","Basic Attack Name"},
        {"Basic Attack Name","Basic Attack Name"},

        {"Description","Description"},

        // Legacy optional sprite paths
        {"Icon Path","Icon Path"},
        {"Back Icon Path","Back Icon Path"},
        {"Shiny Icon Path","Shiny Icon Path"},
        {"Shiny Back Icon Path","Shiny Back Icon Path"},

        // Titles: new explicit format
        {"Title 1","Title 1"},
        {"Title 1 Unlock Amount","Title 1 Unlock Amount"},
        {"Title 2","Title 2"},
        {"Title 2 Unlock Amount","Title 2 Unlock Amount"},
        {"Title 3","Title 3"},
        {"Title 3 Unlock Amount","Title 3 Unlock Amount"},
        {"Title 4","Title 4"},
        {"Title 4 Unlock Amount","Title 4 Unlock Amount"},
        {"Title 5","Title 5"},
        {"Title 5 Unlock Amount","Title 5 Unlock Amount"},

        // Always-on titles (optional)
        {"Always On Titles","Always On Titles"},

        // Iron titles (optional, pipe-separated)
        {"Iron Titles","Iron Titles"},

        // Starter
        {"Can Be Starter","Can Be Starter"},
        {"canBeStarter","Can Be Starter"},
        {"Starter Weight","Starter Weight"},
        {"starterWeight","Starter Weight"},

        // Max Level (optional override)
        {"Max Level","Max Level"},
        {"maxLevel","Max Level"},

        // Boss Weight
        {"Boss Weight","Boss Weight"},
        {"bossWeight","Boss Weight"},

        // Attack prefab lifetime
        {"Basic Attack Prefab Lifetime","Basic Attack Prefab Lifetime"},
        {"basicAttackPrefabLifetime","Basic Attack Prefab Lifetime"},

        // Exchange
        {"Base Market Value","Base Market Value"},
        {"baseMarketValue","Base Market Value"},
    };

    [MenuItem("Bitlings/Builder/Monsters From CSV")]
    public static void Open()
    {
        var win = GetWindow<MonsterBuilder>("Monster CSV Importer");
        win.minSize = new Vector2(740, 980);
    }

    private void OnEnable()
    {
        if (titlesRootFolder == null)
            titlesRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data/Title");
        if (titleTracksRootFolder == null)
            titleTracksRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data/TitleTracks");
        if (typeIconsRootFolder == null)
            typeIconsRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Art/Types");

        if (monsterSpritesRootFolder == null)
            monsterSpritesRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Art/Monsters");
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

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
        EditorGUILayout.LabelField("Monster Sprites (Convention)", EditorStyles.boldLabel);
        autoAssignMonsterSpritesByConvention = EditorGUILayout.Toggle("Auto-Assign Monster Sprites by Convention", autoAssignMonsterSpritesByConvention);
        using (new EditorGUI.DisabledScope(!autoAssignMonsterSpritesByConvention))
        {
            monsterSpritesRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Monster Sprites Root (Assets/Art/Monsters)", monsterSpritesRootFolder, typeof(DefaultAsset), false);
            spriteSuffixFront = EditorGUILayout.TextField("Front Suffix", spriteSuffixFront);
            spriteSuffixBack = EditorGUILayout.TextField("Back Suffix", spriteSuffixBack);
            spriteSuffixFrontShiny = EditorGUILayout.TextField("Front Shiny Suffix", spriteSuffixFrontShiny);
            spriteSuffixBackShiny = EditorGUILayout.TextField("Back Shiny Suffix", spriteSuffixBackShiny);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Art Pipeline (Pre-Import)", EditorStyles.boldLabel);
        runRenameBitlingsPreImport = EditorGUILayout.Toggle("Run RenameBitlings Pre-Import", runRenameBitlingsPreImport);
        using (new EditorGUI.DisabledScope(!runRenameBitlingsPreImport || !autoAssignMonsterSpritesByConvention))
        {
            fixMonsterArtFolderNames = EditorGUILayout.Toggle("Fix/Migrate Legacy Folders to Flat (Option A)", fixMonsterArtFolderNames);
            renameMonsterArtFilesToConvention = EditorGUILayout.Toggle("Rename/Move Legacy Files to Convention", renameMonsterArtFilesToConvention);
            validateMonsterArtBeforeImport = EditorGUILayout.Toggle("Validate Required Sprites (4)", validateMonsterArtBeforeImport);
            using (new EditorGUI.DisabledScope(!validateMonsterArtBeforeImport))
                abortImportIfArtMissing = EditorGUILayout.Toggle("Abort Import If Art Missing", abortImportIfArtMissing);
        }

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
        EditorGUILayout.LabelField("Pack Routing", EditorStyles.boldLabel);
        routeMonstersToPackFolders = EditorGUILayout.Toggle("Route Monsters To Pack Folders", routeMonstersToPackFolders);
        using (new EditorGUI.DisabledScope(!routeMonstersToPackFolders))
        {
            forceSinglePackFolder = EditorGUILayout.Toggle("Force Single Pack Folder", forceSinglePackFolder);

            using (new EditorGUI.DisabledScope(forceSinglePackFolder))
            {
                packNameFromCsv = EditorGUILayout.Toggle("Pack Name From CSV Column", packNameFromCsv);
            }

            packNameOverride = EditorGUILayout.TextField("Pack Name Override", packNameOverride);

            if (forceSinglePackFolder && string.IsNullOrWhiteSpace(packNameOverride))
            {
                EditorGUILayout.HelpBox("Force Single Pack Folder is ON, but Pack Name Override is blank. Pack routing will fall back to Main (non-pack).", MessageType.Warning);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Monster Asset Naming", EditorStyles.boldLabel);
        renameMonsterAssetsToMonsterName = EditorGUILayout.Toggle("Rename Monster Assets", renameMonsterAssetsToMonsterName);
        using (new EditorGUI.DisabledScope(!renameMonsterAssetsToMonsterName))
            syncMonsterScriptableObjectNameToAsset = EditorGUILayout.Toggle("Sync Monster SO .name to Asset", syncMonsterScriptableObjectNameToAsset);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Type Icon", EditorStyles.boldLabel);
        autoAssignTypeIcon = EditorGUILayout.Toggle("Auto-Assign typeIcon", autoAssignTypeIcon);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sprites (Legacy via Asset Path Columns)", EditorStyles.boldLabel);
        importIconSpritesByPath = EditorGUILayout.Toggle("Import icon/back/shiny sprites by Asset Path columns", importIconSpritesByPath);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Personality", EditorStyles.boldLabel);
        resolvePersonality = EditorGUILayout.Toggle("Resolve Personality (Assets/Resources/MonsterPersonalities)", resolvePersonality);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Deterministic Attack Prefab", EditorStyles.boldLabel);
        setBasicAttackPrefabByType = EditorGUILayout.Toggle("Set basicAttackPrefab by Type", setBasicAttackPrefabByType);
        using (new EditorGUI.DisabledScope(!setBasicAttackPrefabByType))
        {
            typeEffectPrefabFolder = EditorGUILayout.TextField("TypeEffects Prefab Folder", typeEffectPrefabFolder);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Title Track Sync (skipped for Boss rarity)", EditorStyles.boldLabel);
        reviewUpdateTitleTrack = EditorGUILayout.Toggle("Review/Update Title Track", reviewUpdateTitleTrack);
        using (new EditorGUI.DisabledScope(!reviewUpdateTitleTrack))
        {
            createTitleTrackIfMissing = EditorGUILayout.Toggle("Create Track If Missing", createTitleTrackIfMissing);
            moveTitleTracksToTypeFolder = EditorGUILayout.Toggle("Move Tracks To Type Folder", moveTitleTracksToTypeFolder);
            renameTitleTracksToMonsterName = EditorGUILayout.Toggle("Rename Track Assets", renameTitleTracksToMonsterName);
            using (new EditorGUI.DisabledScope(!renameTitleTracksToMonsterName))
                syncTitleTrackScriptableObjectNameToAsset = EditorGUILayout.Toggle("Sync Track SO .name to Asset", syncTitleTrackScriptableObjectNameToAsset);

            syncTitleTrackTiersFromCsv = EditorGUILayout.Toggle("Sync Track Tiers From CSV (Title 1..5)", syncTitleTrackTiersFromCsv);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Always-On Titles (skipped for Boss rarity)", EditorStyles.boldLabel);
        syncAlwaysOnTitlesFromCsv = EditorGUILayout.Toggle("Sync defaultAlwaysOnTitles From CSV", syncAlwaysOnTitlesFromCsv);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Iron Titles (skipped for Boss rarity)", EditorStyles.boldLabel);
        syncIronTitlesFromCsv = EditorGUILayout.Toggle("Sync ironTitles From CSV", syncIronTitlesFromCsv);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Evolution", EditorStyles.boldLabel);
        resolveEvolutionForm = EditorGUILayout.Toggle("Resolve Evolution Form (2nd pass)", resolveEvolutionForm);

        EditorGUILayout.Space();
        logVerbose = EditorGUILayout.Toggle("Verbose Logs", logVerbose);

        EditorGUILayout.Space(18);

        using (new EditorGUI.DisabledScope(!CanRun()))
        {
            if (GUILayout.Button("Import / Update Monsters", GUILayout.Height(42)))
                Import();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Notes:\n" +
            "- Monster assets can be renamed to Monster_<Token>.asset.\n" +
            "- If 'Sync Monster SO .name to Asset' is enabled, the ScriptableObject internal name is also updated.\n" +
            "- Pack Routing: With 'Force Single Pack Folder' ON, all pack monsters route to Packs/<PackNameOverride>/<Rarity>.\n",
            MessageType.Info
        );

        EditorGUILayout.EndScrollView();
    }

    private bool CanRun()
    {
        if (outputMonsterRootFolder == null) return false;
        if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(outputMonsterRootFolder))) return false;
        if (csvAsset == null && string.IsNullOrWhiteSpace(csvPathOverride)) return false;

        if ((reviewUpdateTitleTrack || syncAlwaysOnTitlesFromCsv || syncIronTitlesFromCsv))
        {
            if (titlesRootFolder == null) return false;
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(titlesRootFolder))) return false;
        }

        if (reviewUpdateTitleTrack)
        {
            if (titleTracksRootFolder == null) return false;
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(titleTracksRootFolder))) return false;
        }

        if (autoAssignTypeIcon)
        {
            if (typeIconsRootFolder == null) return false;
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(typeIconsRootFolder))) return false;
        }

        if (autoAssignMonsterSpritesByConvention)
        {
            if (monsterSpritesRootFolder == null) return false;
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(monsterSpritesRootFolder))) return false;
        }

        if (setBasicAttackPrefabByType)
        {
            if (string.IsNullOrWhiteSpace(typeEffectPrefabFolder)) return false;
            if (!AssetDatabase.IsValidFolder(typeEffectPrefabFolder)) return false;
        }

        return true;
    }

    private void Import()
    {
        string monsterRootPath = AssetDatabase.GetAssetPath(outputMonsterRootFolder);
        string titlesRootPath = titlesRootFolder ? AssetDatabase.GetAssetPath(titlesRootFolder) : null;
        string trackRootPath = titleTracksRootFolder ? AssetDatabase.GetAssetPath(titleTracksRootFolder) : null;
        string typeIconsPath = typeIconsRootFolder ? AssetDatabase.GetAssetPath(typeIconsRootFolder) : null;
        string monsterSpritesRootPath = monsterSpritesRootFolder ? AssetDatabase.GetAssetPath(monsterSpritesRootFolder) : null;

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

        // ---- Pre-import Art Pipeline (Option A) ----
        if (runRenameBitlingsPreImport &&
            autoAssignMonsterSpritesByConvention &&
            !string.IsNullOrWhiteSpace(monsterSpritesRootPath) &&
            AssetDatabase.IsValidFolder(monsterSpritesRootPath))
        {
            var monsterRows = new List<(MonsterType type, Rarity rarity, string displayName)>();
            foreach (var row in table.Rows)
            {
                string name = Get(row, headerMap, "Name").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                MonsterType t = MonsterType.None;
                TryParseEnum(Get(row, headerMap, "Type"), out t);

                Rarity r = Rarity.Common;
                TryParseEnum(Get(row, headerMap, "Rarity"), out r);

                monsterRows.Add((t, r, name));
            }

            var report = RenameBitlingsUtility.RunPreImportPipeline(
                targetRootArtMonsters: monsterSpritesRootPath,
                monsters: monsterRows,
                fixFolderNamesAndMigrateToFlat: fixMonsterArtFolderNames,
                renameFilesToConvention: renameMonsterArtFilesToConvention,
                validateRequiredSprites: validateMonsterArtBeforeImport,
                dryRun: false,
                logVerbose: logVerbose
            );

            if (report.HasIssues || report.Info.Count > 0 || report.Skipped.Count > 0)
            {
                var msg = "[MonsterCsvImporter] RenameBitlings Pre-Import Report:\n" + report.ToSummaryString();
                if (report.HasIssues) Debug.LogWarning(msg);
                else Debug.Log(msg);
            }

            if (abortImportIfArtMissing && report.Missing.Count > 0)
            {
                Debug.LogError("[MonsterCsvImporter] Import aborted due to missing monster art (Abort Import If Art Missing = ON).");
                return;
            }
        }

        var existingById = IndexExistingMonsters(monsterRootPath);

        Dictionary<string, TitleSO> titleByTitleId = null;
        if (reviewUpdateTitleTrack || syncAlwaysOnTitlesFromCsv || syncIronTitlesFromCsv)
            titleByTitleId = IndexTitlesByTitleIdInFolder(titlesRootPath);

        Dictionary<MonsterType, Sprite> typeIconCache = null;
        if (autoAssignTypeIcon)
            typeIconCache = BuildTypeIconCache(typeIconsPath);

        Dictionary<string, MonsterPersonalitySO> personalityByName = null;
        if (resolvePersonality)
            personalityByName = IndexPersonalitiesFromResources();

        var pendingEvolution = new Dictionary<MonsterDataSO, string>();

        var deferredMonsterMoves = new List<(MonsterDataSO monster, string desiredFolder)>();
        var deferredMonsterRenames = new List<MonsterDataSO>();

        var deferredTrackMoves = new List<(TitleTrackSO track, string desiredFolder)>();
        var deferredTrackRenames = new List<(TitleTrackSO track, string desiredName)>();
        var deferredTrackTierSync = new List<(TitleTrackSO track, List<(int level, List<TitleSO> titles)> desiredTiers)>();

        int created = 0, updated = 0, skipped = 0, errors = 0;
        int mainRows = 0, packRows = 0;
        int movedMonsters = 0, renamedMonsters = 0;
        int movedTracks = 0, renamedTracks = 0, updatedTracks = 0, createdTracks = 0;
        int updatedAlwaysOnTitles = 0;
        int updatedIronTitles = 0;
        int setAttackPrefabs = 0;
        int setSpriteRefs = 0;
        int setMonsterSpritesByConvention = 0;

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

                    // ---- Pack routing decision (UPDATED) ----
                    string packNameCellRaw = Get(row, headerMap, "Pack Name").Trim();

                    // Determine the "intended" pack name based on settings.
                    // If ForceSinglePackFolder is ON, we will use packNameOverride for folder routing.
                    string packNameIntended;
                    if (routeMonstersToPackFolders && forceSinglePackFolder)
                        packNameIntended = (packNameOverride ?? "").Trim();
                    else
                        packNameIntended = packNameFromCsv ? packNameCellRaw : (packNameOverride ?? "").Trim();

                    bool isMainMonster = IsMainPackName(packNameIntended);
                    bool isPackMonster = routeMonstersToPackFolders && !isMainMonster && !string.IsNullOrWhiteSpace(packNameIntended);

                    // Counts for summary (valid ID rows only)
                    if (isPackMonster) packRows++; else mainRows++;

                    bool has = existingById.TryGetValue(id, out var monster);
                    if (!has && !createIfMissing) { skipped++; continue; }
                    if (has && !updateExisting) { skipped++; continue; }

                    if (!has)
                    {
                        string folder;

                        if (isPackMonster)
                        {
                            // Uses: Assets/Data/Monsters/Packs/<PackName>/<Rarity>
                            Rarity rParsed = Rarity.Common;
                            TryParseEnum(Get(row, headerMap, "Rarity"), out rParsed);
                            folder = EnsurePackRarityFolder(monsterRootPath, packNameIntended, rParsed);
                        }
                        else
                        {
                            folder = routeMonstersByTypeFolder ? EnsureTypeFolder(monsterRootPath, parsedType) : monsterRootPath;
                        }

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

                    // MaxLevel (CSV override or default 50)
                    if (TryInt(Get(row, headerMap, "Max Level"), out int ml))
                        monster.maxLevel = Mathf.Max(1, ml);
                    else
                        monster.maxLevel = 50;

                    // Boss rule enforced only by rarity
                    bool boss = monster.rarity == Rarity.Boss;
                    monster.isBoss = boss;
                    monster.uncatchable = boss;

                    // Starter
                    string starterStr = Get(row, headerMap, "Can Be Starter").Trim();
                    if (!string.IsNullOrWhiteSpace(starterStr))
                        monster.canBeStarter = starterStr.Equals("true", StringComparison.OrdinalIgnoreCase) || starterStr == "1";
                    if (boss) monster.canBeStarter = false;

                    if (TryInt(Get(row, headerMap, "Starter Weight"), out int stw))
                        monster.starterWeight = Mathf.Max(0, stw);

                    if (boss)
                    {
                        monster.titleTrack = null;
                        monster.defaultAlwaysOnTitles = Array.Empty<TitleSO>();
                    }

                    // Type icon assignment
                    if (autoAssignTypeIcon && typeIconCache != null)
                    {
                        if (typeIconCache.TryGetValue(monster.type, out var sprite) && sprite != null)
                            monster.typeIcon = sprite;
                    }

                    // Monster sprites by convention (Option A flat folder)
                    if (autoAssignMonsterSpritesByConvention && !string.IsNullOrWhiteSpace(monsterSpritesRootPath))
                    {
                        if (TryAssignMonsterSpritesByConvention(monster, monsterSpritesRootPath, out int assignedCount))
                        {
                            if (assignedCount > 0) setMonsterSpritesByConvention++;
                        }
                        else if (logVerbose)
                        {
                            Debug.LogWarning($"[MonsterCsvImporter] Could not assign monster sprites by convention for {monster.id} (check folder/names).");
                        }
                    }

                    // Legacy sprite paths (optional override)
                    if (importIconSpritesByPath)
                    {
                        bool any = false;
                        any |= TryAssignSpriteByPath(Get(row, headerMap, "Icon Path"), ref monster.icon);
                        any |= TryAssignSpriteByPath(Get(row, headerMap, "Back Icon Path"), ref monster.backIcon);
                        any |= TryAssignSpriteByPath(Get(row, headerMap, "Shiny Icon Path"), ref monster.shinyIcon);
                        any |= TryAssignSpriteByPath(Get(row, headerMap, "Shiny Back Icon Path"), ref monster.shinyBackIcon);
                        if (any) setSpriteRefs++;
                    }

                    // Defer monster move/rename (never move pack monsters into type folders)
                    if (!isPackMonster && routeMonstersByTypeFolder && moveExistingMonstersToTypeFolder)
                    {
                        string desiredFolder = EnsureTypeFolder(monsterRootPath, monster.type);
                        deferredMonsterMoves.Add((monster, desiredFolder));
                    }

                    if (renameMonsterAssetsToMonsterName)
                        deferredMonsterRenames.Add(monster);

                    // Encounter
                    if (TryFloat(Get(row, headerMap, "Spawn Weight"), out float sw))
                        monster.spawnWeight = Mathf.Max(0f, sw);

                    // Boss Weight
                    if (TryInt(Get(row, headerMap, "Boss Weight"), out int bw))
                        monster.bossWeight = Mathf.Max(1, bw);

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
                    if (TryInt(Get(row, headerMap, "Evolution Stage"), out int es)) monster.evolutionStage = Mathf.Max(0, es);
                    if (TryInt(Get(row, headerMap, "Evolution Level"), out int el)) monster.evolutionLevel = Mathf.Max(0, el);

                    

                    // If this monster has an evolution stage but no explicit evolution level, derive it from rarity.
                    // (Keeps single-stage monsters at 0.)
                    if (monster.evolutionStage <= 0)
                    {
                        monster.evolutionLevel = 0;
                    }
                    else if (monster.evolutionLevel <= 0)
                    {
                        monster.evolutionLevel = DefaultEvolutionLevel(monster.rarity);
                    }

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
                            Debug.LogWarning($"[MonsterCsvImporter] Personality '{p}' not found in Resources/MonsterPersonalities (Monster {id})");
                    }

                    // Basic attack name
                    string atkName = Get(row, headerMap, "Basic Attack Name");
                    if (!string.IsNullOrWhiteSpace(atkName)) monster.basicAttackName = atkName.Trim();

                    // Deterministic basicAttackPrefab by type
                    if (setBasicAttackPrefabByType)
                    {
                        var prefab = LoadTypeEffectPrefab(monster.type, typeEffectPrefabFolder);
                        if (prefab != null)
                        {
                            monster.basicAttackPrefab = prefab;
                            setAttackPrefabs++;
                        }
                        else if (logVerbose)
                        {
                            Debug.LogWarning($"[MonsterCsvImporter] Missing TypeEffect prefab for type '{monster.type}' at {typeEffectPrefabFolder}/{monster.type}.prefab");
                        }
                    }

                    // Basic Attack Prefab Lifetime
                    if (TryFloat(Get(row, headerMap, "Basic Attack Prefab Lifetime"), out float atkLife))
                        monster.basicAttackPrefabLifetime = Mathf.Max(0f, atkLife);

                    // Description
                    string desc = Get(row, headerMap, "Description");
                    if (!string.IsNullOrWhiteSpace(desc)) monster.description = desc.Trim();

                    // Base Market Value
                    if (TryInt(Get(row, headerMap, "Base Market Value"), out int bmv))
                        monster.baseMarketValue = Mathf.Max(0, bmv);

                    // Always-On Titles (skipped for Boss)
                    if (!boss && syncAlwaysOnTitlesFromCsv && titleByTitleId != null)
                    {
                        string always = Get(row, headerMap, "Always On Titles").Trim();
                        if (!string.IsNullOrWhiteSpace(always))
                        {
                            var titles = ResolveTitleIdList(always, titleByTitleId);
                            if (!TitleArrayEquals(monster.defaultAlwaysOnTitles, titles))
                            {
                                monster.defaultAlwaysOnTitles = titles;
                                updatedAlwaysOnTitles++;
                            }
                        }
                    }

                    // Iron Titles (skipped for Boss)
                    if (!boss && syncIronTitlesFromCsv && titleByTitleId != null)
                    {
                        string ironStr = Get(row, headerMap, "Iron Titles").Trim();
                        if (!string.IsNullOrWhiteSpace(ironStr))
                        {
                            var ironTitles = ResolveTitleIdList(ironStr, titleByTitleId);
                            if (!TitleArrayEquals(monster.ironTitles, ironTitles))
                            {
                                monster.ironTitles = ironTitles;
                                updatedIronTitles++;
                            }
                        }
                    }

                    // Title Track sync (skipped for Boss)
                    if (!boss && reviewUpdateTitleTrack)
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
                                var desiredTiers = BuildDesiredTitleTiersFromCsv_Slots(row, headerMap, titleByTitleId, id, logVerbose);
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

        // Apply deferred monster renames (and sync ScriptableObject .name)
        if (renameMonsterAssetsToMonsterName && deferredMonsterRenames.Count > 0)
        {
            foreach (var monster in deferredMonsterRenames)
            {
                if (monster == null) continue;

                string desired = $"Monster_{ToAssetName(monster.displayName)}";

                bool renamed = TryRenameAsset(monster, desired, out string renameErr);
                if (renamed)
                {
                    renamedMonsters++;
                    if (syncMonsterScriptableObjectNameToAsset)
                        SyncObjectNameToAssetFile(monster);
                }
                else
                {
                    if (syncMonsterScriptableObjectNameToAsset)
                        SyncObjectNameToAssetFile(monster);

                    if (!string.IsNullOrWhiteSpace(renameErr))
                        Debug.LogWarning($"[MonsterCsvImporter] Monster rename failed for {monster.id}: {renameErr}");
                }
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

        // Apply deferred track renames (and sync ScriptableObject .name)
        if (reviewUpdateTitleTrack && renameTitleTracksToMonsterName && deferredTrackRenames.Count > 0)
        {
            foreach (var r in deferredTrackRenames)
            {
                if (r.track == null) continue;

                bool renamed = TryRenameAsset(r.track, r.desiredName, out string renameErr);
                if (renamed)
                {
                    renamedTracks++;
                    if (syncTitleTrackScriptableObjectNameToAsset)
                        SyncObjectNameToAssetFile(r.track);
                }
                else
                {
                    if (syncTitleTrackScriptableObjectNameToAsset)
                        SyncObjectNameToAssetFile(r.track);

                    if (!string.IsNullOrWhiteSpace(renameErr))
                        Debug.LogWarning($"[MonsterCsvImporter] TitleTrack rename failed: {renameErr}");
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Apply track tier sync
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

        // Second pass: resolve evolution forms
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
            $"RowsMain={mainRows}, RowsPack={packRows}. " +
            $"Created={created}, Updated={updated}, Skipped={skipped}, Errors={errors}. " +
            $"MonsterMoved={movedMonsters}, MonsterRenamed={renamedMonsters}. " +
            $"TracksCreated={createdTracks}, TracksMoved={movedTracks}, TracksRenamed={renamedTracks}, TracksUpdated={updatedTracks}. " +
            $"AlwaysOnTitlesUpdated={updatedAlwaysOnTitles}, IronTitlesUpdated={updatedIronTitles}, TypeAttackPrefabsSet={setAttackPrefabs}, " +
            $"LegacySpriteRefsSet={setSpriteRefs}, ConventionSpritesSet={setMonsterSpritesByConvention}."
        );
    }

    // ---------------- NEW: Sync ScriptableObject internal name to asset filename ----------------

    private static void SyncObjectNameToAssetFile(UnityEngine.Object asset)
    {
        if (asset == null) return;

        string p = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(p)) return;

        string fileName = Path.GetFileNameWithoutExtension(p);
        if (string.IsNullOrWhiteSpace(fileName)) return;

        if (asset.name == fileName) return;

        asset.name = fileName;
        EditorUtility.SetDirty(asset);
    }

    // ---------------- Title Track tier build (Title 1..N Slots) ----------------

    private static List<(int level, List<TitleSO> titles)> BuildDesiredTitleTiersFromCsv_Slots(
        Dictionary<string, string> row,
        Dictionary<string, string> headerMap,
        Dictionary<string, TitleSO> titleById,
        string monsterId,
        bool logVerbose
    )
    {
        if (titleById == null) return null;

        var result = new List<(int level, List<TitleSO> titles)>();

        for (int i = 1; i <= TITLE_SLOTS; i++)
        {
            string titleKey = $"Title {i}";
            string levelKey = $"Title {i} Unlock Amount";

            string tid = Get(row, headerMap, titleKey).Trim();

            if (IsNaOrEmpty(tid))
                continue;

            if (!titleById.TryGetValue(tid, out var title) || title == null)
            {
                if (logVerbose)
                    Debug.LogWarning($"[MonsterCsvImporter] TitleTrack: titleId '{tid}' not found (Monster {monsterId}).");
                continue;
            }

            int level = 3; // default
            string lvlStr = Get(row, headerMap, levelKey).Trim();
            if (!IsNaOrEmpty(lvlStr) && TryInt(lvlStr, out int parsed))
                level = Mathf.Max(1, parsed);

            // One tier per title unlock
            result.Add((level, new List<TitleSO> { title }));
        }

        return result.Count > 0 ? result : null;
    }

    private static bool IsNaOrEmpty(string s)
    {
        s = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return true;
        return string.Equals(s, "N/A", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(s, "NA", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(s, "-", StringComparison.OrdinalIgnoreCase);
    }

    // ---------------- Monster Sprite Convention (Option A flat folder) ----------------

    private bool TryAssignMonsterSpritesByConvention(MonsterDataSO monster, string rootPath, out int assignedCount)
    {
        assignedCount = 0;
        if (monster == null) return false;
        if (string.IsNullOrWhiteSpace(rootPath) || !AssetDatabase.IsValidFolder(rootPath)) return false;

        string typeFolder = monster.type == MonsterType.None ? "Unsorted" : monster.type.ToString();
        string rarityFolder = monster.rarity.ToString();

        string folder = $"{rootPath}/{typeFolder}/{rarityFolder}";
        if (!AssetDatabase.IsValidFolder(folder)) return false;

        string token = NormalizeMonsterToken(monster.displayName);
        if (string.IsNullOrWhiteSpace(token)) return false;

        var front = LoadSpriteByNameInFolder(folder, token + spriteSuffixFront);
        var back = LoadSpriteByNameInFolder(folder, token + spriteSuffixBack);
        var frontShiny = LoadSpriteByNameInFolder(folder, token + spriteSuffixFrontShiny);
        var backShiny = LoadSpriteByNameInFolder(folder, token + spriteSuffixBackShiny);

        if (front != null && monster.icon != front) { monster.icon = front; assignedCount++; }
        if (back != null && monster.backIcon != back) { monster.backIcon = back; assignedCount++; }
        if (frontShiny != null && monster.shinyIcon != frontShiny) { monster.shinyIcon = frontShiny; assignedCount++; }
        if (backShiny != null && monster.shinyBackIcon != backShiny) { monster.shinyBackIcon = backShiny; assignedCount++; }

        return true;
    }

    private static Sprite LoadSpriteByNameInFolder(string folder, string spriteName)
    {
        if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
            return null;

        string[] guids = AssetDatabase.FindAssets($"t:Sprite {spriteName}", new[] { folder });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            if (s != null && string.Equals(s.name, spriteName, StringComparison.OrdinalIgnoreCase))
                return s;
        }

        if (guids.Length > 0)
        {
            string p = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }

        return null;
    }

    private static string NormalizeMonsterToken(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "";

        foreach (char c in Path.GetInvalidFileNameChars())
            displayName = displayName.Replace(c.ToString(), "");

        // Remove ALL whitespace
        string token = string.Concat(displayName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        if (token.Length == 0) return "";

        return char.ToUpperInvariant(token[0]) + token.Substring(1);
    }

    // ---------------- Deterministic Type Effect Prefab ----------------

    private static GameObject LoadTypeEffectPrefab(MonsterType type, string folder)
    {
        if (type == MonsterType.None) return null;
        if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder)) return null;

        string path = $"{folder}/{type}.prefab";
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go != null) return go;

        string[] guids = AssetDatabase.FindAssets($"t:GameObject {type}", new[] { folder });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (candidate != null && string.Equals(candidate.name, type.ToString(), StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    // ---------------- Sprites by Asset Path (Legacy) ----------------

    private static bool TryAssignSpriteByPath(string path, ref Sprite targetField)
    {
        path = (path ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path)) return false;
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (s == null) return false;
        if (targetField == s) return false;
        targetField = s;
        return true;
    }

    // ---------------- Always-On Titles ----------------

    private static TitleSO[] ResolveTitleIdList(string pipeList, Dictionary<string, TitleSO> titleById)
    {
        var parts = pipeList.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<TitleSO>();

        foreach (var p in parts)
        {
            var key = (p ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (titleById.TryGetValue(key, out var so) && so != null)
            {
                if (!list.Contains(so))
                    list.Add(so);
            }
        }

        return list.ToArray();
    }

    private static bool TitleArrayEquals(TitleSO[] a, TitleSO[] b)
    {
        a ??= Array.Empty<TitleSO>();
        b ??= Array.Empty<TitleSO>();
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
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

    private static bool ApplyTitleTrackIfDifferent(TitleTrackSO track, List<(int level, List<TitleSO> titles)> desired)
    {
        if (track == null || desired == null) return false;

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
                if (aChoices[j] != bChoices[j]) return false;
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

    // ---------------- Personality from Resources ----------------

    private static Dictionary<string, MonsterPersonalitySO> IndexPersonalitiesFromResources()
    {
        var dict = new Dictionary<string, MonsterPersonalitySO>(StringComparer.OrdinalIgnoreCase);
        var all = Resources.LoadAll<MonsterPersonalitySO>("MonsterPersonalities");
        foreach (var p in all)
        {
            if (p == null) continue;
            dict[p.name.Trim()] = p;
        }
        return dict;
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

        // remove whitespace
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

    private static bool IsMainPackName(string packName)
    {
        packName = (packName ?? "").Trim();

        // Treat blank and NA-like as "Main"
        if (string.IsNullOrWhiteSpace(packName)) return true;
        if (string.Equals(packName, "N/A", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(packName, "NA", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(packName, "-", StringComparison.OrdinalIgnoreCase)) return true;

        return packName.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizePackFolderName(string packName)
    {
        packName = (packName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(packName)) return "Unsorted";

        // normalize whitespace -> underscores (prevents "Season 01" vs "Season_01")
        packName = string.Join("_", packName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        foreach (char c in Path.GetInvalidFileNameChars())
            packName = packName.Replace(c.ToString(), "");

        if (string.IsNullOrWhiteSpace(packName)) return "Unsorted";
        return packName;
    }

    private static string EnsureFolder(string parent, string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Unsorted";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "");

        if (string.IsNullOrWhiteSpace(name)) name = "Unsorted";

        string desired = $"{parent}/{name}";
        if (!AssetDatabase.IsValidFolder(desired))
            AssetDatabase.CreateFolder(parent, name);

        return desired;
    }

    private static string EnsurePackRarityFolder(string monsterRootPath, string packName, Rarity rarity)
    {
        // Defensive guard: "Main"/blank/NA is not a real pack and should never create Packs/Main.
        if (IsMainPackName(packName))
            throw new ArgumentException("Pack Name is 'Main' (or blank/NA). This row should be routed as a non-pack monster.", nameof(packName));

        packName = NormalizePackFolderName(packName);

        // Assets/Data/Monsters/Packs/<PackName>/<Rarity>
        string packsRoot = EnsureFolder(monsterRootPath, "Packs");
        string packRoot = EnsureFolder(packsRoot, packName);
        string rarityFolder = EnsureFolder(packRoot, rarity.ToString());
        return rarityFolder;
    }

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

    private static int DefaultEvolutionLevel(Rarity rarity)
    {
        // Defaults that scale with rarity. Tune here as needed.
        return rarity switch
        {
            Rarity.Common    => 8,
            Rarity.Uncommon  => 10,
            Rarity.Rare      => 12,
            Rarity.Epic      => 15,
            Rarity.Legendary => 18,
            _                => 12
        };
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
