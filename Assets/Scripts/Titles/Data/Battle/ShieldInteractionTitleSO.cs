using UnityEngine;

// ─────────────────────────────────────────────────────────────
// Enums for ShieldInteractionTitleSO
// ─────────────────────────────────────────────────────────────

/// <summary>Which shield mechanic this title provides.</summary>
public enum ShieldInteractionKind
{
    /// <summary>Converts a percentage of excess healing into shield.</summary>
    ConvertOverhealToShield,

    /// <summary>Fires an effect when all shields on the combatant deplete.</summary>
    EffectOnShieldBreak
}

/// <summary>What happens when shields break (only used with EffectOnShieldBreak).</summary>
public enum ShieldBreakEffectKind
{
    GainFlatShield,
    HealFlat,
    GainTempStatBuff
}

// ─────────────────────────────────────────────────────────────
// ShieldInteractionTitleSO
// Provides shield-specific mechanics that go beyond the generic
// OnEventTriggerTitleSO:
//   • Overheal → shield conversion
//   • Effect on shield break (all shields depleted)
// ─────────────────────────────────────────────────────────────

[CreateAssetMenu(menuName = "Data/Titles/Battle/Shield Interaction", fileName = "ShieldInteractionTitle")]
[Tooltip("A title that provides advanced shield mechanics: overheal-to-shield conversion or shield-break effects.")]
public sealed class ShieldInteractionTitleSO : TitleSO
{
    [Header("Interaction Kind")]
    [Tooltip("Which shield mechanic this title provides.")]
    public ShieldInteractionKind kind = ShieldInteractionKind.ConvertOverhealToShield;

    [Header("Overheal → Shield  (ConvertOverhealToShield only)")]
    [Tooltip("Percentage of overheal converted to shield (0-100).")]
    [Range(0f, 100f)]
    public float conversionPercent = 50f;

    [Tooltip("Maximum shield that can be gained from a single overheal event. 0 = unlimited.")]
    public float maxShieldPerHeal = 0f;

    [Header("Shield Break Effect  (EffectOnShieldBreak only)")]
    [Tooltip("What effect fires when all shields deplete.")]
    public ShieldBreakEffectKind breakEffect = ShieldBreakEffectKind.GainFlatShield;

    [Tooltip("Magnitude of the break effect (flat shield, flat heal, or stat bonus).")]
    public float breakEffectValue = 10f;

    [Tooltip("Which stat to buff (only used when breakEffect = GainTempStatBuff).")]
    public BattleStatKind breakBuffStat = BattleStatKind.ATK;

    [Tooltip("Duration in seconds for GainTempStatBuff on shield break (~10s ≈ 1 turn).")]
    public float breakBuffDurationSeconds = 10f;

    [Header("Limit")]
    [Tooltip("How often this title may fire.")]
    public TriggerLimitKind triggerLimit = TriggerLimitKind.Unlimited;
}
