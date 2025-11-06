using UnityEngine;

public enum EventTriggerKind { OnHitTaken, OnAttack, OnCrit }

[CreateAssetMenu(menuName = "Data/Titles/Event Stacks", fileName = "EventStacksTitle")]
public sealed class EventStacksTitleSO : TitleSO
{
    public BattleStatKind stat = BattleStatKind.ATK;
    public EventTriggerKind trigger = EventTriggerKind.OnHitTaken;

    [Tooltip("+% per stack (e.g., 5 = +5%)")]
    public float percentPerStack = 5f;

    [Tooltip("Maximum number of stacks")]
    public int maxStacks = 3;

    [Tooltip("Decay per turn (stacks lost at OnTurnAdvanced)")]
    public int decayPerTurn = 0; // 0 = no decay
}
