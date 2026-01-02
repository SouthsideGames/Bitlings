#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class RenameBitlings : EditorWindow
{
    [Header("Roots - Main Monsters (Type/Rarity folders)")]
    [SerializeField] private DefaultAsset artMonstersRoot; // Assets/Art/Monsters

    [Header("Roots - Pack Monsters (per-pack folders)")]
    [Tooltip("Pack monster art root. Expected: Assets/Monsters/Packs/<Pack Name>/<Monster Name>/")]
    [SerializeField] private DefaultAsset packMonstersRoot; // Assets/Monsters/Packs
    [SerializeField] private bool includePackMonsters = true;

    [Header("CSV (Optional)")]
    [Tooltip("If provided, tool validates/renames only monsters found in the CSV. CSV must include Name, Rarity, Type, and Pack Name (Pack Name='Main' means not a pack).")]
    [SerializeField] private UnityEngine.Object csvAsset;

    [Header("Actions")]
    [SerializeField] private bool fixFoldersAndMigrateLegacy = true; // currently ignored by utility (no moving)
    [SerializeField] private bool renameLegacyFilesToConvention = true;
    [SerializeField] private bool validateRequiredSprites = true;

    [Header("Safety")]
    [SerializeField] private bool dryRun = false;
    [SerializeField] private bool verboseLogs = false;

    private Vector2 _scroll;
    private string _lastReport;

    [MenuItem("Bitlings/Art/RenameBitlings")]
    public static void Open()
    {
        var win = GetWindow<RenameBitlings>("RenameBitlings");
        win.minSize = new Vector2(720, 700);
    }

    private void OnEnable()
    {
        if (artMonstersRoot == null)
            artMonstersRoot = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Art/Monsters");

        if (packMonstersRoot == null)
            packMonstersRoot = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Monsters/Packs");
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("RenameBitlings", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        artMonstersRoot = (DefaultAsset)EditorGUILayout.ObjectField("Main Art Root (Assets/Art/Monsters)", artMonstersRoot, typeof(DefaultAsset), false);

        EditorGUILayout.Space(6);
        includePackMonsters = EditorGUILayout.ToggleLeft("Include Pack Monsters", includePackMonsters);
        using (new EditorGUI.DisabledScope(!includePackMonsters))
        {
            packMonstersRoot = (DefaultAsset)EditorGUILayout.ObjectField("Pack Art Root (Assets/Monsters/Packs)", packMonstersRoot, typeof(DefaultAsset), false);
        }

        EditorGUILayout.Space(10);
        csvAsset = EditorGUILayout.ObjectField("CSV (optional)", csvAsset, typeof(UnityEngine.Object), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pipeline", EditorStyles.boldLabel);
        fixFoldersAndMigrateLegacy = EditorGUILayout.ToggleLeft("Fix Folder Names (legacy migrate - currently ignored; no moves)", fixFoldersAndMigrateLegacy);
        renameLegacyFilesToConvention = EditorGUILayout.ToggleLeft("Rename legacy files to strict convention (<Token>_front/_back/_frontshiny/_backshiny)", renameLegacyFilesToConvention);
        validateRequiredSprites = EditorGUILayout.ToggleLeft("Validate required sprites (4)", validateRequiredSprites);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Safety", EditorStyles.boldLabel);
        dryRun = EditorGUILayout.ToggleLeft("Dry Run (no changes)", dryRun);
        verboseLogs = EditorGUILayout.ToggleLeft("Verbose Logs", verboseLogs);

        EditorGUILayout.Space(12);

        using (new EditorGUI.DisabledScope(!CanRun()))
        {
            if (GUILayout.Button("Run RenameBitlings", GUILayout.Height(42)))
                Run();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last Report", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextArea(string.IsNullOrWhiteSpace(_lastReport) ? "(none yet)" : _lastReport, GUILayout.MinHeight(260));
        }

        EditorGUILayout.EndScrollView();
    }

    private bool CanRun()
    {
        if (artMonstersRoot == null) return false;
        var p = AssetDatabase.GetAssetPath(artMonstersRoot);
        if (!AssetDatabase.IsValidFolder(p)) return false;

        if (includePackMonsters)
        {
            if (packMonstersRoot == null) return false;
            var pp = AssetDatabase.GetAssetPath(packMonstersRoot);
            if (!AssetDatabase.IsValidFolder(pp)) return false;
        }

        return true;
    }

    private void Run()
    {
        string mainRoot = AssetDatabase.GetAssetPath(artMonstersRoot);
        string packsRoot = includePackMonsters ? AssetDatabase.GetAssetPath(packMonstersRoot) : null;

        List<(MonsterType type, Rarity rarity, string displayName)> mainMonsters = new();
        List<(string packName, string displayName)> packMonsters = new();

        if (csvAsset != null)
        {
            if (!RenameBitlingsUtility.TryReadCsvMonstersV2(csvAsset, out mainMonsters, out packMonsters, out string err))
            {
                Debug.LogError($"[RenameBitlings] CSV read failed: {err}");
                return;
            }
        }
        else
        {
            mainMonsters = RenameBitlingsUtility.ScanMonstersFromArtRoot(mainRoot);
            if (includePackMonsters)
                packMonsters = RenameBitlingsUtility.ScanMonstersFromPackRoot(packsRoot);
        }

        var rep = new RenameBitlingsUtility.Report();

        // Main monsters (Assets/Art/Monsters/<Type>/<Rarity>/)
        if (mainMonsters != null && mainMonsters.Count > 0)
        {
            var mainRep = RenameBitlingsUtility.RunPreImportPipeline(
                targetRootArtMonsters: mainRoot,
                monsters: mainMonsters,
                fixFolderNamesAndMigrateToFlat: fixFoldersAndMigrateLegacy,
                renameFilesToConvention: renameLegacyFilesToConvention,
                validateRequiredSprites: validateRequiredSprites,
                dryRun: dryRun,
                logVerbose: verboseLogs
            );
            rep.Absorb(mainRep);
        }

        // Pack monsters (Assets/Monsters/Packs/<Pack Name>/<Monster Name>/)
        if (includePackMonsters && !string.IsNullOrWhiteSpace(packsRoot) && packMonsters != null && packMonsters.Count > 0)
        {
            var packRep = RenameBitlingsUtility.RunPackPipeline(
                packsRoot: packsRoot,
                monsters: packMonsters,
                renameFilesToConvention: renameLegacyFilesToConvention,
                validateRequiredSprites: validateRequiredSprites,
                dryRun: dryRun,
                logVerbose: verboseLogs
            );
            rep.Absorb(packRep);
        }

        _lastReport = rep.ToSummaryString();

        if (rep.HasIssues) Debug.LogWarning("[RenameBitlings] Completed with issues.\n" + _lastReport);
        else Debug.Log("[RenameBitlings] Completed.\n" + _lastReport);
    }
}
#endif
