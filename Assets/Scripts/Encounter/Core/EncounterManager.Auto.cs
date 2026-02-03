using UnityEngine;
using System.Collections;

public partial class EncounterManager
{
    // ─────────────────────────────────────────────────────────────────────────────
    // AUTO BATTLE LOOP
    // ─────────────────────────────────────────────────────────────────────────────
    IEnumerator AutoLoop()
    {
        while (autoMode)
        {
            if (!inBattle)
            {
                // 1) Safety: stop if team cannot battle.
                if (!HasHealthyMonsters())
                {
                    StopAuto_NoHealthy();
                    yield break;
                }

                // 2) Pay energy for this auto-run if not yet paid.
                if (!autoRunPaidEnergy)
                {
                    if (!HasEnergy() || !SpendEnergy())
                    {
                        StopAuto_NoEnergy();
                        yield break;
                    }
                    autoRunPaidEnergy = true;
                }

                // 3) Start the actual encounter.
                if (!inBattle)
                {
                    // NOTE: this will pick the wild monster & start the battle.
                    // When the monster is picked, call NotifyAuto_SpecialSpawn(def)
                    // from that spawn logic (see notes below).
                    StartEncounter(false);
                }
            }

            // 4) Poll at a fixed cadence while auto is active.
            if (autoMode)
            {
                yield return new WaitForSeconds(autoPollSeconds);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // AUTO STOP HELPERS
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when auto mode runs out of energy (or fails to spend it).
    /// </summary>
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

        // Option 1: show the merged IdleBattle rewards panel instead of per-fight victory summaries.
        IdleBattleForegroundLogger.MarkPendingIfLogExists();
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();

        EmitStatus("AUTO stopped: no energy.", LogScope.System);
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Called when auto mode detects there are no healthy monsters left
    /// to continue battling.
    /// </summary>
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

        // Option 1: show the merged IdleBattle rewards panel instead of per-fight victory summaries.
        IdleBattleForegroundLogger.MarkPendingIfLogExists();
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();

        // Option 1: show the merged IdleBattle rewards panel instead of per-fight victory summaries.
        IdleBattleForegroundLogger.MarkPendingIfLogExists();
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();

        EmitStatus("AUTO stopped: no healthy team members.", LogScope.System);
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Call this from your encounter spawn logic when a wild monster is chosen.
    /// If it’s a special spawn (Epic/Mythic/Legendary or isUnique), auto pauses
    /// so the player can decide manually.
    /// </summary>
    public void NotifyAuto_SpecialSpawn(MonsterDataSO def)
    {
        if (!autoMode || def == null) return;

        bool isSpecial = false;

        try
        {
            // Example: treat Epic / Mythic / Legendary or explicit "isUnique" flag as special.
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

        // Option 1: show the merged IdleBattle rewards panel instead of per-fight victory summaries.
        IdleBattleForegroundLogger.MarkPendingIfLogExists();
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();

        // Option 1: show the merged IdleBattle rewards panel instead of per-fight victory summaries.
        IdleBattleForegroundLogger.MarkPendingIfLogExists();
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();

        // Option 1: show the merged IdleBattle rewards panel instead of per-fight victory summaries.
        IdleBattleForegroundLogger.MarkPendingIfLogExists();
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();

        EmitStatus("AUTO stopped: team knocked out.", LogScope.System);
        OnStateChanged?.Invoke();
    }
}
