using System;
using UnityEngine;

/// <summary>
/// Lightweight internal event stream for battle narration/FX.
/// BattleManager emits events; a feedback layer consumes them.
/// Also tracks consumer count so legacy direct feedback calls can be gated.
/// </summary>
public sealed class BattleEventBus
{
    public event Action<BattleEvent> OnEvent;

    private int _consumerCount;

    public bool HasConsumers => _consumerCount > 0;

    public void RegisterConsumer() => _consumerCount++;

    public void UnregisterConsumer() => _consumerCount = Mathf.Max(0, _consumerCount - 1);

    public void Emit(BattleEvent e)
    {
        try { OnEvent?.Invoke(e); }
        catch (Exception ex) { Debug.LogException(ex); }
    }
}
