using UnityEngine;

// ─────────────────────────────────────────────────────────────
// Enums shared by OnEventTriggerTitleSO
// ─────────────────────────────────────────────────────────────

/// <summary>Which battle event triggers this title.</summary>
public enum OnEventTriggerKind
{
    OnAttackLanded,
    OnCritLanded,
    OnDamageTaken,
    OnKill,
    OnTurnStart,
    OnTurnEnd
}

/// <summary>What effect fires when the trigger activates.</summary>
public enum TitleEffectKind
{
    GainFlatShield,
    HealFlat,
    HealPercentMaxHp,
    GainTempStatBuff
}

/// <summary>How often the title is allowed to fire.</summary>
public enum TriggerLimitKind
{
    Unlimited,
    OncePerTurn,
    OncePerBattle
}

// ─────────────────────────────────────────────────────────────
// OnEventTriggerTitleSO
// Generic event-driven title: listens for a battle event, rolls
// chance, respects a trigger limit, and requests a battle effect.
// ─────────────────────────────────────────────────────────────

[CreateAssetMenu(menuName = "Data/Titles/Battle/On Event Trigger", fileName = "OnEventTriggerTitle")]
[Tooltip("A generic title that listens for a battle event and performs a configured effect (shield, heal, temp buff).")]
public sealed class OnEventTriggerTitleSO : TitleSO
{
    [Header("Trigger")]
    [Tooltip("Which battle event activates this title.")]
    public OnEventTriggerKind trigger = OnEventTriggerKind.OnAttackLanded;

    [Header("Effect")]
    [Tooltip("What happens when the trigger fires.")]
    public TitleEffectKind effect = TitleEffectKind.GainFlatShield;

    [Tooltip("Magnitude of the effect (flat shield HP, flat heal HP, %maxHP for HealPercentMaxHp, or flat stat bonus for GainTempStatBuff).")]
    public float effectValue = 10f;

    [Tooltip("Which stat to buff (only used when effect = GainTempStatBuff).")]
    public BattleStatKind buffStat = BattleStatKind.ATK;

    [Tooltip("Duration in seconds for GainTempStatBuff (only used when effect = GainTempStatBuff). ~10s ≈ 1 battle turn.")]
    public float buffDurationSeconds = 10f;

    [Header("Chance")]
    [Tooltip("Percent chance to fire when triggered (0–100). 100 = always.")]
    [Range(0f, 100f)]
    public float chancePercent = 100f;

    [Header("Limit")]
    [Tooltip("How often this title is allowed to fire: Unlimited, OncePerTurn, or OncePerBattle.")]
    public TriggerLimitKind triggerLimit = TriggerLimitKind.Unlimited;
}
