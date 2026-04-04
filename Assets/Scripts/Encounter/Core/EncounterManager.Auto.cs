using UnityEngine;
using System.Collections;

// ─────────────────────────────────────────────────────────────
// EncounterManager.Auto
// Foreground auto-loop execution and auto-stop/auto-pause conditions.
// ─────────────────────────────────────────────────────────────

public partial class EncounterManager
{

    IEnumerator AutoLoop()
    {
        while (autoMode)
        {
            if (!inBattle)
            {
                if (!HasHealthyMonsters())
                {
                    StopAuto_NoHealthy();
                    yield break;
                }

                if (!autoRunPaidEnergy)
                {
                    if (!HasEnergy() || !SpendEnergy())
                    {
                        StopAuto_NoEnergy();
                        yield break;
                    }
                    autoRunPaidEnergy = true;
                }

                if (!inBattle)
                {

                    StartEncounter(false);
                }
            }

            if (autoMode)
            {
                yield return new WaitForSeconds(autoPollSeconds);
            }
        }
    }


    void StopAuto_NoEnergy()
    {
        if (!autoMode) return;

        autoMode        = false;
        autoRunPaidEnergy = false;

        IdleBattleManager.I?.DisableAuto();

        if (autoLoopCo != null)
        {
            StopCoroutine(autoLoopCo);
            autoLoopCo = null;
        }

        PostBattleSummaryManager.I?.NotifyEnergyDepleted();
        PostBattleSummaryManager.I?.SetAutoBattling(false);

        // Foreground auto was stopped due to energy; do NOT show IdleBattleRewards.

        GameEvents.RaiseAutoBattleModeChanged(false);
        EmitStatus("AUTO stopped: no energy.", LogScope.System);
        OnStateChanged?.Invoke();
    }


    void StopAuto_NoHealthy()
    {
        if (!autoMode) return;

        autoMode          = false;
        autoRunPaidEnergy = false;

        IdleBattleManager.I?.DisableAuto();

        if (autoLoopCo != null)
        {
            StopCoroutine(autoLoopCo);
            autoLoopCo = null;
        }

        PostBattleSummaryManager.I?.SetAutoBattling(false);

        // Foreground auto was stopped due to no healthy monsters; do NOT show IdleBattleRewards.

        GameEvents.RaiseAutoBattleModeChanged(false);

        EmitStatus("AUTO stopped: no healthy team members.", LogScope.System);
        OnStateChanged?.Invoke();
    }


    public void NotifyAuto_SpecialSpawn(MonsterDataSO def)
    {
        if (!autoMode || def == null) return;

        bool isSpecial = false;

        try
        {
            if (def.rarity == Rarity.Epic ||
                def.rarity == Rarity.Mythic ||
                def.rarity == Rarity.Legendary)
            {
                isSpecial = true;
            }

            var uniqueField = def.GetType().GetField("isUnique");
            if (uniqueField != null && uniqueField.FieldType == typeof(bool))
            {
                if ((bool)uniqueField.GetValue(def))
                    isSpecial = true;
            }
        }
        catch
        {
            // If anything goes wrong, just fall back to not treating it as special.
        }

        if (!isSpecial) return;

        // Pause auto so player can manually decide.
        autoMode          = false;
        autoRunPaidEnergy = false;

        IdleBattleManager.I?.DisableAuto();

        if (autoLoopCo != null)
        {
            StopCoroutine(autoLoopCo);
            autoLoopCo = null;
        }

        PostBattleSummaryManager.I?.SetAutoBattling(false);

        EmitStatus("AUTO paused: special encounter.", LogScope.System);
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Call this when the team is wiped mid-auto (from BattleManager defeat).
    /// </summary>
    public void NotifyAuto_TeamKO()
    {
        if (!autoMode) return;

        autoMode          = false;
        autoRunPaidEnergy = false;

        IdleBattleManager.I?.DisableAuto();

        if (autoLoopCo != null)
        {
            StopCoroutine(autoLoopCo);
            autoLoopCo = null;
        }

        PostBattleSummaryManager.I?.SetAutoBattling(false);

        // Team was wiped while auto was running: open the merged IdleBattleRewards summary.
        // (This is the only foreground-auto case where we show the idle rewards panel.)
        IdleBattleForegroundLogger.MarkPendingIfLogExists();
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();

        EmitStatus("AUTO stopped: team knocked out.", LogScope.System);
        OnStateChanged?.Invoke();
    }
}
