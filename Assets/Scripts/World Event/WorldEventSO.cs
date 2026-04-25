using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/World Events/World Event", fileName = "WE_")]
public class WorldEventSO : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;

    [Header("Category")]
    public WorldEventCategory category = WorldEventCategory.Flavor;

    [Tooltip("If true, this is considered a holiday-style ticker item. (May be scheduled or rotate.)")]
    public bool isHoliday = false;

    [Header("Description")]
    [Tooltip("Full description shown in event detail UI. Separate from the short ticker feed line.")]
    [TextArea(2, 5)] public string description;


    [Header("Ticker")]
    [TextArea(2, 4)] public string tickerMessage;

    [Header("Scheduling (optional)")]
    [Tooltip("If true, this event only activates within the scheduled window.")]
    public bool scheduledOnly = false;

    [Tooltip("Unix seconds; 0 = ignore.")]
    public long startUnix;

    [Tooltip("Unix seconds; 0 = ignore.")]
    public long endUnix;

    [Header("Rotation (if not scheduledOnly)")]
    public bool canRotate = true;
    [Min(0)] public int weight = 1;

    [Tooltip("If > 0, this event should not be re-rolled until at least this many days have passed.")]
    [Min(0f)] public float minDaysBetween = 0f;

    [Header("Reward Modifiers")]
    [Tooltip("Multiplies idle tick rewards while this event is active. 1 = no change.")]
    [Min(0f)] public float idleRewardMultiplier = 1f;

    [Tooltip("Multiplies post-battle reward payouts while this event is active. 1 = no change.")]
    [Min(0f)] public float battleRewardMultiplier = 1f;

    [Tooltip("Multiplies exchange sale value while this event is active. 1 = no change.")]
    [Min(0f)] public float exchangeValueMultiplier = 1f;

    [Header("Type Bonus (optional)")]
    [Tooltip("If set, this event spotlights a monster type. Leave None for no type bonus.")]
    public MonsterType boostedMonsterType = MonsterType.None;

    [Tooltip("Damage multiplier applied when the attacker's type matches boostedMonsterType. Ignored if boostedMonsterType is None.")]
    [Min(0f)] public float typeDamageMultiplier = 1f;

    [Header("Placeholder Effects")]
    public List<WorldEventEffect> effects = new();

    // ── Computed flags ─────────────────────────────────────────────────────────
    // Use these instead of manual booleans so they never go out of sync.

    public bool AffectsIdle     => !Mathf.Approximately(idleRewardMultiplier,    1f);
    public bool AffectsBattle   => !Mathf.Approximately(battleRewardMultiplier,  1f);
    public bool AffectsExchange => !Mathf.Approximately(exchangeValueMultiplier, 1f);
    public bool AffectsTypeBonus => boostedMonsterType != MonsterType.None;

    /// <summary>True if any reward modifier on this event deviates from the neutral default.</summary>
    public bool HasAnyModifier => AffectsIdle || AffectsBattle || AffectsExchange || AffectsTypeBonus
        || (effects != null && effects.Count > 0 && effects.Exists(fx => fx.kind != WorldEventEffectKind.None));

    public bool IsActiveNow(long nowUnix)
    {
        if (startUnix <= 0 && endUnix <= 0) return !scheduledOnly;

        if (startUnix > 0 && nowUnix < startUnix) return false;
        if (endUnix > 0 && nowUnix > endUnix) return false;
        return true;
    }
}

public enum WorldEventCategory
{
    Job = 0,
    Rift = 1,
    Meta = 2,
    Flavor = 3
}

[Serializable]
public struct WorldEventEffect
{
    public WorldEventEffectKind kind;

    [Tooltip("Optional job target (used by DisableJobSite / JobRateMultiplier).")]
    public JobType job;

    [Tooltip("Optional resource target (used by ResourceGainMultiplier).")]
    public ResourceType resource;

    [Tooltip("Optional monster type target (used by BoostedMonsterType / TypeDamageMultiplier).")]
    public MonsterType monsterType;

    [Tooltip("Generic numeric value (multipliers, etc.).")]
    public float value;

    [Tooltip("Optional boolean (used by JobCollectDisabled when you want explicit true/false).")]
    public bool flag;
}
