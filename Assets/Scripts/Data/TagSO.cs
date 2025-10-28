using UnityEngine;

public enum TagTrigger
{
    // ─────────────────────────────────────────────────────────────────────────────
    // BASICS
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("None")]                                  None                     = 0,
    [InspectorName("Always On (passive)")]                   AlwaysOn                 = 1,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: LIFECYCLE / GLOBAL
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Battle Start")]                          OnBattleStart            = 8,
    [InspectorName("Battle End")]                            OnBattleEnd              = 6,
    [InspectorName("Battle Condition (generic)")]            OnBattleCondition        = 5,
    [InspectorName("Battle Length Gate")]                    OnBattleLength           = 7,
    [InspectorName("Survive Rounds Gate")]                   OnSurviveRounds          = 64,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: TURN/ROUND CADENCE & INITIATIVE
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Acts First This Round")]                 OnActFirst               = 2,
    [InspectorName("Speed Check (initiative)")]              OnSpeedCheck             = 59,
    [InspectorName("Each Round (tick)")]                     OnEachRound              = 22,
    [InspectorName("End of Turn")]                           OnEndTurn                = 23,
    [InspectorName("End Turn: Regen")]                       OnEndTurnRegen           = 24,
    [InspectorName("Every Other Turn")]                      OnEveryOtherTurn         = 32,
    [InspectorName("Every Odd Turn")]                        OnEveryOddTurn           = 31,
    [InspectorName("Every 3 Turns")]                         OnEvery3Turns            = 29,
    [InspectorName("First 2 Turns")]                         OnFirst2Turns            = 34,
    [InspectorName("First 3 Turns")]                         OnFirst3Turns            = 35,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: ACTIONS & KOs
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("On Attack (basic strike)")]              OnAttack                 = 4,
    [InspectorName("First Attack (this battle)")]            OnFirstAttack            = 36,
    [InspectorName("Multi-Hit (this turn)")]                 OnMultiHit               = 51,
    [InspectorName("Consecutive Hits")]                      OnConsecutiveHits        = 13,
    [InspectorName("Every 3rd Attack (this turn)")]          OnEvery3rdAttack         = 30,
    [InspectorName("Kill (you)")]                            OnKill                   = 49,
    [InspectorName("Enemy KO (you dealt)")]                  OnEnemyKO                = 28,
    [InspectorName("Ally KO (this round)")]                  OnAllyKO                 = 3,
    [InspectorName("On Death (KO)")]                         OnDeath                  = 18,
    [InspectorName("First KO Dealt")]                        OnFirstKODealt           = 39,
    [InspectorName("First KO Taken")]                        OnFirstKOTaken           = 40,
    [InspectorName("No Damage Dealt for 2T")]                OnNoDamageDealt2T        = 53,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: DAMAGE / HITS (IN/OUT)
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Outgoing Damage")]                       OnOutgoingDamage         = 54,
    [InspectorName("On Any Damage Event")]                   OnDamage                 = 17,
    [InspectorName("Incoming Damage")]                       OnIncomingDamage         = 46,
    [InspectorName("Incoming Damage (flat)")]                OnIncomingDamageFlat     = 70,
    [InspectorName("First Hit Dealt (this battle)")]         OnFirstHit               = 37,
    [InspectorName("First Incoming (this battle)")]          OnFirstIncoming          = 38,
    [InspectorName("On Hit By Enemy")]                       OnHitByEnemy             = 44,
    [InspectorName("On Block")]                              OnBlock                  = 9,
    [InspectorName("On Block or Resist")]                    OnBlockOrResist          = 10,
    [InspectorName("Defense Mod")]                           OnDefense                = 19,
    [InspectorName("Ignore Defense")]                        OnDefenseIgnore          = 20,
    [InspectorName("Lifesteal Trigger")]                     OnLifesteal              = 50,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: CRITS
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Crit: Chance Mod")]                      OnCritChance             = 14,
    [InspectorName("Crit: Dealt")]                           OnCritDealt              = 15,
    [InspectorName("Crit: Custom Logic")]                    OnCritLogic              = 16,
    [InspectorName("Incoming Critical Hit")]                 OnIncomingCrit           = 45,
    [InspectorName("No Crits For 2 Turns")]                  OnNoCritsFor2Turns       = 52,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: STATUS (APPLY/TAKEN/CHANCE MODS)
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Status (has/applies)")]                  OnStatus                 = 60,
    [InspectorName("Status: Inflict")]                       OnStatusInflict          = 61,
    [InspectorName("Status: Taken")]                         OnStatusTaken            = 62,
    [InspectorName("Burn: Chance Mod")]                      OnBurnChance             = 11,
    [InspectorName("Freeze: Chance Mod")]                    OnFreezeChance           = 41,
    [InspectorName("Shock: Chance Mod")]                     OnShockChance            = 55,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: GATES (HP / ENEMY TYPE / BOSS)
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Self HP Gate (generic)")]                OnHP                     = 42,
    [InspectorName("HP Threshold Gate (generic)")]           OnHPThreshold            = 43,
    [InspectorName("Enemy HP ≤ 50%")]                        OnEnemyBelow50           = 26,
    [InspectorName("Enemy HP ≤ 20%")]                        OnEnemyBelow20           = 25,
    [InspectorName("Enemy is Boss")]                         OnEnemyBoss              = 27,

    // ─────────────────────────────────────────────────────────────────────────────
    // BATTLE: POSITION / RESCUE / SWAP
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Swap-In")]                               OnSwapIn                 = 65,
    [InspectorName("Rescue Heal < 40% HP")]                  OnRescueHealBelow40      = 66,

    // ─────────────────────────────────────────────────────────────────────────────
    // JOBS / IDLE
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Fatigue Decay (jobs)")]                  OnFatigueDecay           = 33,
    [InspectorName("Job: Output (generic)")]                 OnJobOutput              = 48,
    [InspectorName("Job: Coins Output")]                     OnJobCoins               = 47,
    [InspectorName("Storage Capacity (jobs)")]               OnStorageCap             = 63,
    [InspectorName("Job: Energy Output")]                    OnJobEnergy              = 67,
    [InspectorName("Job: Medkits Output")]                   OnJobMedkits             = 68,
    [InspectorName("Job: Materials Output")]                 OnJobMaterials           = 69,

    // ─────────────────────────────────────────────────────────────────────────────
    // SHOP / ECONOMY / META
    // ─────────────────────────────────────────────────────────────────────────────
    [InspectorName("Coins Gained (economy)")]                OnCoinsGained            = 12,
    [InspectorName("Drop Chance Mod")]                       OnDropChance             = 21,
    [InspectorName("Shop: Price Multiplier")]                OnShopPrice              = 56,
    [InspectorName("Shop: Refresh Cooldown")]                OnShopRefresh            = 57,
    [InspectorName("Shop: Reroll Cost")]                     OnShopRerollCost         = 58,
}



