#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool to quickly build / update a MonsterDataSO from high-level inputs:
/// - Icon
/// - Name
/// - Type
/// - Rarity
/// - Evolution stage info
///
/// It computes base stats from rarity + type + stage using your balance rules,
/// and fills MonsterDataSO fields accordingly (no changes to MonsterDataSO needed).
/// </summary>
public class MonsterBuilderWindow : EditorWindow
{
    // ─────────────────────────────────────────────────────────────
    // Static Balance Rules (rarity budgets & type personalities)
    // ─────────────────────────────────────────────────────────────

    [Serializable]
    private struct TypeRatios
    {
        public float hp;
        public float atk;
        public float def;
        public float spd;

        public TypeRatios(float hp, float atk, float def, float spd)
        {
            this.hp = hp;
            this.atk = atk;
            this.def = def;
            this.spd = spd;
        }
    }

    // Level 1 total stat budgets
    private static readonly Dictionary<Rarity, int> RARITY_BUDGETS = new()
    {
        { Rarity.Common,    200 },
        { Rarity.Uncommon,  220 },
        { Rarity.Rare,      240 },
        { Rarity.Epic,      260 },
        { Rarity.Legendary, 280 },
        { Rarity.Mythic,    300 },
    };

    // Default spawnWeight suggestion bands (midpoint of each band)
    private static readonly Dictionary<Rarity, int> SPAWN_WEIGHT_DEFAULTS = new()
    {
        { Rarity.Common,    90 }, // 80–100
        { Rarity.Uncommon,  65 }, // 55–75
        { Rarity.Rare,      40 }, // 30–50
        { Rarity.Epic,      20 }, // 15–25
        { Rarity.Legendary,  8 }, // 5–10
        { Rarity.Mythic,     3 }, // 1–4
    };

    // Type personality matrix: HP%, Atk%, Def%, Spd%
    private static readonly Dictionary<MonsterType, TypeRatios> TYPE_RATIOS = new()
    {
        { MonsterType.Fire,     new TypeRatios(0.25f, 0.35f, 0.15f, 0.25f) },
        { MonsterType.Water,    new TypeRatios(0.35f, 0.20f, 0.30f, 0.15f) },
        { MonsterType.Grass,    new TypeRatios(0.30f, 0.20f, 0.35f, 0.15f) },
        { MonsterType.Electric, new TypeRatios(0.20f, 0.30f, 0.15f, 0.35f) },
        { MonsterType.Ice,      new TypeRatios(0.20f, 0.35f, 0.15f, 0.30f) },
        { MonsterType.Clash,    new TypeRatios(0.30f, 0.35f, 0.15f, 0.20f) },
        { MonsterType.Corrupt,  new TypeRatios(0.20f, 0.40f, 0.15f, 0.25f) },
        { MonsterType.Ground,   new TypeRatios(0.40f, 0.20f, 0.30f, 0.10f) },
        { MonsterType.Sky,      new TypeRatios(0.20f, 0.25f, 0.15f, 0.40f) },
        { MonsterType.Oracle,   new TypeRatios(0.20f, 0.30f, 0.20f, 0.30f) },
        { MonsterType.Bug,      new TypeRatios(0.25f, 0.25f, 0.20f, 0.30f) },
        { MonsterType.Rock,     new TypeRatios(0.40f, 0.20f, 0.30f, 0.10f) },
        { MonsterType.Specter,  new TypeRatios(0.20f, 0.25f, 0.15f, 0.40f) },
        { MonsterType.Wyrm,     new TypeRatios(0.35f, 0.35f, 0.20f, 0.10f) },
        { MonsterType.Umbral,   new TypeRatios(0.25f, 0.35f, 0.15f, 0.25f) },
        { MonsterType.Alloy,    new TypeRatios(0.35f, 0.20f, 0.35f, 0.10f) },
    };

    private static TypeRatios GetRatios(MonsterType type)
    {
        if (TYPE_RATIOS.TryGetValue(type, out var r))
            return r;

        // Fallback: balanced 25% each if type not in table
        return new TypeRatios(0.25f, 0.25f, 0.25f, 0.25f);
    }

    /// <summary>
    /// Stage budget delta relative to rarity base.
    /// 1-stage: 0
    /// 2-stage: stage1 -5, stage2 +5
    /// 3-stage: stage1 -10, stage2 0, stage3 +10
    /// </summary>
    private static int GetStageBudgetDelta(int totalStages, int stageIndex)
    {
        totalStages = Mathf.Clamp(totalStages, 1, 3);
        stageIndex = Mathf.Clamp(stageIndex, 1, totalStages);

        return totalStages switch
        {
            1 => 0,
            2 => (stageIndex == 1) ? -5 : +5,
            3 => stageIndex switch
            {
                1 => -10,
                2 => 0,
                3 => +10,
                _ => 0
            },
            _ => 0
        };
    }

