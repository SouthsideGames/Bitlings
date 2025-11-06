using UnityEngine;

public enum EventTriggerKind { OnHitTaken, OnAttack, OnCrit }

[CreateAssetMenu(menuName = "Data/Titles/Event Stacks", fileName = "EventStacksTitle")]
[Tooltip("Used to define Titles that build temporary stat stacks when specific battle events occur (hit, attack, crit).")]
public sealed class EventStacksTitleSO : TitleSO
{
    [Header("Stack Parameters")]
    [Tooltip("Which stat is increased per stack.")]
    public BattleStatKind stat = BattleStatKind.ATK;

    [Tooltip("Event that triggers stack gain (e.g., OnHitTaken, OnAttack, OnCrit).")]
    public EventTriggerKind trigger = EventTriggerKind.OnHitTaken;

    [Tooltip("Percent increase per stack (e.g., 5 = +5% per stack).")]
    public float percentPerStack = 5f;

    [Tooltip("Maximum number of stacks allowed.")]
    public int maxStacks = 3;

    [Tooltip("How many stacks decay each turn (0 = no decay).")]
    public int decayPerTurn = 0;
}
