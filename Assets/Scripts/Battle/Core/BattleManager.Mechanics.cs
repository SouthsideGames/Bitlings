using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public partial class BattleManager : MonoBehaviour
{



    private int GetPlayerEffectiveSpeedForRun()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length || teamDefs[activeIndex] == null)
            return 1;

        int spd = GetProgressionTotalSPDForIndex(activeIndex);

        var j = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (j != null && j.speedBuffTurns > 0 && j.speedBonusPctFirstTurns != 0f)
            spd = Mathf.Max(1, Mathf.RoundToInt(spd * (1f + j.speedBonusPctFirstTurns)));

        var cmods = GetConditionalModsForActive();
        spd = Mathf.Max(1, Mathf.RoundToInt((spd + Mathf.Max(0, cmods.spdFlat)) * (1f + Mathf.Max(0f, cmods.spdPct))));

        // Status: Soaked reduces speed (run chance).
        spd = Mathf.Max(1, Mathf.RoundToInt(spd * Mathf.Max(0.1f, GetActivePlayerSoakedSpeedMultiplier())));

        return Mathf.Max(1, spd);
    }




    private int GetWildEffectiveSpeedForRun()
    {
        if (!wildDef) return 1;
        int spd = Mathf.Max(1, wildBaseSpeed);
        // Status: Soaked reduces speed (run chance).
        spd = Mathf.Max(1, Mathf.RoundToInt(spd * Mathf.Max(0.1f, GetWildSoakedSpeedMultiplier())));
        return spd;
    }




    private float ComputeRunChance()
    {
        int pSpd = GetPlayerEffectiveSpeedForRun();
        int wSpd = GetWildEffectiveSpeedForRun();
        float speedTerm = (pSpd + wSpd) > 0 ? (float)pSpd / (pSpd + wSpd) : 0.5f;

        float hp01 = 1f - (wildHP / Mathf.Max(1f, wildMaxHP));
        float attemptsBonus = runAttemptBonus * Mathf.Max(0, runAttempts);

        float chance =
            runBaseChance +
            runSpeedWeight * (speedTerm - 0.5f) +
            runHpWeight * hp01 +
            attemptsBonus;

        return Mathf.Clamp(chance, runMinChance, runMaxChance);
    }




    private void ApplyPendingGuardShieldForActive()
    {
        if (pendingGuardShield == null || shieldHP == null) return;
        if (activeIndex < 0 || activeIndex >= pendingGuardShield.Length) return;

        float gain = pendingGuardShield[activeIndex];
        if (gain <= 0.01f) return;

        shieldHP[activeIndex] += gain;
        pendingGuardShield[activeIndex] = 0f;

        BattleLogger.Log($"{GetName(activeIndex)} gains a guard shield of {Mathf.RoundToInt(gain)}!", LogScope.Battle);
        ClampAndPushActiveHP();
    }






    private bool RollDefendSuccess()
    {
        float chance = Mathf.Clamp01(currentDefendSuccess);
        bool ok = Rng01() <= chance;

        if (ok)
        {
            defendConsecutiveUses++;
            float next = defendFirstUseSuccess * Mathf.Pow(defendRepeatMultiplier, defendConsecutiveUses);
            currentDefendSuccess = Mathf.Max(defendMinSuccess, next);
        }
        else
        {
            defendConsecutiveUses = 0;
            currentDefendSuccess = defendFirstUseSuccess;
        }

        return ok;
    }




    private void ResetDefendStreak()
    {
        defendConsecutiveUses = 0;
        currentDefendSuccess = defendFirstUseSuccess;
    }

    private void GrantFailedDefendCritBonus(BattleSide failedDefender)
    {
        if (defendFailCritBonus <= 0f)
            return;

        if (failedDefender == BattleSide.Player)
        {
            _wildPendingFailDefendCritCharges = Mathf.Max(1, _wildPendingFailDefendCritCharges + 1);
            BattleLogger.Log("Wild gains bonus crit chance on its next attack (failed player defend).", LogScope.Battle);
            return;
        }

        _playerPendingFailDefendCritCharges = Mathf.Max(1, _playerPendingFailDefendCritCharges + 1);
        BattleLogger.Log("Player gains bonus crit chance on the next attack (failed wild defend).", LogScope.Battle);
    }

    private float GetFailedDefendCritBonusForAttacker(BattleSide attacker)
    {
        if (defendFailCritBonus <= 0f)
            return 0f;

        if (attacker == BattleSide.Player)
            return _playerPendingFailDefendCritCharges > 0 ? defendFailCritBonus : 0f;

        return _wildPendingFailDefendCritCharges > 0 ? defendFailCritBonus : 0f;
    }

    private void ConsumeFailedDefendCritBonusForAttacker(BattleSide attacker)
    {
        if (attacker == BattleSide.Player)
        {
            if (_playerPendingFailDefendCritCharges > 0)
                _playerPendingFailDefendCritCharges--;
            return;
        }

        if (_wildPendingFailDefendCritCharges > 0)
            _wildPendingFailDefendCritCharges--;
    }


    private void FirePlayerEndTurnTicks(bool dealtDamageThisTurn, bool critThisTurn)
    {
        playerNoDmgTurns = dealtDamageThisTurn ? 0 : Mathf.Min(playerNoDmgTurns + 1, 99);
        playerNoCritTurns = critThisTurn ? 0 : Mathf.Min(playerNoCritTurns + 1, 99);
    }




    private void ClearPlayerGuardStateForActive()
    {
        defendActiveThisRound = false;

        // Extra safety: ensure no lingering "stored guard shield" is applied later due to stale data.
        if (pendingGuardShield != null &&
            activeIndex >= 0 && activeIndex < pendingGuardShield.Length)
        {
            pendingGuardShield[activeIndex] = 0f;
        }
    }

}
