using System;
using UnityEngine;

public enum BoosterType { Attack, Health, Speed, TypeResist }

/// <summary>
/// Controls in-battle booster usage, including durations and cooldowns.
/// Raises GameEvents.BattleStatsChanged whenever effective stats may have changed,
/// and (optionally) GameEvents.OnBoostersChanged for booster UI (cooldowns/durations).
/// </summary>
[DisallowMultipleComponent]
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
    [Range(0.1f, 1f)] public float resistMultiplier = 0.75f;
    [Min(1)] public int   healthHealAmount  = 15;

    // Runtime state
    private int attackDur, speedDur, resistDur;
    private int cdAtk, cdHp, cdSpd, cdRes;
    private bool usedABoosterThisTurn;
    private bool playersTurn;

    private BattleRuntimeHooks _hooks;
    private bool _hooksInitialized;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    /// <summary>
    /// Inject runtime hooks from the battle system (e.g., healing callback).
    /// Call this once when the battle starts.
    /// </summary>
    public void SetHooks(BattleRuntimeHooks hooks)
    {
        _hooks = hooks;
        _hooksInitialized = true;
    }

    public bool TryUseFromUI(BoosterType t, out string msg)
    {
        if (!_hooksInitialized)
        {
            msg = "Booster system not initialized.";
            return false;
        }

        return TryUse(t, _hooks, out msg);
    }

    public void OnTurnStart(bool isPlayer)
    {
        if (IronCareerRuntime.IsActive)
            return;

        playersTurn = isPlayer;
        usedABoosterThisTurn = false;

        // Turn start changes whether boosters are usable ("Not your turn" gate),
        // so the booster UI must refresh here.
        GameEvents.OnBoostersChanged?.Invoke();
    }

    public void OnTurnEnd()
    {
        int a0  = attackDur, s0  = speedDur, r0  = resistDur;
        int c0  = cdAtk,    ch0 = cdHp,     cs0 = cdSpd, cr0 = cdRes;

        if (attackDur > 0) attackDur--;
        if (speedDur  > 0) speedDur--;
        if (resistDur > 0) resistDur--;

        if (cdAtk > 0) cdAtk--;
        if (cdHp  > 0) cdHp--;
        if (cdSpd > 0) cdSpd--;
        if (cdRes > 0) cdRes--;

        bool anyChanged =
            attackDur != a0 || speedDur != s0 || resistDur != r0 ||
            cdAtk != c0 || cdHp != ch0 || cdSpd != cs0 || cdRes != cr0;

        if (anyChanged)
        {
            GameEvents.OnBoostersChanged?.Invoke();   // booster panel (cooldowns/durations)
            GameEvents.RaiseBattleStatsChanged();     // stat panel (numbers/colors)
        }

        if (r0 > 0 && resistDur == 0)
            BattleLogger.Log("Type Resist has worn off.", LogScope.Battle);
    }

    public bool IsBoosterActive(BoosterType t) =>
        t switch
        {
            BoosterType.Attack     => attackDur > 0,
            BoosterType.Speed      => speedDur  > 0,
            BoosterType.TypeResist => resistDur > 0,
            _ => false
        };

    public (int remaining, int max) Active(BoosterType t)
    {
        return t switch
        {
            BoosterType.Attack     => (attackDur, attackBoostTurns),
            BoosterType.Speed      => (speedDur,  speedBoostTurns),
            BoosterType.TypeResist => (resistDur, resistBoostTurns),
            BoosterType.Health     => (0, 0),
            _                      => (0, 0),
        };
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
            case BoosterType.Attack:
                if (cdAtk > 0) { reason = $"Attack Boost cooling down ({cdAtk})."; return false; }
                break;

            case BoosterType.Health:
                if (cdHp > 0) { reason = $"Health Boost cooling down ({cdHp})."; return false; }
                break;

            case BoosterType.Speed:
                if (cdSpd > 0) { reason = $"Speed Boost cooling down ({cdSpd})."; return false; }
                break;

            case BoosterType.TypeResist:
                if (cdRes > 0) { reason = $"Resist Boost cooling down ({cdRes})."; return false; }
                break;
        }

        reason = null;
        return true;
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
                if (hooks.HealPlayer != null)
                    healed = hooks.HealPlayer(healthHealAmount);

                cdHp = boosterCooldownTurns;
                msg = $"Health Boost: Healed {healed} HP.";
                break;
            }

            case BoosterType.Speed:
                speedDur = speedBoostTurns;
                cdSpd = boosterCooldownTurns;
                msg = $"Speed Boost: +{speedFlatBonus} SPD for {speedDur} turn(s).";
                break;

            case BoosterType.TypeResist:
                resistDur = resistBoostTurns;
                cdRes = boosterCooldownTurns;
                msg = $"Type Resist activated: super-effective hits are reduced to neutral for {resistDur} turn(s).";
                break;

            default:
                msg = "Unknown booster.";
                return false;
        }

        usedABoosterThisTurn = true;

        GameEvents.OnBoostersChanged?.Invoke();
        GameEvents.RaiseBattleStatsChanged();

        return true;
    }

    public (int atk, int hp, int spd, int res) Cooldowns() => (cdAtk, cdHp, cdSpd, cdRes);

    public void ResetBetweenBattles()
    {
        bool hadState =
            attackDur > 0 || speedDur > 0 || resistDur > 0 ||
            cdAtk > 0 || cdHp > 0 || cdSpd > 0 || cdRes > 0 ||
            usedABoosterThisTurn || playersTurn;

        attackDur = 0;
        speedDur = 0;
        resistDur = 0;

        cdAtk = 0;
        cdHp = 0;
        cdSpd = 0;
        cdRes = 0;

        usedABoosterThisTurn = false;
        playersTurn = false;

        if (hadState)
        {
            GameEvents.OnBoostersChanged?.Invoke();
            GameEvents.RaiseBattleStatsChanged();
        }
    }

    public int ConsumeSpeedBonusForInitiative()
    {
        if (speedDur <= 0)
            return 0;

        int bonus = speedFlatBonus;

        speedDur = 0;

        GameEvents.OnBoostersChanged?.Invoke();
        GameEvents.RaiseBattleStatsChanged();

        return bonus;
    }

}

public struct BattleRuntimeHooks
{
    public Func<int, int> HealPlayer;
}


