using UnityEngine;
using System;

/// <summary>
/// Result payload returned when a battle ends (kept for backward compatibility).
/// </summary>
public struct BattleResult
{
    public bool victory;
    public int coinsGained;
    public MonsterDataSO wildDef;
    public int wildLevel;
    public float secondsSurvived;
    public int critCount;
    public int turnsSurvived;
    public int damageTaken;
}

/// <summary>
/// Compatibility adapter that preserves the old BattleManager API while forwarding
/// to the new TurnBattleManager (turn-based system). Speed controls are now no-ops.
/// Attach this on the SAME GameObject as TurnBattleManager and assign the reference.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleManager : MonoBehaviour
{
    [Header("Forward Target (required)")]
    [SerializeField] private TurnBattleManager turn;

    /// <summary>
    /// Legacy property kept so old code compiles. Turn-based has no speed scalar,
    /// so we just return 1f.
    /// </summary>
    public float BattleSpeed => 1f;

    private void Reset()
    {
        if (!turn) turn = GetComponent<TurnBattleManager>();
    }

    private void Awake()
    {
        if (!turn) turn = GetComponent<TurnBattleManager>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Backward-compatible API (forwards to TurnBattleManager)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Legacy alias retained by many callers.</summary>
    public void BeginBattle(MonsterDataSO wild, int level, System.Action<BattleResult> onEnded)
    {
        Begin(wild, level, onEnded);
    }

    /// <summary>Primary entry point: starts a turn-based battle via TurnBattleManager.</summary>
    public void Begin(MonsterDataSO wild, int level, System.Action<BattleResult> onEnded)
    {
        if (!turn)
        {
            Debug.LogError("[BattleManager Adapter] TurnBattleManager reference is missing on this GameObject.");
            onEnded?.Invoke(new BattleResult
            {
                victory = false,
                wildDef = wild,
                wildLevel = level,
                secondsSurvived = 0f,
                critCount = 0,
                turnsSurvived = 0,
                damageTaken = 0,
                coinsGained = 0
            });
            return;
        }

        turn.Begin(wild, level, onEnded);
    }

    /// <summary>Heals the active player monster during battle.</summary>
    public void TryAddHPToActive(float amount)
    {
        if (!turn)
        {
            Debug.LogWarning("[BattleManager Adapter] TryAddHPToActive called but TurnBattleManager is missing.");
            return;
        }

        turn.TryAddHPToActive(amount);
    }

    /// <summary>Toggle auto-battle mode for the player.</summary>
    public void SetAutoMode(bool on)
    {
        if (!turn)
        {
            Debug.LogWarning("[BattleManager Adapter] SetAutoMode called but TurnBattleManager is missing.");
            return;
        }

        turn.SetAutoMode(on);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Legacy speed API (now NO-OPS so other scripts keep compiling)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>No-op in turn-based mode. Kept for ABI compatibility.</summary>
    public void CycleBattleSpeed() { /* no-op */ }

    /// <summary>No-op in turn-based mode. Kept for ABI compatibility.</summary>
    public void SetBattleSpeed(float s) { /* no-op */ }

    // ─────────────────────────────────────────────────────────────────────────────
    // Optional legacy no-op (UI visual tap)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>No-op; visual feedback handled elsewhere.</summary>
    public void Punch(UnityEngine.UI.Graphic _) { /* no-op */ }
}
