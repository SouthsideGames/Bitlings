using System;
using System.Collections.Generic;
using UnityEngine;

public enum MonsterType { None = 0, Fire = 1, Water = 2, Grass = 3, Electric = 4, Ice = 5, Clash = 6, Corrupt = 7, Ground = 8, Sky = 9, Oracle = 10, Bug = 11, Rock = 12, Specter = 13, Wyrm = 14, Umbral = 15, Alloy = 16 }
public enum Rarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4, Mythic = 5, Boss = 6 }

[CreateAssetMenu(menuName = "Data/Monster/Monster", fileName = "Monster_")]
public class MonsterDataSO : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;
    public MonsterType type;
    public Sprite typeIcon;
    public Sprite icon;
    public Sprite backIcon;
    public Sprite premiumIcon;
    public Sprite premiumBackIcon;
    public Rarity rarity = Rarity.Common;
    public bool canBeStarter = false;
    [Min(0)] public int starterWeight = 1;
    
    public int maxLevel = 50;

    [Header("Stats")]
    public int baseHP = 60;
    public int baseAttack = 10;
    public int baseDefense = 8;
    public int baseSpeed = 10;

    [Header("Regeneration")]
    [Tooltip("Passive HP regenerated per real-time hour when not in battle. 0 = no passive regen.")]
    [Min(0f)] public float hpRegenPerHour = 6f;

    [Header("Fatigue")]
    [Tooltip("Percent (0..1) of site fatigue added per hour when this monster works. 0.03 = 3%/hr.")]
    [Range(0f, 0.20f)]
    public float fatigueRatePerHour = 0.03f;
    [Range(0f, 48f)] public float fatigueCooldownHours = 8f;


    [Header("Evolution")]
    public int evolutionStage = 1;
    public int evolutionLevel = 0;
    public MonsterDataSO evolutionForm;

    [Header("Boss / Rift Flags")]
    [Tooltip("If true, this Bitling can never be captured.")]
    public bool uncatchable = false;

    [Header("Boss (optional)")]
    public bool isBoss = false;
    public List<JobDebuff> bossJobDebuffs = new List<JobDebuff>();

    [Tooltip("Relative chance to be picked as boss when multiple bosses exist. 1 = default.")]
    [Min(1)] public int bossWeight = 1;

    [Header("Rift Weights")]
    [Tooltip("Relative chance to appear in wild rifts. 0 = never spawns. 1+ = higher weight = more common.")]
    [Min(0)] public float spawnWeight = 1;

    [Header("Collection & Jobs")]
    [Range(0.5f, 3f)] public float jobSkill = 1f;

    [Header("Titles")]
    public TitleTrackSO titleTrack;
    public TitleSO[] defaultAlwaysOnTitles;

    [Header("Iron Career")]
    [Tooltip("Optional curated titles used only in Iron Career title rolls.")]
    public TitleSO[] ironTitles;

    [Header("Personality")]
    public MonsterPersonalitySO Personality;

    [Header("Battle VFX & Moves")]
    [Tooltip("Name shown in battle log. E.g., 'Tackle', 'Leaf Slash', 'Spark Shot'.")]
    public string basicAttackName = "Attack";

    [Tooltip("Optional prefab spawned when this Bitling attacks (projectile, slash, etc.).")]
    public GameObject basicAttackPrefab;

    [Tooltip("Lifetime in seconds for spawned attack prefab. 0 = do not auto-destroy.")]
    [Min(0f)] public float basicAttackPrefabLifetime = 1f;

    [Header("Battle Audio")]
    [Tooltip("Optional one-shot SFX played when this monster is revealed at battle start.")]
    public AudioClip spawnSfx;


    [Header("Exchange")]
    [Tooltip("Base market value in Credits on the Bitling Exchange. Scaled by rarity, demand, and world events at runtime.")]
    [Min(0)] public int baseMarketValue = 10;

    [Header("Arena")]
    [Tooltip("Base arena score contributed by this species when placed on an arena battle team. Higher = stronger bracket seeding.")]
    [Min(0)] public int arenaScore = 10;

    [Header("Description")]
    [TextArea(3, 10)] public string description;

}


