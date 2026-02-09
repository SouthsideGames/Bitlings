using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public partial class BattleManager : MonoBehaviour
{

    private IEnumerator Co_RevealPanelsThenStart(CanvasGroup wildCG, CanvasGroup playerCG, float duration)
    {
        if (feedback != null)
            yield return feedback.Co_RevealPanels(wildCG, playerCG, duration);
        else
            yield return CoWaitUnscaled(Mathf.Max(0f, duration));

        if (wildCG) { wildCG.alpha = 1f; wildCG.blocksRaycasts = true; wildCG.interactable = true; }
        if (playerCG) { playerCG.alpha = 1f; playerCG.blocksRaycasts = true; playerCG.interactable = true; }

        yield return Co_StartBattleNow();
    }




    private IEnumerator Co_StartBattleNow()
    {
        _turnIndex = 0;
        EnsureBattleRngInitialized();
        inBattle = true;
        startTime = Time.unscaledTime;

        var vsName = wildDef ? $"{wildDef.displayName} (Lv {wildLevel})" : "Unknown";
        BattleLogger.BeginBattle(vsName, BattleSeed, BattleSeedLabel);
        // Reset key moment snapshot for this battle.
        BattleLogger.SetKeyMomentsCap(20);
        BattleLogger.ClearKeyMoments();

        if (wildDef)
            BattleLogger.Log($"A wild {wildDef.displayName} (Lv {wildLevel}) appeared!", LogScope.Battle);
        else
            BattleLogger.Log("A wild foe appeared!", LogScope.Battle);

        string personalityLabel = GetWildPersonalityLabel();
        if (!string.IsNullOrEmpty(personalityLabel) && wildDef && wildDef.Personality != null)
        {
            if (!string.IsNullOrEmpty(wildDef.Personality.description))
                BattleLogger.Log($"Personality: {personalityLabel} – {wildDef.Personality.description}", LogScope.Battle);
            else
                BattleLogger.Log($"Personality: {personalityLabel}.", LogScope.Battle);
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();

        if (activeIndex >= 0 && teamIds != null && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleStart(teamIds[activeIndex], wildDef, wildLevel);

        // Pull any Title battle-start shield into battle state so the damage pipeline and UI can consume/display it.
        if (titleShieldHP != null && activeIndex >= 0 && activeIndex < titleShieldHP.Length)
            titleShieldHP[activeIndex] = TitlesAdapter.GetBattleStartShieldRemaining(teamIds[activeIndex]);
        wildTitleShieldHP = 0f;

        Debug_LogActiveTitlesSnapshot("BattleStart");

        UpdateHPTextUI();

        ResetStatusIcons();
        RefreshStatusIconsFromState();

        if (turnCR != null) StopCoroutine(turnCR);
        turnCR = StartCoroutine(TurnLoop());
        yield break;
    }




    private IEnumerator TurnLoop()
    {
        int round = 0;
        yield return CoWaitScaled(0.4f);

        while (inBattle)
        {
            bool swappedFromKO = false;

            if (teamHP[activeIndex] <= 0.01f)
            {
                if (!AutoSwapToAlive())
                {
                    BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
                    EndBattle(false);
                    break;
                }
                swappedFromKO = true;
            }

            // Apply any stored guard shields (from last round)
            ApplyPendingGuardShieldForActive();
            ApplyPendingGuardShieldForWild();

            // New round: clear defend stances (they are "this round only")
            defendActiveThisRound = false;
            wildDefendActiveThisRound = false;

            // Sync status icons after round reset + shield application
            RefreshStatusIconsFromState();

            if (!AutoResolveActive)
                BattleLogger.Log(_logBuffer.GetRoundLine(round), LogScope.Battle);
            yield return CoWaitScaled(beginRoundDelay);

            _turnIndex++;
            TitlesAdapter.OnTurnAdvanced(_turnIndex);
            GameEvents.RaiseBattleStatsChanged();

            if (debugTitles && debugTitlesEveryTurn)
                Debug_LogActiveTitlesSnapshot("TurnAdvanced");

            if (swappedFromKO)
            {
                ClampAndPushActiveHP();
                ApplyActiveToUI();
                RefreshBenchUI();

                // Swap can change which slot has charge queued
                RefreshStatusIconsFromState();
            }

            if (IsWildKO() || IsTeamKO())
            {
                if (CheckEnd()) break;
                round++;
                continue;
            }

            int pSpeedBase = GetProgressionTotalSPDForIndex(activeIndex);

            var jSpeed = (jobCtx != null && activeIndex >= 0 && activeIndex < jobCtx.Length) ? jobCtx[activeIndex] : null;
            if (jSpeed != null && jSpeed.speedBuffTurns > 0 && jSpeed.speedBonusPctFirstTurns != 0f)
                pSpeedBase = Mathf.Max(1, Mathf.RoundToInt(pSpeedBase * (1f + jSpeed.speedBonusPctFirstTurns)));

            var titleCtx = BuildTitleContextForActive();
            float pSpeedAfterTitlesF = TitlesAdapter.GetStatValue(
                teamIds[activeIndex],
                teamDefs[activeIndex],
                teamLevels[activeIndex],
                "SPD",
                titleCtx,
                pSpeedBase
            );
            int pSpeedAfterTitles = Mathf.Max(1, Mathf.RoundToInt(pSpeedAfterTitlesF));

            var cmods = GetConditionalModsForActive();
            float pSpeedWithConditionalsF =
                (pSpeedAfterTitles + Mathf.Max(0, cmods.spdFlat)) *
                (1f + Mathf.Max(0f, cmods.spdPct));
            int pSpeedWithConditionals = Mathf.Max(1, Mathf.RoundToInt(pSpeedWithConditionalsF));

            int tempSPDFlat = (BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0);

            if (BattleBoosterController.I != null)
                tempSPDFlat += Mathf.Max(0, BattleBoosterController.I.ConsumeSpeedBonusForInitiative());
            int pSpeed = Mathf.Max(1, pSpeedWithConditionals + Mathf.Max(0, tempSPDFlat));

            int wSpeed = BattleCalc.CalcSpeed(wildDef, wildLevel);

            bool playerFirst;
            if (pSpeed > wSpeed) playerFirst = true;
            else if (pSpeed < wSpeed) playerFirst = false;
            else playerFirst = Rng01() < 0.5f;

            EnemyAction wildChoice = ChooseEnemyAction();

            yield return Co_TelegraphWildIntent(wildChoice);

            if (playerFirst)
            {
                if (wildChoice == EnemyAction.Defend)
                {
                    ApplyWildDefendStance();
                    RefreshStatusIconsFromState();
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    if (manualTurns) yield return WaitForPlayerChoiceAndResolve();
                    else yield return PlayerTurn();

                    RefreshStatusIconsFromState();

                    if (CheckEnd()) break;
                    yield return CoWaitScaled(hitPause);

                    if (!IsTeamKO() && teamHP[activeIndex] <= 0.01f)
                    {
                        AutoSwapToAlive();
                        RefreshStatusIconsFromState();
                    }
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    if (wildChoice != EnemyAction.Defend)
                    {
                        yield return EnemyTurn(wildChoice);

                        RefreshStatusIconsFromState();

                        if (CheckEnd()) break;
                        yield return CoWaitScaled(hitPause);
                    }
                }
            }
            else
            {
                if (!IsWildKO() && !IsTeamKO())
                {
                    PlayerAction queuedChoice = PlayerAction.Attack;

                    if (manualTurns)
                    {
                        SetIsPlayerTurn(true);
                        pendingAction = PlayerAction.None;

                        float choiceStart = Time.unscaledTime;

                        while (inBattle && pendingAction == PlayerAction.None)
                        {
                            if (enableAutoQueueAttack && autoQueueAttackAfterSeconds > 0f && !_narrationLock)
                            {
                                if (Time.unscaledTime - choiceStart >= autoQueueAttackAfterSeconds)
                                {
                                    pendingAction = PlayerAction.Attack;
                                    BattleLogger.Log($"[Battle] Failsafe: auto-queued Attack after {autoQueueAttackAfterSeconds:0}s idle.", LogScope.Battle);
                                    break;
                                }
                            }

                            yield return null;
                        }

                        queuedChoice = pendingAction;
                        pendingAction = PlayerAction.None;
                        GameEvents.OnBattleStateChanged?.Invoke();
                        SetIsPlayerTurn(false);

                        if (queuedChoice == PlayerAction.Defend)
                        {
                            string name = GetName(activeIndex);
                            bool success = RollDefendSuccess();

                            defendActiveThisRound = success;

                            if (!success)
                            {
                                // ✅ Critical fix: failure must not leave guard state on.
                                ClearPlayerGuardStateForActive();
                            }

                            if (feedback)
                                Emit(BattleEvent.DefendResult(BattleSide.Player, success));
        if (!HasBattleEventConsumers && feedback) feedback.PlayDefendResult(BattleFeedbackManager.BattleFeedbackSide.Player, success);
RefreshStatusIconsFromState();

                            if (success)
                            {
                                BattleLogger.Log($"{name} is defending.", LogScope.Battle);
                                BattleLogger.Log($"{name} will reduce the next hit and convert it into a shield for the following round.", LogScope.Battle);
                            }
                            else
                            {
                                BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
                            }
                        }
                        else
                        {
                            ResetDefendStreak();
                            ClearPlayerGuardStateForActive(); // ✅ consistent: no guard unless defend succeeded
                            RefreshStatusIconsFromState();
                        }

                    }

                    // ─────────────────────────────────────────────────────────
                    // Swap priority (Pokemon-style):
                    // If the player queued a Swap during the enemy-first branch,
                    // resolve the swap BEFORE the wild executes its action so the
                    // incoming hit targets the swapped-in monster.
                    // (Swap consumes the player's action for the round.)
                    // ─────────────────────────────────────────────────────────
                    if (manualTurns && queuedChoice == PlayerAction.Swap)
                    {
                        // If the current active was KO'ed somehow before swap resolution,
                        // the queued action is lost (handled below after enemy turn).
                        if (teamHP[activeIndex] > 0.01f)
                        {
                            ResetDefendStreak();
                            ResolveQueuedSwap();
                            RefreshStatusIconsFromState();
                        }

                        // Swap consumes the turn.
                        queuedChoice = PlayerAction.None;
                    }

                    // In the enemy-first branch, if the wild is defending this round,
                    // apply the defend stance BEFORE its action would execute.
                    // (We do NOT call EnemyTurn(Defend) to avoid double-rolling.)
                    if (wildChoice == EnemyAction.Defend)
                    {
                        ApplyWildDefendStance();
                        RefreshStatusIconsFromState();
                    }
                    else
                    {
                        yield return EnemyTurn(wildChoice);
                    }

                    // Enemy turn can set defend/charge/consume charge
                    RefreshStatusIconsFromState();

                    if (CheckEnd()) break;
                    yield return CoWaitScaled(hitPause);

                    // If the wild KO'ed our active slot, we must auto-swap (if possible) and the queued action is lost.
                if (!IsTeamKO() && teamHP[activeIndex] <= 0.01f)
                    {
                        // ✅ Guard was tied to the KO'd monster; never carry to the swapped-in one.
                        ClearPlayerGuardStateForActive();

                        AutoSwapToAlive();
                        queuedChoice = PlayerAction.None;
                        RefreshStatusIconsFromState();
                    }

                    if (!IsWildKO() && !IsTeamKO())
                    {
                        if (manualTurns)
                        {
                            switch (queuedChoice)
                            {
                                case PlayerAction.Attack:
                                    // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                    if (teamHP[activeIndex] > 0.01f)
                                        yield return PlayerTurn();
                                    RefreshStatusIconsFromState();
                                    break;

                                case PlayerAction.Focus:
                                    {
                                        // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                        if (teamHP[activeIndex] <= 0.01f)
                                        {
                                            RefreshStatusIconsFromState();
                                            break;
                                        }

                                        ResetDefendStreak();

                                        if (chargedNextAttack != null &&
                                            activeIndex >= 0 &&
                                            activeIndex < chargedNextAttack.Length)
                                        {
                                            chargedNextAttack[activeIndex] = true;
                                        }

                                        BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                                        BattleLogger.Log($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", LogScope.Battle);                                        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Focus"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Focus
                                        );
                                        RefreshStatusIconsFromState();
                                        break;
                                    }

                                case PlayerAction.Swap:
                                    {
                                        // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                        if (teamHP[activeIndex] <= 0.01f)
                                        {
                                            RefreshStatusIconsFromState();
                                            break;
                                        }

                                        ResetDefendStreak();
                                        ResolveQueuedSwap();
                                        RefreshStatusIconsFromState();
                                        break;
                                    }

                                case PlayerAction.Run:
                                    {
                                        // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                        if (teamHP[activeIndex] <= 0.01f)
                                        {
                                            RefreshStatusIconsFromState();
                                            break;
                                        }

                                        ResetDefendStreak();

                                        float chance = ComputeRunChance();
                                        bool escaped = Rng01() < chance;

                                        string name = GetName(activeIndex);                                        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Run"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Run
                                        );
                                        // Run does not affect guard/charge, but keep icons correct anyway
                                        RefreshStatusIconsFromState();

                                        if (escaped)
                                        {
                                            BattleLogger.Log($"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                                            EndBattle(false, true);
                                            yield break;
                                        }
                                        else
                                        {
                                            runAttempts++;
                                            BattleLogger.Log($"Couldn't escape! (Run chance was {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                                        }
                                        break;
                                    }

                                case PlayerAction.Defend:
                                default:
                                    // Defend is resolved before the wild acts in the enemy-first branch.
                                    break;
                            }
                        }
                        else
                        {
                            yield return PlayerTurn();
                            RefreshStatusIconsFromState();
                        }

                        if (CheckEnd()) break;
                        yield return CoWaitScaled(hitPause);
                    }
                }
            }

            if (!IsWildKO() && !IsTeamKO())
            {
                if (jobCtx != null && activeIndex >= 0 && activeIndex < jobCtx.Length && jobCtx[activeIndex] != null)
                {
                    if (jobCtx[activeIndex].speedBuffTurns > 0) jobCtx[activeIndex].speedBuffTurns--;
                    if (jobCtx[activeIndex].critBuffTurns > 0) jobCtx[activeIndex].critBuffTurns--;
                    if (jobCtx[activeIndex].critResistBuffTurns > 0) jobCtx[activeIndex].critResistBuffTurns--;
                    if (jobCtx[activeIndex].dmgReduceBuffTurns > 0) jobCtx[activeIndex].dmgReduceBuffTurns--;
                }

                yield return CoWaitScaled(endRoundDelay);
            }

            // Booster system: tick durations/cooldowns once per completed round.
            if (BattleBoosterController.I != null)
                BattleBoosterController.I.OnTurnEnd();

            defendActiveThisRound = false;
            wildDefendActiveThisRound = false;
            RefreshStatusIconsFromState();

            round++;
        }

        turnCR = null;
    }





    private IEnumerator WaitForPlayerChoiceAndResolve()
    {
        SetIsPlayerTurn(true);

        float choiceStart = Time.unscaledTime;

        while (inBattle && pendingAction == PlayerAction.None)
        {
            if (enableAutoQueueAttack && autoQueueAttackAfterSeconds > 0f && !_narrationLock)
            {
                if (Time.unscaledTime - choiceStart >= autoQueueAttackAfterSeconds)
                {
                    pendingAction = PlayerAction.Attack;
                    BattleLogger.Log($"[Battle] Failsafe: auto-queued Attack after {autoQueueAttackAfterSeconds:0}s idle.", LogScope.Battle);
                    break;
                }
            }

            yield return null;
        }

        var choice = pendingAction;
        pendingAction = PlayerAction.None;
        GameEvents.OnBattleStateChanged?.Invoke();
        SetIsPlayerTurn(false);

        switch (choice)
        {
            case PlayerAction.Attack:
                ResetDefendStreak();
                ClearPlayerGuardStateForActive(); // safety: attack implies no guard this round
                yield return PlayerTurn();
                break;

            case PlayerAction.Defend:
            {
                string name = GetName(activeIndex);
                bool success = RollDefendSuccess();

                defendActiveThisRound = success;

                if (!success)
                {
                    // ✅ Critical fix: failed defend must never leave guard state enabled.
                    ClearPlayerGuardStateForActive();
                }

                if (feedback)
                    Emit(BattleEvent.DefendResult(BattleSide.Player, success));
        if (!HasBattleEventConsumers && feedback) feedback.PlayDefendResult(BattleFeedbackManager.BattleFeedbackSide.Player, success);
// Keep icons correct immediately
                RefreshStatusIconsFromState();

                if (success)
                {
                    BattleLogger.Log($"{name} is defending.", LogScope.Battle);
                    BattleLogger.Log($"{name} will reduce the next hit and convert it into a shield for the following round.", LogScope.Battle);
                }
                else
                {
                    BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
                }

                break;
            }

            case PlayerAction.Focus:
            {
                ResetDefendStreak();
                ClearPlayerGuardStateForActive(); // safety

                if (chargedNextAttack != null && activeIndex >= 0 && activeIndex < chargedNextAttack.Length)
                    chargedNextAttack[activeIndex] = true;

                BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                BattleLogger.Log($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", LogScope.Battle);                                        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Focus"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Focus
                                        );
                RefreshStatusIconsFromState();
                break;
            }

            case PlayerAction.Swap:
            {
                ResetDefendStreak();
                ClearPlayerGuardStateForActive(); // ✅ guard must never carry to a swapped-in monster
                ResolveQueuedSwap();
                RefreshStatusIconsFromState();
                break;
            }

            case PlayerAction.Run:
            {
                ResetDefendStreak();
                ClearPlayerGuardStateForActive(); // safety

                float chance = ComputeRunChance();
                bool escaped = Rng01() < chance;

                string name = GetName(activeIndex);                                        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Run"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Run
                                        );
                RefreshStatusIconsFromState();

                if (escaped)
                {
                    BattleLogger.Log($"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                    EndBattle(false, true);
                    yield break;
                }
                else
                {
                    runAttempts++;
                    BattleLogger.Log($"Couldn't escape! (Run chance was {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                }
                break;
            }

            default:
                break;
        }
    }





    private IEnumerator PlayerTurn()
    {
        if (isResolvingPlayerTurn)
            yield break;

        isResolvingPlayerTurn = true;

        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
        {
            isResolvingPlayerTurn = false;
            yield break;
        }

        var playerDef = teamDefs[activeIndex];
        string attacker = GetName(activeIndex);
        string move = GetBasicMoveName(playerDef);
        string foeName = wildDef ? wildDef.displayName : "Foe";

        if (!ShouldSkipNarration(BattleLineTag.Flavor))
            yield return Say($"{attacker} used {move}!", BattleLineTag.Flavor);
        Emit(BattleEvent.ActionWindup(BattleSide.Player));
        if (!HasBattleEventConsumers && feedback) feedback.PlayAttackWindup(BattleFeedbackManager.BattleFeedbackSide.Player);
if (feedback)
            feedback.SpawnBasicAttackVfx(isPlayerSide: true, playerDef: playerDef, wildDef: wildDef);

        yield return CoWaitScaled(0.10f);

        // Baseline TOTAL ATK (SpeciesBase + LevelGrowth + Training + flatAtkBonus w/ legacy guard)
        GetProgressionTotalsForIndex(activeIndex, out _, out int atkBaseTotal, out _, out _, out _);

        // Conditionals apply on top of baseline totals
        var cond = GetConditionalModsForActive();
        int atkWithCondFlat = Mathf.Max(1, atkBaseTotal + Mathf.Max(0, cond.atkFlat));
        int atkForResolve = Mathf.Max(1, Mathf.RoundToInt(atkWithCondFlat * (1f + Mathf.Max(0f, cond.atkPct))));

        // Temp boosters are additive flat on top (do NOT recalc BattleCalc to create a multiplier)
        int tempFlatFromBoosters = (BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0) + (BattleBoosterController.I ? Mathf.Max(0, BattleBoosterController.I.GetAttackBonus()) : 0);
        if (tempFlatFromBoosters > 0)
            atkForResolve = Mathf.Max(1, atkForResolve + Mathf.Max(0, tempFlatFromBoosters));


        var jctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float playerCrit = critChancePlayer;
        if (jctx != null)
        {
            playerCrit += jctx.critChanceFlat;
            if (jctx.critBuffTurns > 0)
                playerCrit += jctx.critChanceBonusFirstTurns;
        }
        playerCrit = Mathf.Clamp01(playerCrit);

        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            null, wildDef, wildLevel,
            atkForResolve,
            playerCrit,
            critMultiplier,
            0
        );

        TitlesAdapter.OnAttackLanded(teamIds[activeIndex], dr.crit);
        if (dr.crit) _totalCritsThisBattle++;

        if (jctx != null && jctx.attackBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.attackBonusPct)));

        if (jctx != null && jctx.usedFirstOutgoing == false && jctx.firstOutgoingBonus > 0f)
        {
            jctx.usedFirstOutgoing = true;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.firstOutgoingBonus)));
        }

        if (jctx != null && jctx.surgeApplied && jctx.surgeAtkBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.surgeAtkBonusPct)));

        if (slotDamageBuffPct != null && slotDamageBuffTurns != null &&
            activeIndex >= 0 && activeIndex < slotDamageBuffPct.Length &&
            slotDamageBuffTurns[activeIndex] > 0 &&
            slotDamageBuffPct[activeIndex] > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + slotDamageBuffPct[activeIndex])));
            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"+{Mathf.RoundToInt(slotDamageBuffPct[activeIndex] * 100f)}% damage buff active.", BattleLineTag.Flavor);

            slotDamageBuffTurns[activeIndex]--;
            if (slotDamageBuffTurns[activeIndex] <= 0)
                slotDamageBuffPct[activeIndex] = 0f;
        }

        if (chargedNextAttack != null &&
            activeIndex >= 0 &&
            activeIndex < chargedNextAttack.Length &&
            chargedNextAttack[activeIndex] &&
            chargeBonusPct > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + chargeBonusPct)));
            chargedNextAttack[activeIndex] = false;

            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"{GetName(activeIndex)} unleashes a charged attack (+{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage)!", BattleLineTag.Flavor);
        }

        float preventedByWildGuard = 0f;
        int dmgToApply = dr.damage;

        if (wildDefendActiveThisRound && defendReducePct > 0f)
        {
            float guardPct = Mathf.Clamp01(defendReducePct);
            int before = dmgToApply;
            int after = Mathf.Max(1, Mathf.RoundToInt(dmgToApply * (1f - guardPct)));
            preventedByWildGuard = Mathf.Max(0, before - after);
            dmgToApply = after;

            if (preventedByWildGuard > 0f)
            {
                Emit(BattleEvent.StatusApplied(BattleSide.Wild, BattleSide.Wild, "DefendShieldFX"));
                if (!HasBattleEventConsumers && feedback) feedback.PlayDefendShieldFX(isPlayer: false);
            }
}

        float absorbedByWildShield = 0f;

        float absorbedByWildTitleShield = 0f;

        if (wildTitleShieldHP > 0f && dmgToApply > 0)
        {
            float absorb = Mathf.Min(wildTitleShieldHP, dmgToApply);
            absorbedByWildTitleShield = absorb;
            wildTitleShieldHP = Mathf.Max(0f, wildTitleShieldHP - absorb);
            dmgToApply = Mathf.Max(0, dmgToApply - Mathf.RoundToInt(absorb));

            if (absorb > 0f)
                if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                    yield return Say($"{foeName}'s title shield absorbed {Mathf.RoundToInt(absorb)}!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        if (wildShieldHP > 0f && dmgToApply > 0)
        {
            float absorb = Mathf.Min(wildShieldHP, dmgToApply);
            absorbedByWildShield = absorb;
            wildShieldHP = Mathf.Max(0f, wildShieldHP - absorb);
            dmgToApply = Mathf.Max(0, dmgToApply - Mathf.RoundToInt(absorb));

            if (absorb > 0f)
                if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                    yield return Say($"{foeName}'s shield absorbed {Mathf.RoundToInt(absorb)}!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        if (preventedByWildGuard > 0f && guardConvertPct > 0f)
        {
            float gain = preventedByWildGuard * guardConvertPct;
            wildPendingGuardShield += gain;
            if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                yield return Say($"{foeName} stores {Mathf.RoundToInt(gain)} damage as a guard shield for the next round.", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        float preWildHP = wildHP;
        wildHP = Mathf.Max(0f, wildHP - dmgToApply);
        _totalDamageDealtThisBattle += Mathf.Max(0, dmgToApply);
        PushHPBars();

        float wRatio = wildMaxHP > 0.01f ? (float)dmgToApply / wildMaxHP : 0f;
        Emit(BattleEvent.Damage(BattleSide.Player, BattleSide.Wild, dmgToApply, dr.crit, dr.effectiveness, wRatio, (preventedByWildGuard > 0f) || (absorbedByWildShield > 0f) || (absorbedByWildTitleShield > 0f)));
        if (!HasBattleEventConsumers && feedback) feedback.PlayHitReaction(BattleFeedbackManager.BattleFeedbackSide.Wild, dr.crit, wRatio, wasGuarded: (preventedByWildGuard > 0f) || (absorbedByWildShield > 0f) || (absorbedByWildTitleShield > 0f));
if (!playerLandedFirstHitThisBattle && dr.damage > 0)
            playerLandedFirstHitThisBattle = true;

        yield return Say($"{attacker} hits {foeName} for {dmgToApply}!", BattleLineTag.Result);

        if (dr.crit)
            BattleLogger.AddKeyMoment($"CRIT: {attacker} → {foeName} ({dmgToApply})");

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f)
            {
                if (!ShouldSkipNarration(BattleLineTag.SuperEffective | BattleLineTag.Flavor))
                    yield return Say("It's super effective!", BattleLineTag.SuperEffective | BattleLineTag.Flavor);
            }
            else if (dr.effectiveness < 0.85f)
            {
                if (!ShouldSkipNarration(BattleLineTag.NotEffective | BattleLineTag.Flavor))
                    yield return Say("It's not very effective...", BattleLineTag.NotEffective | BattleLineTag.Flavor);
            }
        }
        if (dr.crit)
        {
            if (!ShouldSkipNarration(BattleLineTag.Crit | BattleLineTag.Flavor))
                yield return Say("Critical hit!", BattleLineTag.Crit | BattleLineTag.Flavor);
        }

        // Centralized KO messaging (fires only on HP crossing >0 → 0)
        yield return MaybeSayKO_Wild(foeName, preWildHP, wildHP);

        if (jctx != null && jctx.endTurnHealPct > 0f)
        {
            bool canHeal = (jctx.regenTurns == int.MaxValue) || (jctx.regenTurns > 0);
            if (canHeal)
            {
                float healAmt = GetFinalMaxHPForIndex(activeIndex) * jctx.endTurnHealPct;
                TryAddHPToActive(healAmt);
                if (jctx.regenTurns != int.MaxValue) jctx.regenTurns--;
                if (!ShouldSkipNarration(BattleLineTag.Flavor))
                    yield return Say($"{GetName(activeIndex)} regenerates {Mathf.RoundToInt(healAmt)} HP.", BattleLineTag.Flavor);
            }
        }

        FirePlayerEndTurnTicks(dealtDamageThisTurn: dr.damage > 0, critThisTurn: dr.crit);

        isResolvingPlayerTurn = false;
    }




    private IEnumerator EnemyTurn(EnemyAction choice)
    {
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            yield break;

        if (choice != EnemyAction.Defend)
            yield return CoWaitScaled(0.15f);

        if (choice != EnemyAction.Defend)
            ResetEnemyDefendStreak();

        if (choice == EnemyAction.Defend)
        {
            string name = wildDef ? wildDef.displayName : "Foe";
            bool success = RollEnemyDefendSuccess();

            wildDefendActiveThisRound = success;

            if (feedback)
            {
                Emit(BattleEvent.DefendResult(BattleSide.Wild, success));
                if (!HasBattleEventConsumers && feedback) feedback.PlayDefendResult(BattleFeedbackManager.BattleFeedbackSide.Wild, success);
}

            if (success)
            {
                yield return Say($"{name} is defending.", BattleLineTag.Result);
                yield return Say($"{name} will reduce the next hit and convert it into a shield for the following round.", BattleLineTag.Result);
            }
            else
            {
                yield return Say($"{name} tried to defend, but it failed!", BattleLineTag.Result);
            }

            yield break;
        }

        if (choice == EnemyAction.Focus)
        {
            wildChargedNextAttack = true;

            string name = wildDef ? wildDef.displayName : "Foe";
            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"{name} is charging up.", BattleLineTag.Flavor);
            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", BattleLineTag.Flavor);                                        Emit(BattleEvent.ActionQueued(BattleSide.Wild, "Swap"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Wild,
                                            BattleFeedbackManager.BattleFeedbackAction.Swap
                                        );
            yield break;
        }

        if (choice == EnemyAction.Run)
        {
            string name = wildDef ? wildDef.displayName : "Foe";
            float chance = ComputeEnemyRunChance();
            bool fled = Rng01() < chance;

            if (fled)
            {
                yield return Say($"{name} fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", BattleLineTag.Result);
                EndBattle(false, escaped: true);
                yield break;
            }
            else
            {
                yield return Say($"{name} tried to flee, but couldn't! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", BattleLineTag.Result);
                yield break;
            }
        }

        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            yield break;

        string attackerName = wildDef ? wildDef.displayName : "Foe";
        string move = GetBasicMoveName(wildDef);

        if (!ShouldSkipNarration(BattleLineTag.Flavor))
            yield return Say($"{attackerName} used {move}!", BattleLineTag.Flavor);
        Emit(BattleEvent.ActionWindup(BattleSide.Wild));
        if (!HasBattleEventConsumers && feedback) feedback.PlayAttackWindup(BattleFeedbackManager.BattleFeedbackSide.Wild);
if (feedback)
            feedback.SpawnBasicAttackVfx(isPlayerSide: false, playerDef: teamDefs[activeIndex], wildDef: wildDef);

        yield return CoWaitScaled(0.10f);

        int enemyAtk = Mathf.Max(1, Mathf.RoundToInt(wildAttackPerTurn));
        int defFlatBooster = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;

        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float preHP = teamHP[activeIndex];


        var cmods = GetConditionalModsForActive();

        var df = TitlesAdapter.GetDamageFilter(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);

        float playerCritResist = 0f;
        if (ctx != null)
        {
            playerCritResist += ctx.critResistFlat;
            if (ctx.critResistBuffTurns > 0)
                playerCritResist += ctx.critResistBonusFirstTurns;
        }

        float wildCritChance = df.cannotBeCrit ? 0f : Mathf.Clamp01(critChanceWild - playerCritResist);

        // Baseline TOTAL DEF (SpeciesBase + LevelGrowth + Training)
        GetProgressionTotalsForIndex(activeIndex, out _, out _, out int defBaseTotal, out _, out _);

        // Flat defense sources stack onto DEF as a STAT (boosters + conditional flat)
        int defenderEffectiveDefenseStat =
            Mathf.Max(0, defBaseTotal + Mathf.Max(0, defFlatBooster) + Mathf.Max(0, cmods.defFlat));

        var dr = BattleCalc.ResolveHit(
            null, wildDef, wildLevel,
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            enemyAtk, wildCritChance, critMultiplier,
            defenderFlatDefenseBonus: 0,
            defenderEffectiveDefenseStat: defenderEffectiveDefenseStat
        );


        if (wildChargedNextAttack && chargeBonusPct > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + chargeBonusPct)));
            wildChargedNextAttack = false;

            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"{attackerName} unleashes a charged attack (+{Mathf.RoundToInt(chargeBonusPct * 100f)}% dmg)!", BattleLineTag.Flavor);
        }

        float incomingScalar = 1f;

        if (cmods.defPct > 0f)
            incomingScalar *= 1f - Mathf.Clamp01(cmods.defPct);

        if (ctx != null && !ctx.usedFirstIncoming && ctx.firstIncomingReduce > 0f)
        {
            ctx.usedFirstIncoming = true;
            incomingScalar *= 1f - ctx.firstIncomingReduce;
        }

        if (ctx != null && ctx.baseDamageReducePct > 0f)
            incomingScalar *= 1f - ctx.baseDamageReducePct;

        if (ctx != null && ctx.defenseBonusPct > 0f)
            incomingScalar *= 1f - ctx.defenseBonusPct;

        if (ctx != null && ctx.dmgReduceBuffTurns > 0 && ctx.dmgReduceFirstTurns > 0f)
            incomingScalar *= 1f - ctx.dmgReduceFirstTurns;

        float scalarBeforeGuard = incomingScalar;
        float preventedByGuardRaw = 0f;

        if (defendActiveThisRound && defendReducePct > 0f)
        {
            float guardPct = Mathf.Clamp01(defendReducePct);
            incomingScalar *= (1f - guardPct);

            float dmgBeforeGuard = dr.damage * scalarBeforeGuard;
            float dmgAfterGuard = dr.damage * incomingScalar;
            preventedByGuardRaw = Mathf.Max(0f, dmgBeforeGuard - dmgAfterGuard);

            if (preventedByGuardRaw > 0f)
        {
            Emit(BattleEvent.StatusApplied(BattleSide.Player, BattleSide.Player, "DefendShieldFX"));
            if (!HasBattleEventConsumers && feedback) feedback.PlayDefendShieldFX(isPlayer: true);
        }
}

        int dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        // Title battle-start shield (separate pool, consumed before normal shields)
        float titleShieldBefore = (titleShieldHP != null && activeIndex >= 0 && activeIndex < titleShieldHP.Length) ? titleShieldHP[activeIndex] : 0f;
        float titleShieldAbsorbF = 0f;

        int dmg_incoming = dmg_afterScalar;

        int dmg_final = dmg_incoming;
        if (titleShieldBefore > 0f && dmg_final > 0)
        {
            titleShieldAbsorbF = Mathf.Min(titleShieldBefore, dmg_final);
            if (titleShieldHP != null && activeIndex >= 0 && activeIndex < titleShieldHP.Length)
                titleShieldHP[activeIndex] = Mathf.Max(0f, titleShieldBefore - titleShieldAbsorbF);
            dmg_final = Mathf.Max(0, dmg_final - Mathf.RoundToInt(titleShieldAbsorbF));

            if (titleShieldAbsorbF > 0f)
                if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                    yield return Say($"{GetName(activeIndex)}'s title shield absorbed {Mathf.RoundToInt(titleShieldAbsorbF)}!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        float shieldBefore = (shieldHP != null && activeIndex >= 0 && activeIndex < shieldHP.Length) ? shieldHP[activeIndex] : 0f;
        float shieldAbsorbF = 0f;

        if (shieldBefore > 0f && dmg_final > 0)
        {
            shieldAbsorbF = Mathf.Min(shieldBefore, dmg_final);
            shieldHP[activeIndex] = Mathf.Max(0f, shieldBefore - shieldAbsorbF);
            dmg_final = Mathf.Max(0, dmg_final - Mathf.RoundToInt(shieldAbsorbF));

            if (shieldAbsorbF > 0f)
                if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                    yield return Say($"{GetName(activeIndex)}'s shield absorbed {Mathf.RoundToInt(shieldAbsorbF)}!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        string victimName = GetName(activeIndex);
        float prePlayerHP = teamHP[activeIndex];
        teamHP[activeIndex] = Mathf.Max(0f, teamHP[activeIndex] - dmg_final);
        ClampAndPushActiveHP();

        float maxHP = GetFinalMaxHPForIndex(activeIndex);
        float ratio = maxHP > 0.01f ? (float)dmg_final / maxHP : 0f;
        Emit(BattleEvent.Damage(BattleSide.Wild, BattleSide.Player, dmg_final, (dr.crit && !df.cannotBeCrit), dr.effectiveness, ratio, (preventedByGuardRaw > 0f) || (shieldAbsorbF > 0f) || (titleShieldAbsorbF > 0f)));
        if (!HasBattleEventConsumers && feedback) feedback.PlayHitReaction(BattleFeedbackManager.BattleFeedbackSide.Player, dr.crit && !df.cannotBeCrit, ratio, wasGuarded: (preventedByGuardRaw > 0f) || (shieldAbsorbF > 0f) || (titleShieldAbsorbF > 0f));

        if (preventedByGuardRaw > 0f &&
            pendingGuardShield != null &&
            activeIndex >= 0 &&
            activeIndex < pendingGuardShield.Length &&
            guardConvertPct > 0f)
        {
            float shieldGain = preventedByGuardRaw * guardConvertPct;
            pendingGuardShield[activeIndex] += shieldGain;
            if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                yield return Say($"{GetName(activeIndex)} stores {Mathf.RoundToInt(shieldGain)} damage as a guard shield for the next round.", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        TitlesAdapter.OnHitTaken(teamIds[activeIndex], dmg_incoming, dr.crit && !df.cannotBeCrit);

        yield return Say($"{attackerName} hits {GetName(activeIndex)} for {dmg_final}!", BattleLineTag.Result);

        if (dr.crit && !df.cannotBeCrit)
            BattleLogger.AddKeyMoment($"CRIT: {attackerName} → {GetName(activeIndex)} ({dmg_final})");

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f)
            {
                if (!ShouldSkipNarration(BattleLineTag.SuperEffective | BattleLineTag.Flavor))
                    yield return Say("It's super effective!", BattleLineTag.SuperEffective | BattleLineTag.Flavor);
            }
            else if (dr.effectiveness < 0.85f)
            {
                if (!ShouldSkipNarration(BattleLineTag.NotEffective | BattleLineTag.Flavor))
                    yield return Say("It's not very effective...", BattleLineTag.NotEffective | BattleLineTag.Flavor);
            }
        }

        if (dr.crit && !df.cannotBeCrit)
        {
            if (!ShouldSkipNarration(BattleLineTag.Crit | BattleLineTag.Flavor))
                yield return Say("Critical hit!", BattleLineTag.Crit | BattleLineTag.Flavor);
            _totalCritsThisBattle++;
        }

        // Centralized KO messaging (fires only on HP crossing >0 → 0)
        yield return MaybeSayKO_Player(victimName, prePlayerHP, teamHP[activeIndex]);

        _totalDamageTakenThisBattle += dmg_final;

        if (!playerTookFirstIncomingThisBattle)
            playerTookFirstIncomingThisBattle = true;

        if (ctx != null && !ctx.rescueUsed && ctx.rescueHealPct > 0f && teamHP[activeIndex] > 0f)
        {
            float curMax = GetFinalMaxHPForIndex(activeIndex);
            float thresholdHP = curMax * (ctx.rescueThreshold > 0f ? ctx.rescueThreshold : 0.4f);
            if (preHP > thresholdHP && teamHP[activeIndex] <= thresholdHP)
            {
                ctx.rescueUsed = true;
                float healAmt = curMax * ctx.rescueHealPct;
                TryAddHPToActive(healAmt);
                yield return Say($"{GetName(activeIndex)} triage heals {Mathf.RoundToInt(healAmt)} HP!", BattleLineTag.Result);
                AudioManager.I?.PlaySfx(SfxType.Heal);
            }
        }

        if (ctx != null && !ctx.surgeApplied)
        {
            float curMax = GetFinalMaxHPForIndex(activeIndex);
            if (teamHP[activeIndex] <= curMax * 0.5f && ctx.surgeAtkBonusPct > 0f)
            {
                ctx.surgeApplied = true;
                ctx.attackBonusPct += ctx.surgeAtkBonusPct;
                yield return Say($"{GetName(activeIndex)} becomes enraged (+{Mathf.RoundToInt(ctx.surgeAtkBonusPct * 100f)}% ATK)!", BattleLineTag.Result);
                AudioManager.I?.PlaySfx(SfxType.Clutch);
            }
        }
    }




    private bool CheckEnd()
    {
        if (IsWildKO())
        {
            BattleLogger.Log("Wild monster fainted!", LogScope.Battle);
            AudioManager.I?.PlaySfx(SfxType.KO);
            Emit(BattleEvent.KO(BattleSide.Wild));
            if (!HasBattleEventConsumers && feedback) feedback.PlayKO(BattleFeedbackManager.BattleFeedbackSide.Wild);
EndBattle(true);
            return true;
        }
        if (IsTeamKO())
        {
            BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
            AudioManager.I?.PlaySfx(SfxType.KO);
            Emit(BattleEvent.KO(BattleSide.Player));
            if (!HasBattleEventConsumers && feedback) feedback.PlayKO(BattleFeedbackManager.BattleFeedbackSide.Player);
EndBattle(false);
            return true;
        }
        return false;
    }


    private void ForceEndBattleEarly(bool victory, bool escaped = false)
    {
        BattleCalc.ResetRng();
        SetIsPlayerTurn(false);
        pendingAction = PlayerAction.None;
        ResetStatusIcons();

        if (benchBtn1) benchBtn1.interactable = false;
        if (benchBtn2) benchBtn2.interactable = false;

        var result = new BattleResult
        {
            victory = victory,
            escaped = escaped,
            creditsGained = 0,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = 0f,
            critCount = 0,
            turnsSurvived = 0,
            damageTaken = 0,
            damageDealt = 0,
            gotFirstHit = false
        };

        onEnd?.Invoke(result);
        GameEvents.BattleFinished?.Invoke(result);
    }

    private bool ShouldSkipNarration(BattleLineTag tags)
    {
        bool condensed = SettingsManager.I != null && SettingsManager.I.GetCondensedBattleText();
        bool autoCompress = SettingsManager.I != null && SettingsManager.I.GetCompressAutoBattleText();

        bool isAuto = AutoResolveActive || !manualTurns;

        if (condensed && (tags & BattleLineTag.Result) == 0)
            return true;

        if (isAuto && autoCompress && (tags & BattleLineTag.Flavor) != 0)
            return true;

        return false;
    }

    private WaitForSecondsRealtime Wait(float t)
    {
        float scaled = Mathf.Max(0.01f, t / Mathf.Max(0.01f, battleSpeed));
        return new WaitForSecondsRealtime(scaled);
    }

    private IEnumerator CoWaitScaled(float t)
    {
        float scaled = Mathf.Max(0.01f, t / Mathf.Max(0.01f, battleSpeed));
        float end = Time.unscaledTime + scaled;
        while (Time.unscaledTime < end)
            yield return null;
    }




    private IEnumerator CoWaitUnscaled(float seconds)
    {
        float s = Mathf.Max(0f, seconds);
        if (s <= 0f) yield break;

        float end = Time.unscaledTime + s;
        while (Time.unscaledTime < end)
            yield return null;
    }

}