public enum TagGateType
{
    None,

    /// <summary>Self HP ≤ gateValueF (0..1)</summary>
    SelfHPBelow01,

    /// <summary>Enemy HP ≤ gateValueF (0..1)</summary>
    EnemyHPBelow01,

    /// <summary>Active only during the first gateValueI turns.</summary>
    FirstNTurns,

    /// <summary>Active on turns divisible by gateValueI (e.g., 3 → 3,6,9…).</summary>
    EveryNthTurn,

    /// <summary>Active only if you act first this round.</summary>
    ActFirstThisRound,

    /// <summary>Active only if the enemy is a boss.</summary>
    EnemyIsBoss,

    /// <summary>Active if attacksThisTurn ≥ gateValueI (multi-hit cadence).</summary>
    AttacksThisTurnAtLeast,

    /// <summary>Active if battleTurnsElapsed ≥ gateValueI.</summary>
    BattleTurnsAtLeast,

    /// <summary>Active if roundsSurvived ≥ gateValueI.</summary>
    RoundsSurvivedAtLeast,

    TimeIsNight,
}

[System.Serializable]
public struct TagEffect
{
    [Tooltip("When this effect attempts to apply (battle, jobs, shop, etc.).")]
    public TagTrigger trigger;

    [Header("Common knobs")]
    [Tooltip("Additive percentage to apply when this effect is active. Example: 0.10 = +10%, -0.25 = -25%.")]
    [Range(-1f, 5f)] public float addPct;

    [Tooltip("If true, this effect only applies when the monster is physically working at the site (for job triggers).")]
    public bool onlyWhenAtSite;

    [Tooltip("If non-None, this effect only applies while at the given job site or when evaluating that site's output.")]
    public JobType siteScope;

    [Range(0f, 1f)] public float weakenEnemyDamagePct;

    [Tooltip("How many turns this effect lasts once triggered (0 = instant / passive).")]
    [Min(0)] public int durationTurns;

    [Header("Type filters (battle)")]
    [Tooltip("If true, this effect only applies when the attacker type matches 'attackerType'.")]
    public bool gateByAttackerType;

    [Tooltip("Required attacker type when 'gateByAttackerType' is enabled.")]
    public MonsterType attackerType;

    [Tooltip("If true, this effect only applies when the defender type matches 'defenderType'.")]
    public bool gateByDefenderType;

    [Tooltip("Required defender type when 'gateByDefenderType' is enabled.")]
    public MonsterType defenderType;

    [Header("Generic gates (sheet: Gate Type / Gate Value)")]
    [Tooltip("Extra conditional gate to match spreadsheet definitions (HP gates, Nth turn, boss, etc).")]
    public TagGateType gateType;

    [Tooltip("Integer value for gate comparisons (e.g., FirstNTurns=N, EveryNthTurn=N, AttacksThisTurnAtLeast=N).")]
    public int gateValueI;

    [Tooltip("Float value for gate comparisons (e.g., HP thresholds as 0..1).")]
    public float gateValueF;

    [Tooltip("Boolean value for boolean gates (e.g., ActFirstThisRound, EnemyIsBoss).")]
    public bool gateBool;

    [Header("Crit handling")]
    public bool negateCritOnce;

    [Header("OnDeath payloads")]
    [Range(0f, 1f)] public float healAlliesPctMaxHp;

    [Header("Round survival rewards")]
    public int coinsOnSurvive;
    [Min(1)] public int everyNRounds;

    [Header("Job side-effects")]
    [Range(0f, 1f)] public float extraFatiguePct;
    
    public bool noCrits;  
    public float critTakenReducePct;
}

[CreateAssetMenu(menuName = "Data/Tags/Tag", fileName = "Tag_")]
public class TagSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable unique ID (string). Used to look up this tag in save data & runtime.")]
    public string id;

    [Tooltip("Display name shown in UI.")]
    public string displayName;

    [Tooltip("Short description of what this tag does (shown in tooltips/popups).")]
    [TextArea] public string desc;

    [Tooltip("Optional icon for UI.")]
    public Sprite icon;

    [Header("Effects")]
    [Tooltip("One or more effects this tag provides. Each effect can target a different trigger and have its own gates.")]
    public TagEffect[] effects;

    

#if UNITY_EDITOR
    [ContextMenu("Generate New ID")]
    void GenerateId()
    {
        id = System.Guid.NewGuid().ToString("N");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
