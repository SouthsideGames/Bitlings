using UnityEngine;

// ─────────────────────────────────────────────────────────────
// Enums for TeamAuraBattleTitleSO
// ─────────────────────────────────────────────────────────────

/// <summary>How the aura modifies the target's stats.</summary>
public enum AuraEffectKind
{
    /// <summary>Add a flat value to the stat.</summary>
    FlatBoost,
    /// <summary>Multiply the stat by (1 + value/100).</summary>
    PercentBoost,
    /// <summary>Reduce incoming damage by value% (post-DEF). Not a stat mod — consumed by damage pipeline.</summary>
    IncomingDamageReduction,
}

/// <summary>Who benefits from the aura.</summary>
public enum AuraTargetScope
{
    /// <summary>All allies including the aura owner.</summary>
    AllAllies,
    /// <summary>All allies except the aura owner.</summary>
    AlliesExceptSelf,
    /// <summary>Only the aura owner.</summary>
    SelfOnly,
}

/// <summary>Optional condition that gates the aura.</summary>
public enum AuraConditionKind
{
    /// <summary>Always active.</summary>
    None,
    /// <summary>Active only when the TARGET's HP is above the threshold.</summary>
    TargetHPAbove,
    /// <summary>Active only when the TARGET's HP is below the threshold.</summary>
    TargetHPBelow,
    /// <summary>Active only during the first N turns of battle.</summary>
    FirstNTurns,
}

// ─────────────────────────────────────────────────────────────
// TeamAuraBattleTitleSO
// Provides a passive aura buff to allies during battle.
// Plugs into the existing stat aggregation pipeline so stat
// colors (green/red) reflect the aura contribution naturally.
// ─────────────────────────────────────────────────────────────

[CreateAssetMenu(menuName = "Data/Titles/Battle/Team Aura", fileName = "TeamAuraBattleTitle")]
[Tooltip("A title that provides a passive stat aura to allies during battle.")]
public sealed class TeamAuraBattleTitleSO : TitleSO
{
    [Header("Effect")]
    [Tooltip("How the aura modifies the target.")]
    public AuraEffectKind effect = AuraEffectKind.FlatBoost;

    [Tooltip("Which stat to modify (ignored for IncomingDamageReduction).")]
    public StatKind stat = StatKind.Attack;

    [Tooltip("Value meaning depends on effect:\n" +
             "  FlatBoost → flat amount\n" +
             "  PercentBoost → percent increase (10 = +10%)\n" +
             "  IncomingDamageReduction → percent reduction (15 = −15% incoming)")]
    public float value = 10f;

    [Header("Target")]
    [Tooltip("Who benefits from the aura.")]
    public AuraTargetScope scope = AuraTargetScope.AllAllies;

    [Header("Condition (optional)")]
    [Tooltip("Optional condition. None = always active.")]
    public AuraConditionKind condition = AuraConditionKind.None;

    [Tooltip("HP threshold (0–1) for TargetHPAbove / TargetHPBelow conditions.")]
    [Range(0f, 1f)]
    public float conditionThreshold = 0.5f;

    [Tooltip("Turn count for FirstNTurns condition (e.g. 3 = active turns 0,1,2).")]
    [Min(1)]
    public int conditionTurns = 3;
}
