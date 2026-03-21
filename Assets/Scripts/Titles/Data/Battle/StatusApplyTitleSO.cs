using UnityEngine;

// ─────────────────────────────────────────────────────────────
// StatusApplyTitleSO
// Event-driven title that inflicts a status on a target when a
// battle event fires.  Reuses OnEventTriggerKind and
// TriggerLimitKind from OnEventTriggerTitleSO.
// ─────────────────────────────────────────────────────────────

/// <summary>Who receives the inflicted status.</summary>
public enum TitleStatusTarget
{
    /// <summary>The combatant that owns this title (always the player unit).</summary>
    Self,
    /// <summary>The opposing combatant (the wild monster).</summary>
    Opponent,
}

[CreateAssetMenu(menuName = "Data/Titles/Battle/Status Apply", fileName = "StatusApplyTitle")]
[Tooltip("A title that inflicts a status effect on a target when a battle event fires.")]
public sealed class StatusApplyTitleSO : TitleSO
{
    [Header("Trigger")]
    [Tooltip("Which battle event activates this title.")]
    public OnEventTriggerKind trigger = OnEventTriggerKind.OnAttackLanded;

    [Header("Status")]
    [Tooltip("The status to apply.")]
    public StatusType status = StatusType.Burn;

    [Tooltip("Who receives the status.")]
    public TitleStatusTarget target = TitleStatusTarget.Opponent;

    [Tooltip("Duration in turns (ignored if persistent is true).")]
    [Min(0)]
    public int turns = 3;

    [Tooltip("If true the status lasts for the entire battle.")]
    public bool persistent;

    [Tooltip("Status magnitude (e.g. shield %, DoT multiplier). Meaning depends on the status type.")]
    public float magnitude;

    [Header("Chance")]
    [Tooltip("Percent chance to fire when triggered (0–100). 100 = always.")]
    [Range(0f, 100f)]
    public float chancePercent = 100f;

    [Header("Limit")]
    [Tooltip("How often this title is allowed to fire: Unlimited, OncePerTurn, or OncePerBattle.")]
    public TriggerLimitKind triggerLimit = TriggerLimitKind.Unlimited;
}
