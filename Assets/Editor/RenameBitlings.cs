#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class RenameBitlings : EditorWindow
{
    [Header("Roots")]
    [SerializeField] private DefaultAsset artMonstersRoot; // Assets/Art/Monsters

    [Header("CSV (Optional)")]
    [Tooltip("If provided, tool validates only monsters found in the CSV. If blank, scans all type/rarity folders under Art/Monsters.")]
    [SerializeField] private UnityEngine.Object csvAsset;

    [Header("Actions")]
    [SerializeField] private bool fixFoldersAndMigrateLegacy = true;
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
        win.minSize = new Vector2(720, 640);
    }

    private void OnEnable()
    {
        if (artMonstersRoot == null)
            artMonstersRoot = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Art/Monsters");
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("RenameBitlings (Option A)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        artMonstersRoot = (DefaultAsset)EditorGUILayout.ObjectField("Art Root (Assets/Art/Monsters)", artMonstersRoot, typeof(DefaultAsset), false);
        csvAsset = EditorGUILayout.ObjectField("CSV (optional)", csvAsset, typeof(UnityEngine.Object), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pipeline", EditorStyles.boldLabel);
        fixFoldersAndMigrateLegacy = EditorGUILayout.ToggleLeft("Fix Folder Names (Option A: migrate legacy per-monster folders into flat folder)", fixFoldersAndMigrateLegacy);
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
            EditorGUILayout.TextArea(string.IsNullOrWhiteSpace(_lastReport) ? "(none yet)" : _lastReport, GUILayout.MinHeight(240));
        }

        EditorGUILayout.EndScrollView();
    }

    private bool CanRun()
    {
        if (artMonstersRoot == null) return false;
        var p = AssetDatabase.GetAssetPath(artMonstersRoot);
        return AssetDatabase.IsValidFolder(p);
    }

    private void Run()
    {
        string root = AssetDatabase.GetAssetPath(artMonstersRoot);

        List<(MonsterType type, Rarity rarity, string displayName)> monsters;

        if (csvAsset != null)
        {
            if (!RenameBitlingsUtility.TryReadCsvMonsters(csvAsset, out monsters, out string err))
            {
                Debug.LogError($"[RenameBitlings] CSV read failed: {err}");
                return;
            }
        }
        else
        {
            monsters = RenameBitlingsUtility.ScanMonstersFromArtRoot(root);
        }

        var rep = RenameBitlingsUtility.RunPreImportPipeline(
            targetRootArtMonsters: root,
            monsters: monsters,
            fixFolderNamesAndMigrateToFlat: fixFoldersAndMigrateLegacy,
            renameFilesToConvention: renameLegacyFilesToConvention,
            validateRequiredSprites: validateRequiredSprites,
            dryRun: dryRun,
            logVerbose: verboseLogs
        );

        _lastReport = rep.ToSummaryString();

        if (rep.HasIssues) Debug.LogWarning("[RenameBitlings] Completed with issues.\n" + _lastReport);
        else Debug.Log("[RenameBitlings] Completed.\n" + _lastReport);
    }
}
#endif
