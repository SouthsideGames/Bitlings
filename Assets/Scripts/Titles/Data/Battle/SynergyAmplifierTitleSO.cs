using UnityEngine;

// ─────────────────────────────────────────────────────────────
// Enums shared by SynergyAmplifierTitleSO
// ─────────────────────────────────────────────────────────────

/// <summary>How this title amplifies a synergy.</summary>
public enum SynergyAmpType
{
    /// <summary>Multiply synergy magnitude by (1 + value/100).</summary>
    PowerMultiplier,
    /// <summary>Add flat turns to synergy status duration (ignored for persistent).</summary>
    BonusTurns,
    /// <summary>Add flat magnitude to synergy status value.</summary>
    BonusMagnitude,
}

/// <summary>Which synergies this amplifier applies to.</summary>
public enum SynergyAmpFilter
{
    /// <summary>Applies to every resolved synergy.</summary>
    AllSynergies,
    /// <summary>Only synergies matching a specific MonsterType.</summary>
    SpecificType,
}

/// <summary>Optional tier restriction.</summary>
public enum SynergyAmpTierFilter
{
    /// <summary>Applies to both tiers.</summary>
    AnyTier,
    Tier1Only,
    Tier2Only,
}

// ─────────────────────────────────────────────────────────────
// SynergyAmplifierTitleSO
// Enhances resolved synergy commands before they are applied.
// ─────────────────────────────────────────────────────────────

[CreateAssetMenu(menuName = "Data/Titles/Battle/Synergy Amplifier", fileName = "SynergyAmplifierTitle")]
[Tooltip("A title that amplifies the existing synergy system (power, duration, flat magnitude).")]
public sealed class SynergyAmplifierTitleSO : TitleSO
{
    [Header("Amplification")]
    [Tooltip("How this title modifies the synergy.")]
    public SynergyAmpType ampType = SynergyAmpType.PowerMultiplier;

    [Tooltip("Value meaning depends on ampType:\n" +
             "  PowerMultiplier → percent increase (20 = +20%)\n" +
             "  BonusTurns → flat turns to add\n" +
             "  BonusMagnitude → flat magnitude to add")]
    public float value = 20f;

    [Header("Filter")]
    [Tooltip("Which synergies this amplifier affects.")]
    public SynergyAmpFilter filter = SynergyAmpFilter.AllSynergies;

    [Tooltip("Only used when filter = SpecificType.")]
    public MonsterType filterType = MonsterType.Fire;

    [Tooltip("Optional tier restriction.")]
    public SynergyAmpTierFilter tierFilter = SynergyAmpTierFilter.AnyTier;
}