    // ─────────────────────────────────────────────────────────────
    // Window Fields
    // ─────────────────────────────────────────────────────────────

    private MonsterDataSO target;
    private Sprite icon;
    private string displayName;
    private MonsterType chosenType = MonsterType.Fire;
    private Rarity chosenRarity = Rarity.Common;

    // Evolution structure
    private int totalStages = 1;
    private int stageIndex = 1;
    private MonsterDataSO evolutionTo;
    private int evolutionLevel = 0;
    private int maxLevel = 99;

    // Optional encounter setup
    private bool setSpawnWeight = true;
    private int customSpawnWeight = 0;

    // Optional starter flag
    private bool markAsStarter = false;
    private int starterWeight = 1;

    // Optional manual stat nudges (for role-ish flavor)
    private int hpOffset = 0;
    private int atkOffset = 0;
    private int defOffset = 0;
    private int spdOffset = 0;

    // Last computed stats (just for user feedback)
    private int previewHP, previewAtk, previewDef, previewSpd, previewTotal;

    // ─────────────────────────────────────────────────────────────
    // Unity Editor Window Boilerplate
    // ─────────────────────────────────────────────────────────────

    [MenuItem("Bitlings/Monster Builder")]
    public static void Open()
    {
        var win = GetWindow<MonsterBuilderWindow>("Monster Builder");
        win.minSize = new Vector2(380, 420);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Monster Builder", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Target asset
        target = (MonsterDataSO)EditorGUILayout.ObjectField("Target Monster", target, typeof(MonsterDataSO), false);

        if (target == null)
        {
            EditorGUILayout.HelpBox("Assign a MonsterDataSO asset to build/update.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();

        // Identity inputs
        icon = (Sprite)EditorGUILayout.ObjectField("Icon", icon, typeof(Sprite), false);

        displayName = EditorGUILayout.TextField("Display Name", string.IsNullOrWhiteSpace(displayName) ? target.displayName : displayName);

        chosenType = (MonsterType)EditorGUILayout.EnumPopup("Type", chosenType);
        chosenRarity = (Rarity)EditorGUILayout.EnumPopup("Rarity", chosenRarity);

        EditorGUILayout.Space();

        // Evolution inputs
        EditorGUILayout.LabelField("Evolution Setup", EditorStyles.boldLabel);
        totalStages = EditorGUILayout.IntSlider("Total Stages", totalStages, 1, 3);
        stageIndex = EditorGUILayout.IntSlider("Stage Index", stageIndex, 1, totalStages);
        evolutionTo = (MonsterDataSO)EditorGUILayout.ObjectField("Evolves To", evolutionTo, typeof(MonsterDataSO), false);
        evolutionLevel = EditorGUILayout.IntField("Evolution Level", evolutionLevel);
        maxLevel = EditorGUILayout.IntField("Max Level", maxLevel);

        EditorGUILayout.Space();

        // Encounter & starter
        EditorGUILayout.LabelField("Encounter / Starter", EditorStyles.boldLabel);
        setSpawnWeight = EditorGUILayout.Toggle("Auto Spawn Weight", setSpawnWeight);
        if (!setSpawnWeight)
        {
            customSpawnWeight = EditorGUILayout.IntField("Custom Spawn Weight", customSpawnWeight);
        }

        markAsStarter = EditorGUILayout.Toggle("Mark As Starter", markAsStarter);
        if (markAsStarter)
        {
            starterWeight = Mathf.Max(1, EditorGUILayout.IntField("Starter Weight", starterWeight));
        }

        EditorGUILayout.Space();

        // Optional stat offsets
        EditorGUILayout.LabelField("Optional Stat Offsets (Role Flavor)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Use small offsets (e.g. ±5–15) to make this monster more tanky, glass cannon, etc. Leave at 0 for pure type personality.", MessageType.None);
        hpOffset = EditorGUILayout.IntField("HP Offset", hpOffset);
        atkOffset = EditorGUILayout.IntField("Attack Offset", atkOffset);
        defOffset = EditorGUILayout.IntField("Defense Offset", defOffset);
        spdOffset = EditorGUILayout.IntField("Speed Offset", spdOffset);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        if (GUILayout.Button("Build / Update Monster", GUILayout.Height(32)))
        {
            BuildMonster();
        }

        if (previewTotal > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Build Preview:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"HP: {previewHP}   ATK: {previewAtk}   DEF: {previewDef}   SPD: {previewSpd}   (Total: {previewTotal})");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Build Logic
    // ─────────────────────────────────────────────────────────────

    private void BuildMonster()
    {
        if (target == null)
        {
            Debug.LogError("[MonsterBuilder] No target MonsterDataSO assigned.");
            return;
        }

        // Validate rarity budget
        if (!RARITY_BUDGETS.TryGetValue(chosenRarity, out int baseBudget))
        {
            Debug.LogError($"[MonsterBuilder] No rarity budget defined for {chosenRarity}.");
            return;
        }

        // Compute stage-adjusted budget
        int delta = GetStageBudgetDelta(totalStages, stageIndex);
        int stageBudget = baseBudget + delta;

        // Get type ratios
        var ratios = GetRatios(chosenType);

        float hpIdeal  = stageBudget * ratios.hp;
        float atkIdeal = stageBudget * ratios.atk;
        float defIdeal = stageBudget * ratios.def;
        float spdIdeal = stageBudget * ratios.spd;

        // Apply offsets and round
        int hp  = Mathf.Max(1, Mathf.RoundToInt(hpIdeal  + hpOffset));
        int atk = Mathf.Max(1, Mathf.RoundToInt(atkIdeal + atkOffset));
        int def = Mathf.Max(1, Mathf.RoundToInt(defIdeal + defOffset));
        int spd = Mathf.Max(1, Mathf.RoundToInt(spdIdeal + spdOffset));

        int total = hp + atk + def + spd;
        int minTotal = baseBudget - 10;
        int maxTotal = baseBudget + 10;

        if (total < minTotal || total > maxTotal)
        {
            Debug.LogWarning($"[MonsterBuilder] Total stats {total} are outside the recommended range [{minTotal}, {maxTotal}] for rarity {chosenRarity}. You can tweak offsets or accept this intentionally.");
        }

        Undo.RecordObject(target, "Build Monster");

        // Identity
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            target.displayName = displayName;
        }

        // Generate ID if missing
        if (string.IsNullOrWhiteSpace(target.id))
        {
            target.id = GenerateIdFromName(target.displayName, target.name);
        }

        target.type = chosenType;
        target.rarity = chosenRarity;

        if (icon != null)
        {
            target.icon = icon;
        }

        // Stats
        target.baseHP      = hp;
        target.baseAttack  = atk;
        target.baseDefense = def;
        target.baseSpeed   = spd;

        // Regeneration / fatigue / training: leave defaults defined in SO unless you want to override here.

        // Evolution
        target.evolutionStage = stageIndex;
        target.evolutionLevel = Mathf.Max(0, evolutionLevel);
        target.evolutionForm  = evolutionTo;

        if (maxLevel > 0)
        {
            target.maxLevel = maxLevel;
        }

        // Encounter weights
        if (setSpawnWeight)
        {
            if (SPAWN_WEIGHT_DEFAULTS.TryGetValue(chosenRarity, out int sw))
            {
                target.spawnWeight = Mathf.Max(0, sw);
            }
        }
        else if (customSpawnWeight > 0)
        {
            target.spawnWeight = Mathf.Max(0, customSpawnWeight);
        }

        // Starter settings
        target.canBeStarter = markAsStarter;
        if (markAsStarter)
        {
            target.starterWeight = Mathf.Max(1, starterWeight);
        }
        else
        {
            target.starterWeight = Mathf.Max(1, target.starterWeight);
        }

        // Basic jobSkill: leave at default 1.0 unless it's 0 for some reason
        if (target.jobSkill <= 0f)
        {
            target.jobSkill = 1f;
        }

        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();

        previewHP = hp;
        previewAtk = atk;
        previewDef = def;
        previewSpd = spd;
        previewTotal = total;

        Debug.Log($"[MonsterBuilder] Built {target.displayName} ({target.id}) – HP {hp}, ATK {atk}, DEF {def}, SPD {spd}, Total {total}.");
    }

    private static string GenerateIdFromName(string displayName, string fallbackAssetName)
    {
        string src = !string.IsNullOrWhiteSpace(displayName) ? displayName : fallbackAssetName;
        if (string.IsNullOrWhiteSpace(src))
            src = "Monster";

        // Uppercase, only letters/numbers, replace spaces with underscores
        string cleaned = Regex.Replace(src.Trim().ToUpperInvariant(), "[^A-Z0-9]+", "_");
        cleaned = cleaned.Trim('_');

        return "M-" + cleaned;
    }
}
#endif
