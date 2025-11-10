using UnityEngine;

public enum BoosterType { Attack, Health, Speed, TypeResist }

public sealed class BattleBoosterController : MonoBehaviour
{
    public static BattleBoosterController I { get; private set; }

    [Header("Durations (turns)")]
    [Min(1)] public int attackBoostTurns = 1;
    [Min(1)] public int speedBoostTurns  = 2;
    [Min(1)] public int resistBoostTurns = 2;

    [Header("Cooldown (turns)")]
    [Min(1)] public int boosterCooldownTurns = 3;

    [Header("Effect Numbers")]
    [Min(1)] public int   attackFlatBonus   = 10;
    [Min(1)] public int   speedFlatBonus    = 10;
    [Range(0.1f, 1f)] public float resistMultiplier = 0.75f; // incoming damage * this
    [Min(1)] public int   healthHealAmount  = 15;

    // runtime state
    int attackDur, speedDur, resistDur;
    int cdAtk, cdHp, cdSpd, cdRes;
    bool usedABoosterThisTurn;
    bool playersTurn;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    public void OnTurnStart(bool isPlayer)
    {
        playersTurn = isPlayer;
        usedABoosterThisTurn = false;
    }

    public void OnTurnEnd()
    {
        if (attackDur > 0) attackDur--;
        if (speedDur  > 0) speedDur--;
        if (resistDur > 0) resistDur--;

        if (cdAtk > 0) cdAtk--;
        if (cdHp  > 0) cdHp--;
        if (cdSpd > 0) cdSpd--;
        if (cdRes > 0) cdRes--;
    }

    public bool IsBoosterActive(BoosterType t) =>
        t switch {
            BoosterType.Attack     => attackDur > 0,
            BoosterType.Speed      => speedDur  > 0,
            BoosterType.TypeResist => resistDur > 0,
            _ => false
        };

    /// <summary>
    /// Returns (remainingTurns, maxTurns) for the given booster.
    /// Health is instant (no duration), so returns (0,0).
    /// </summary>
    public (int remaining, int max) Active(BoosterType t)
    {
        switch (t)
        {
            case BoosterType.Attack:     return (attackDur, attackBoostTurns);
            case BoosterType.Speed:      return (speedDur,  speedBoostTurns);
            case BoosterType.TypeResist: return (resistDur, resistBoostTurns);
            case BoosterType.Health:     return (0, 0); // instant heal, no active duration
            default:                     return (0, 0);
        }
    }

    public int   GetAttackBonus() => attackDur > 0 ? attackFlatBonus : 0;
    public int   GetSpeedBonus()  => speedDur  > 0 ? speedFlatBonus  : 0;
    public float GetResistMul()   => resistDur > 0 ? resistMultiplier : 1f;

    public bool CanUse(BoosterType t, out string reason)
    {
        if (!playersTurn) { reason = "Not your turn."; return false; }
        if (usedABoosterThisTurn) { reason = "You already used a booster this turn."; return false; }
        switch (t)
        {
            case BoosterType.Attack:     if (cdAtk > 0) { reason = $"Attack Boost cooling down ({cdAtk})."; return false; } break;
            case BoosterType.Health:     if (cdHp  > 0) { reason = $"Health Boost cooling down ({cdHp}).";  return false; } break;
            case BoosterType.Speed:      if (cdSpd > 0) { reason = $"Speed Boost cooling down ({cdSpd}).";  return false; } break;
            case BoosterType.TypeResist: if (cdRes > 0) { reason = $"Resist Boost cooling down ({cdRes})."; return false; } break;
        }
        reason = null; return true;
    }

    public bool TryUse(BoosterType t, BattleRuntimeHooks hooks, out string msg)
    {
        if (!CanUse(t, out msg)) return false;

        switch (t)
        {
            case BoosterType.Attack:
                attackDur = attackBoostTurns;
                cdAtk = boosterCooldownTurns;
                msg = $"Attack Boost: +{attackFlatBonus} ATK for {attackDur} turn(s).";
                break;

            case BoosterType.Health:
                {
                    int healed = 0;
                    if (hooks.HealPlayer != null) healed = hooks.HealPlayer(healthHealAmount);
                    cdHp = boosterCooldownTurns;
                    msg = $"Health Boost: Healed {healed} HP.";
                }
                break;

            case BoosterType.Speed:
                speedDur = speedBoostTurns;
                cdSpd = boosterCooldownTurns;
                msg = $"Speed Boost: +{speedFlatBonus} SPD for {speedDur} turn(s).";
                break;

            case BoosterType.TypeResist:
                resistDur = resistBoostTurns;
                cdRes = boosterCooldownTurns;
                msg = $"Type Resist: Incoming damage reduced for {resistDur} turn(s).";
                break;
        }

        usedABoosterThisTurn = true;
        return true;
    }

    public (int atk,int hp,int spd,int res) Cooldowns() => (cdAtk, cdHp, cdSpd, cdRes);
}

public struct BattleRuntimeHooks
{
    public System.Func<int,int> HealPlayer; // returns actual healed amount
}
