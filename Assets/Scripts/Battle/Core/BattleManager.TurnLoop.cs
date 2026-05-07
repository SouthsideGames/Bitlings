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
        bool playerFirstBySpeed = GetPlayerActsFirstBySpeed();
        MonsterDataSO playerDef = GetTeamDefSafe(activeIndex);
        float spawnDelay = Mathf.Max(0f, spawnDelayBetweenMonsters);
        float fallbackFadeTime = Mathf.Max(0.01f, duration * 0.5f);

        PrepareBattleStartInfoFade();

        if (feedback != null)
            yield return feedback.Co_RevealPanels(
                wildCG,
                playerCG,
                duration,
                playerFirstBySpeed,
                playerDef,
                wildDef,
                onRevealStart: StartSideInfoFadeForIntro,
                betweenSpawnDelay: spawnDelay,
                onSpawnAnnounce: Co_AnnounceSpawnLine);
        else
        {
            if (playerFirstBySpeed)
            {
                StartSideInfoFadeForIntro(BattleFeedbackManager.BattleFeedbackSide.Player, fallbackFadeTime);
                yield return Co_AnnounceSpawnLine(playerDef);

                if (spawnDelay > 0f)
                    yield return CoWaitUnscaled(spawnDelay);

                StartSideInfoFadeForIntro(BattleFeedbackManager.BattleFeedbackSide.Wild, fallbackFadeTime);
                yield return Co_AnnounceSpawnLine(wildDef);
            }
            else
            {
                StartSideInfoFadeForIntro(BattleFeedbackManager.BattleFeedbackSide.Wild, fallbackFadeTime);
                yield return Co_AnnounceSpawnLine(wildDef);

                if (spawnDelay > 0f)
                    yield return CoWaitUnscaled(spawnDelay);

                StartSideInfoFadeForIntro(BattleFeedbackManager.BattleFeedbackSide.Player, fallbackFadeTime);
                yield return Co_AnnounceSpawnLine(playerDef);
            }

            yield return CoWaitUnscaled(Mathf.Max(0f, duration));
        }

        if (wildCG) wildCG.alpha = 1f;
        if (playerCG) playerCG.alpha = 1f;

        FinalizeBattleStartIntroVisuals();

        yield return Co_StartBattleNow();
    }

    private void FinalizeBattleStartIntroVisuals()
    {
        FinalizeCoreIconIntroAlpha(playerIcon);
        FinalizeCoreIconIntroAlpha(wildIcon);
        FinalizeOwnedIconIntroAlpha();
    }

    private void FinalizeCoreIconIntroAlpha(Graphic icon)
    {
        if (!icon) return;

        float targetAlpha;
        if (!_battleStartCoreIconTargetAlpha.TryGetValue(icon, out targetAlpha))
            targetAlpha = 1f;

        SetGraphicAlphaForIntroFade(icon, targetAlpha);
    }

    private void FinalizeOwnedIconIntroAlpha()
    {
        if (!ownedCapturedIcon || !ownedCapturedIcon.activeSelf) return;

        var graphics = ownedCapturedIcon.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (!graphics[i]) continue;

            float targetAlpha;
            if (!_battleStartHpBarTargetAlpha.TryGetValue(graphics[i], out targetAlpha))
                targetAlpha = 1f;

            SetGraphicAlphaForIntroFade(graphics[i], targetAlpha);
        }
    }

    private void PrepareBattleStartInfoFade()
    {
        _battleStartInfoTargetAlpha.Clear();
        _battleStartHpBarTargetAlpha.Clear();
        _battleStartCoreIconTargetAlpha.Clear();

        PrepareInfoTextForIntroFade(playerNameText);
        PrepareInfoTextForIntroFade(playerLevelText);
        PrepareInfoTextForIntroFade(playerIdText);
        PrepareInfoTextForIntroFade(playerTypeText);
        PrepareInfoTextForIntroFade(playerRarityText);
        PrepareInfoTextForIntroFade(playerHPText);
        PrepareInfoTextForIntroFade(playerATKText);
        PrepareInfoTextForIntroFade(playerDEFText);
        PrepareInfoTextForIntroFade(playerSPDText);

        PrepareInfoTextForIntroFade(wildNameText);
        PrepareInfoTextForIntroFade(wildLevelText);
        PrepareInfoTextForIntroFade(wildIdText);
        PrepareInfoTextForIntroFade(wildTypeText);
        PrepareInfoTextForIntroFade(wildRarityText);
        PrepareInfoTextForIntroFade(wildHPText);
        PrepareInfoTextForIntroFade(wildATKText);
        PrepareInfoTextForIntroFade(wildDEFText);
        PrepareInfoTextForIntroFade(wildSPDText);

        PrepareHpBarForIntroFade(playerHPBar);
        PrepareHpBarForIntroFade(wildHPBar);

        PrepareCoreIconForIntroFade(playerIcon);
        PrepareCoreIconForIntroFade(wildIcon);

        PrepareOwnedIconForIntroFade();
    }

    private void PrepareCoreIconForIntroFade(Graphic icon)
    {
        if (!icon) return;

        float targetAlpha = Mathf.Clamp01(icon.color.a);
        if (targetAlpha <= 0.001f)
            targetAlpha = 1f;

        _battleStartCoreIconTargetAlpha[icon] = targetAlpha;
        SetGraphicAlphaForIntroFade(icon, 0f);
    }

    private void PrepareHpBarForIntroFade(Slider slider)
    {
        if (!slider) return;

        var graphics = slider.GetComponentsInChildren<Graphic>(includeInactive: true);
        if (graphics == null || graphics.Length == 0) return;

        for (int i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (!graphic) continue;

            float targetAlpha = Mathf.Clamp01(graphic.color.a);
            _battleStartHpBarTargetAlpha[graphic] = targetAlpha;
            SetGraphicAlphaForIntroFade(graphic, 0f);
        }
    }

    private void PrepareInfoTextForIntroFade(TMP_Text text)
    {
        if (!text) return;

        float targetAlpha = Mathf.Clamp01(text.color.a);
        _battleStartInfoTargetAlpha[text] = targetAlpha;
        SetInfoTextAlpha(text, 0f);
    }

    private void StartSideInfoFadeForIntro(BattleFeedbackManager.BattleFeedbackSide side, float fadeDuration)
    {
        float duration = Mathf.Max(0f, fadeDuration);

        if (side == BattleFeedbackManager.BattleFeedbackSide.Player)
        {
            FadeInfoTextToTarget(playerNameText, duration);
            FadeInfoTextToTarget(playerLevelText, duration);
            FadeInfoTextToTarget(playerIdText, duration);
            FadeInfoTextToTarget(playerTypeText, duration);
            FadeInfoTextToTarget(playerRarityText, duration);
            FadeInfoTextToTarget(playerHPText, duration);
            FadeInfoTextToTarget(playerATKText, duration);
            FadeInfoTextToTarget(playerDEFText, duration);
            FadeInfoTextToTarget(playerSPDText, duration);
            FadeHpBarToTarget(playerHPBar, duration);
            FadeCoreIconToTarget(playerIcon, duration);
            return;
        }

        FadeInfoTextToTarget(wildNameText, duration);
        FadeInfoTextToTarget(wildLevelText, duration);
        FadeInfoTextToTarget(wildIdText, duration);
        FadeInfoTextToTarget(wildTypeText, duration);
        FadeInfoTextToTarget(wildRarityText, duration);
        FadeInfoTextToTarget(wildHPText, duration);
        FadeInfoTextToTarget(wildATKText, duration);
        FadeInfoTextToTarget(wildDEFText, duration);
        FadeInfoTextToTarget(wildSPDText, duration);
        FadeHpBarToTarget(wildHPBar, duration);
        FadeCoreIconToTarget(wildIcon, duration);
        FadeOwnedIconToTarget(duration);
    }

    private void PrepareOwnedIconForIntroFade()
    {
        if (!ownedCapturedIcon || !ownedCapturedIcon.activeSelf) return;

        var graphics = ownedCapturedIcon.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (!graphics[i]) continue;
            _battleStartHpBarTargetAlpha[graphics[i]] = Mathf.Clamp01(graphics[i].color.a);
            SetGraphicAlphaForIntroFade(graphics[i], 0f);
        }
    }

    private void FadeOwnedIconToTarget(float fadeDuration)
    {
        if (!ownedCapturedIcon || !ownedCapturedIcon.activeSelf) return;

        var graphics = ownedCapturedIcon.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (!graphics[i]) continue;

            float targetAlpha;
            if (!_battleStartHpBarTargetAlpha.TryGetValue(graphics[i], out targetAlpha))
                targetAlpha = 1f;

            FadeGraphicToTarget(graphics[i], targetAlpha, fadeDuration);
        }
    }

    private void FadeCoreIconToTarget(Graphic icon, float fadeDuration)
    {
        if (!icon) return;

        float targetAlpha;
        if (!_battleStartCoreIconTargetAlpha.TryGetValue(icon, out targetAlpha))
            targetAlpha = 1f;

        FadeGraphicToTarget(icon, targetAlpha, fadeDuration);
    }

    private void FadeHpBarToTarget(Slider slider, float fadeDuration)
    {
        if (!slider) return;

        var graphics = slider.GetComponentsInChildren<Graphic>(includeInactive: true);
        if (graphics == null || graphics.Length == 0) return;

        for (int i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (!graphic) continue;

            float targetAlpha;
            if (!_battleStartHpBarTargetAlpha.TryGetValue(graphic, out targetAlpha))
                targetAlpha = 1f;

            FadeGraphicToTarget(graphic, targetAlpha, fadeDuration);
        }
    }

    private void FadeInfoTextToTarget(TMP_Text text, float fadeDuration)
    {
        if (!text) return;

        float targetAlpha;
        if (!_battleStartInfoTargetAlpha.TryGetValue(text, out targetAlpha))
            targetAlpha = 1f;

        LeanTween.cancel(text.gameObject);

        if (fadeDuration <= 0f)
        {
            SetInfoTextAlpha(text, targetAlpha);
            return;
        }

        float startAlpha = Mathf.Clamp01(text.color.a);
        LeanTween.value(text.gameObject, startAlpha, targetAlpha, fadeDuration)
            .setIgnoreTimeScale(true)
            .setEaseOutQuad()
            .setOnUpdate((float a) =>
            {
                if (!text) return;
                SetInfoTextAlpha(text, a);
            });
    }

    private static void FadeGraphicToTarget(Graphic graphic, float targetAlpha, float fadeDuration)
    {
        if (!graphic) return;

        LeanTween.cancel(graphic.gameObject);

        if (fadeDuration <= 0f)
        {
            SetGraphicAlphaForIntroFade(graphic, targetAlpha);
            return;
        }

        float startAlpha = Mathf.Clamp01(graphic.color.a);
        LeanTween.value(graphic.gameObject, startAlpha, Mathf.Clamp01(targetAlpha), fadeDuration)
            .setIgnoreTimeScale(true)
            .setEaseOutQuad()
            .setOnUpdate((float a) =>
            {
                if (!graphic) return;
                SetGraphicAlphaForIntroFade(graphic, a);
            });
    }

    private static void SetGraphicAlphaForIntroFade(Graphic graphic, float alpha)
    {
        if (!graphic) return;

        float a = Mathf.Clamp01(alpha);
        var c = graphic.color;
        c.a = a;
        graphic.color = c;
        graphic.canvasRenderer.SetAlpha(a);
    }

    private static void SetInfoTextAlpha(TMP_Text text, float alpha)
    {
        if (!text) return;

        float a = Mathf.Clamp01(alpha);
        var c = text.color;
        c.a = a;
        text.color = c;
        text.canvasRenderer.SetAlpha(a);
    }

    private IEnumerator Co_AnnounceSpawnLine(MonsterDataSO def)
    {
        if (def == null) yield break;

        string name = string.IsNullOrEmpty(def.displayName) ? "Bitling" : def.displayName;
        yield return Say($"{name} clocking in!", BattleLineTag.Result);
    }

    private bool GetPlayerActsFirstBySpeed()
    {
        int pSpeed;
        if (_stats != null)
            pSpeed = Mathf.Max(1, _stats.GetEffectivePlayer(activeIndex).spd);
        else
            pSpeed = Mathf.Max(1, GetProgressionTotalSPDForIndex(activeIndex));

        int wSpeed;
        if (_stats != null)
            wSpeed = Mathf.Max(1, _stats.GetEffectiveWild().spd);
        else
            wSpeed = Mathf.Max(1, wildBaseSpeed);

        return pSpeed >= wSpeed;
    }




    private IEnumerator Co_StartBattleNow()
    {
        _turnIndex = 0;
        // Reset status tick guards for this battle.
        _lastPlayerStatusTickTurnIndex = int.MinValue;
        _lastWildStatusTickTurnIndex = int.MinValue;
        EnsureBattleRngInitialized();
        inBattle = true;
        startTime = Time.unscaledTime;

        var vsName = wildDef ? $"{GetWildDisplayName("Unknown")} (Lv {wildLevel})" : "Unknown";
        BattleLogger.BeginBattle(vsName, BattleSeed, BattleSeedLabel);

        _combatantNameScratch.Clear();
        for (int i = 0; i < 3; i++)
        {
            string teamName = GetName(i);
            if (!string.IsNullOrWhiteSpace(teamName))
                _combatantNameScratch.Add(teamName);
        }

        _enemyNameScratch.Clear();
        _enemyNameScratch.Add(GetWildDisplayName("Foe"));
        BattleLogger.SetCombatants(_combatantNameScratch, _enemyNameScratch);

        // Reset key moment snapshot for this battle.
        int livingCount = 0;
        for (int i = 0; i < teamCount; i++)
        {
            if (teamDefs != null && i < teamDefs.Length && teamDefs[i] != null && teamHP != null && i < teamHP.Length && teamHP[i] > 0f)
                livingCount++;
        }
        BattleLogger.SetKeyMomentsCap(20 + (livingCount * 5)); // UPGRADED: cap scales with roster size.
        BattleLogger.ClearKeyMoments();

        if (wildDef)
            BattleLogger.Log($"A wild {GetWildDisplayName("Foe")} (Lv {wildLevel}) appeared!", LogScope.Battle);
        else
            BattleLogger.Log("A wild foe appeared!", LogScope.Battle);

        if (_battleDifficultyMode > 0)
        {
            BattleLogger.Log(
                $"Difficulty: {DifficultyModeToLabel(_battleDifficultyMode)} (Wild HP x{_battleDifficultyHpMul:0.##}, ATK x{_battleDifficultyAtkMul:0.##}, DEF x{_battleDifficultyDefMul:0.##}, SPD x{_battleDifficultySpdMul:0.##})",
                LogScope.Battle
            );
        }

        string personalityLabel = GetWildPersonalityLabel();
        if (!string.IsNullOrEmpty(personalityLabel) && wildDef && wildDef.Personality != null)
        {
            if (!string.IsNullOrEmpty(wildDef.Personality.description))
                BattleLogger.Log($"Personality: {personalityLabel} – {wildDef.Personality.description}", LogScope.Battle);
            else
                BattleLogger.Log($"Personality: {personalityLabel}.", LogScope.Battle);
        }

        // Log battle-start buff summary for each team member with active bonuses
        for (int i = 0; i < 3; i++)
        {
            var statLines = Stats.GetPlayerStatLines(i);
            if (statLines == null || statLines.Count == 0)
                continue;

            string monsterName = GetName(i);
            var jctx = GetJobCtxSafe(i);
            string titleId = GetTeamTitleIdSafe(i);
            
            // Build source label: job name + "Title" if both present, otherwise whichever is active
            string sourceLabel = null;
            if (jctx != null && jctx.job != JobType.None && !string.IsNullOrEmpty(titleId))
            {
                sourceLabel = JobStrings.SiteName(jctx.job) + " Title";
            }
            else if (jctx != null && jctx.job != JobType.None)
            {
                sourceLabel = JobStrings.SiteName(jctx.job);
            }
            else if (!string.IsNullOrEmpty(titleId))
            {
                sourceLabel = "Title";
            }

            if (string.IsNullOrEmpty(sourceLabel))
                continue;

            // Get stat deltas: final vs adjusted
            var stages = Stats.GetPlayerBreakdownStages(i);
            int atkDelta = stages.final.atk - stages.adjusted.atk;
            int defDelta = stages.final.def - stages.adjusted.def;
            int spdDelta = stages.final.spd - stages.adjusted.spd;
            float hpPctDelta = (stages.final.maxHP > 0 && stages.adjusted.maxHP > 0)
                ? ((float)stages.final.maxHP / stages.adjusted.maxHP) - 1f
                : 0f;

            // Only log if at least one stat changed
            if (atkDelta != 0 || defDelta != 0 || spdDelta != 0 || !Mathf.Approximately(hpPctDelta, 0f))
            {
                BattleLogger.LogTitleStatSummary(
                    monsterName, sourceLabel,
                    atkDelta, 0f,
                    defDelta, 0f,
                    spdDelta, 0f,
                    hpPctDelta,
                    LogScope.Battle
                );
            }
        }

        // Log mentor/honor bonuses at battle start (once per unique bonus)
        if (HonorService.CanApplyCombatBonuses())
        {
            var bonus = HonorService.GetActiveBonus();
            if (bonus != null && (bonus.atkPct > 0f || bonus.defPct > 0f))
            {
                // Try to find the mentor's display name
                string mentorName = null;
                var mentorList = SaveManager.GetMentorHallSnapshot();
                if (mentorList != null)
                {
                    for (int mi = 0; mi < mentorList.Count; mi++)
                    {
                        var m = mentorList[mi];
                        if (m != null && m.mentorUID == bonus.honoredUID)
                        {
                            mentorName = m.displayName;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(mentorName))
                    mentorName = "A retired monster";

                // Find a team member with matching type to show as the guided one
                string guidedMonsterName = null;
                for (int i = 0; i < 3; i++)
                {
                    var monsterDef = GetTeamDefSafe(i);
                    if (monsterDef != null && monsterDef.type == bonus.honoredType)
                    {
                        guidedMonsterName = GetName(i);
                        break; // Log only once
                    }
                }

                if (!string.IsNullOrEmpty(guidedMonsterName))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"<color={BattleLogColors.Title}>[MENTOR]</color> ");
                    sb.Append($"<color={BattleLogColors.Name}>{guidedMonsterName}</color> is guided by ");
                    sb.Append($"<color={BattleLogColors.Name}>{mentorName}</color> → ");

                    if (bonus.atkPct > 0f)
                    {
                        int atkPctI = Mathf.RoundToInt(bonus.atkPct * 100f);
                        sb.Append($"<color={BattleLogColors.Buff}>ATK +{atkPctI}%</color>");
                    }

                    if (bonus.defPct > 0f)
                    {
                        if (bonus.atkPct > 0f) sb.Append("  ");
                        int defPctI = Mathf.RoundToInt(bonus.defPct * 100f);
                        sb.Append($"<color={BattleLogColors.Buff}>DEF +{defPctI}%</color>");
                    }

                    BattleLogger.Log(sb.ToString(), LogScope.Battle);
                }
            }
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();
        // BattleStart titles are applied once in Begin() via ApplyBattleStartTitles().
        Debug_LogActiveTitlesSnapshot("BattleStart");

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
                    EndBattleRouted(false);
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugTitles && debugTitlesEveryTurn)
                DevLog.Log($"[TurnLoop] ROUND {round} -> TurnIndex advanced to {_turnIndex} (one full round: player + wild act)", this);
#endif

            TitlesAdapter.OnTurnAdvanced(_turnIndex);
            // UPGRADED: dirty only on change, not every turn

            // Conditional Titles: show brief feedback when conditional effects become active/inactive.
            // (We keep the detailed math in BattleLogger.)
            if (TryConsumeConditionalTitleFeedback(out var _condMods, out var _condBattleLine, out var _condLogLine))
            {
                RequestBattleStatRebuild(BattleStatRebuildReason.TurnAdvanced);
                if (!string.IsNullOrEmpty(_condLogLine))
                    BattleLogger.LogTitleProc("Conditional Titles", _condLogLine);

                if (!string.IsNullOrEmpty(_condBattleLine) && !AutoResolveActive)
                    yield return Say(_condBattleLine, BattleLineTag.None);
            }

            if (debugTitles && debugTitlesEveryTurn)
                Debug_LogActiveTitlesSnapshot("TurnAdvanced");

            if (swappedFromKO)
            {
                ClampAndPushActiveHP();
                ApplyActiveToUI();
                FireOnEntryEffects(activeIndex);
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

            // Initiative uses centralized battle stats (Adjusted + job + titles + boosters + temp).
            int pSpeed = 1;
            if (_stats != null)
                pSpeed = Mathf.Max(1, _stats.GetEffectivePlayer(activeIndex).spd);
            else
                pSpeed = Mathf.Max(1, GetProgressionTotalSPDForIndex(activeIndex));

            // Speed boosters that are "spent" on initiative should apply here.
            if (_rules.allowBoosters && BattleBoosterController.I != null)
            {
                int consumedSpeedBonus = Mathf.Max(0, BattleBoosterController.I.ConsumeSpeedBonusForInitiative());
                if (consumedSpeedBonus > 0)
                    RequestBattleStatRebuild(BattleStatRebuildReason.TurnAdvanced);
                pSpeed = Mathf.Max(1, pSpeed + consumedSpeedBonus);
            }

            // Status: Soaked reduces speed (initiative).
            pSpeed = Mathf.Max(1, Mathf.RoundToInt(pSpeed * Mathf.Max(0.1f, GetActivePlayerSoakedSpeedMultiplier())));

            int wSpeed = 1;
            if (_stats != null)
                wSpeed = Mathf.Max(1, _stats.GetEffectiveWild().spd);
            else
                wSpeed = Mathf.Max(1, wildBaseSpeed);


            // Status: Soaked reduces speed (initiative).
            wSpeed = Mathf.Max(1, Mathf.RoundToInt(wSpeed * Mathf.Max(0.1f, GetWildSoakedSpeedMultiplier())));

            bool playerFirst;
            if (pSpeed > wSpeed) playerFirst = true;
            else if (pSpeed < wSpeed) playerFirst = false;
            else playerFirst = Rng01() < 0.5f;

            EnemyAction wildChoice = ChooseEnemyAction();

            if (wildChoice != EnemyAction.None)
                yield return Co_TelegraphWildIntent(wildChoice);
            else
                RefreshStatusIconsFromState();

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

                        // Status tick at start of the player's turn (Phase 4).
                        // If an action-skip status triggers (Freeze/Shock), skip input + action entirely this turn.
                        if (TryProcessTurnStartStatus_PlayerActive_OncePerRound(out var skipBy))
                        {
                            SetIsPlayerTurn(false);
                            pendingAction = PlayerAction.None;
                            GameEvents.OnBattleStateChanged?.Invoke();

                            if (!ShouldSkipNarration(BattleLineTag.Result))
                            {
                                string who = GetName(activeIndex);
                                if (skipBy == StatusType.Shock)
                                    yield return Say($"{who} is Shocked! Action failed!", BattleLineTag.Result);
                                else
                                    yield return Say($"{who} is Frozen and can't act!", BattleLineTag.Result);
                            }

                            queuedChoice = PlayerAction.None;
                        }

                        if (queuedChoice != PlayerAction.None)
                        {
                            float choiceStart = Time.unscaledTime;
                            float pausedAccum = 0f;
                            float pauseStartedAt = -1f;

                            while (inBattle && pendingAction == PlayerAction.None)
                        {
                            bool pauseFailsafe = _narrationLock || ShouldPauseAutoQueueAttack();

                            if (enableAutoQueueAttack && autoQueueAttackAfterSeconds > 0f && !pauseFailsafe)
                            {
                                if (pauseStartedAt >= 0f)
                                {
                                    pausedAccum += Mathf.Max(0f, Time.unscaledTime - pauseStartedAt);
                                    pauseStartedAt = -1f;
                                }

                                float elapsed = Mathf.Max(0f, (Time.unscaledTime - choiceStart) - pausedAccum);
                                // Countdown UI: only becomes active for the last N seconds.
                                float remaining = autoQueueAttackAfterSeconds - elapsed;
                                bool showCountdown = (autoQueueCountdownShowAtSeconds > 0f) && (remaining <= autoQueueCountdownShowAtSeconds) && (remaining > 0f);
                                EmitAutoQueueCountdown(remaining, showCountdown);

                                if (elapsed >= autoQueueAttackAfterSeconds)
                                {
                                    // Make sure UI hits 0 then hides.
                                    EmitAutoQueueCountdown(0f, (autoQueueCountdownShowAtSeconds > 0f));
                                    EmitAutoQueueCountdown(0f, false);
                                    pendingAction = ChoosePlayerFailsafeAction();
                                    BattleLogger.Log($"[Battle] Failsafe: auto-queued {pendingAction} (personality) after {autoQueueAttackAfterSeconds:0}s idle.", LogScope.Battle);
                                    break;
                                }
                            }
                            else
                            {
                                if (pauseFailsafe && pauseStartedAt < 0f)
                                    pauseStartedAt = Time.unscaledTime;

                                EmitAutoQueueCountdown(0f, false);
                            }

                            yield return null;
                        }
                        }

                        if (queuedChoice != PlayerAction.None)
                            queuedChoice = pendingAction;
                        pendingAction = PlayerAction.None;
                        // Ensure countdown is hidden once a choice is made.
                        EmitAutoQueueCountdown(0f, false);
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
                                GrantFailedDefendCritBonus(BattleSide.Player);
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
                            yield return ResolveQueuedSwap();
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

                                            // Ensure charge icon updates immediately when focus succeeds.
                                            Emit(BattleEvent.ChargeChanged(BattleSide.Player, true));
                                            if (!HasBattleEventConsumers && feedback)
                                                feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Player, true);
                                        }

                                        BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                                        BattleLogger.Log($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", LogScope.Battle);                                        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Focus"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Focus
                                        );
                                        NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Focus);
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
                                        yield return ResolveQueuedSwap();
                                        NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Swap);
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
                                        NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Run);
                                        // Run does not affect guard/charge, but keep icons correct anyway
                                        RefreshStatusIconsFromState();

                                        if (escaped)
                                        {
                                            BattleLogger.Log($"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                                            if (feedback) feedback.PlayRunSfx();
                                            EndBattleRouted(false, true);
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
                                    
                                        NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Defend);
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
            if (_rules.allowBoosters && BattleBoosterController.I != null)
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
// Status tick at start of the player's turn (Phase 4)
// If an action-skip status triggers (Freeze/Shock), skip input + action entirely this turn.
if (TryProcessTurnStartStatus_PlayerActive_OncePerRound(out var skipBy))
{
    // Immediately end the player's turn so the action bar cannot be used.
    SetIsPlayerTurn(false);
    pendingAction = PlayerAction.None;
    GameEvents.OnBattleStateChanged?.Invoke();

    {
        string who = GetName(activeIndex);
        if (skipBy == StatusType.Shock)
            yield return Say($"{who} is Shocked! Action failed!", BattleLineTag.Result);
        else
            yield return Say($"{who} is Frozen and can't act!", BattleLineTag.Result);
    }

    // If the tick caused a KO, handle it the same way as any other KO would be handled later.
    if (teamHP != null && activeIndex >= 0 && activeIndex < teamHP.Length && teamHP[activeIndex] <= 0.01f)
        yield return MaybeSayKO_Player(GetName(activeIndex), 1f, 0f);

    yield break;
}

// If a DOT tick KO'd the active monster, stop here (battle loop will detect KO and swap/end).
if (teamHP != null && activeIndex >= 0 && activeIndex < teamHP.Length && teamHP[activeIndex] <= 0.01f)
{
    SetIsPlayerTurn(false);
    pendingAction = PlayerAction.None;
    GameEvents.OnBattleStateChanged?.Invoke();
    yield break;
}


        float choiceStart = Time.unscaledTime;
        float pausedAccum = 0f;
        float pauseStartedAt = -1f;

        while (inBattle && pendingAction == PlayerAction.None)
        {
            bool pauseFailsafe = _narrationLock || ShouldPauseAutoQueueAttack();

            if (enableAutoQueueAttack && autoQueueAttackAfterSeconds > 0f && !pauseFailsafe)
            {
                if (pauseStartedAt >= 0f)
                {
                    pausedAccum += Mathf.Max(0f, Time.unscaledTime - pauseStartedAt);
                    pauseStartedAt = -1f;
                }

                float elapsed = Mathf.Max(0f, (Time.unscaledTime - choiceStart) - pausedAccum);
                // Countdown UI: only becomes active for the last N seconds.
                float remaining = autoQueueAttackAfterSeconds - elapsed;
                bool showCountdown = (autoQueueCountdownShowAtSeconds > 0f) && (remaining <= autoQueueCountdownShowAtSeconds) && (remaining > 0f);
                EmitAutoQueueCountdown(remaining, showCountdown);

                if (elapsed >= autoQueueAttackAfterSeconds)
                {
                    // Make sure UI hits 0 then hides.
                    EmitAutoQueueCountdown(0f, (autoQueueCountdownShowAtSeconds > 0f));
                    EmitAutoQueueCountdown(0f, false);
                    pendingAction = ChoosePlayerFailsafeAction();
                    BattleLogger.Log($"[Battle] Failsafe: auto-queued {pendingAction} (personality) after {autoQueueAttackAfterSeconds:0}s idle.", LogScope.Battle);
                    break;
                }
            }
            else
            {
                if (pauseFailsafe && pauseStartedAt < 0f)
                    pauseStartedAt = Time.unscaledTime;

                EmitAutoQueueCountdown(0f, false);
            }

            yield return null;
        }

        var choice = pendingAction;
        pendingAction = PlayerAction.None;
        // Ensure countdown is hidden once a choice is made.
        EmitAutoQueueCountdown(0f, false);
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
                    GrantFailedDefendCritBonus(BattleSide.Player);
                }

                NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Defend);

                break;
            }

            case PlayerAction.Focus:
            {
                ResetDefendStreak();
                ClearPlayerGuardStateForActive(); // safety

                if (chargedNextAttack != null && activeIndex >= 0 && activeIndex < chargedNextAttack.Length)
                {
                    chargedNextAttack[activeIndex] = true;

                    // Ensure charge icon updates immediately when focus succeeds.
                    Emit(BattleEvent.ChargeChanged(BattleSide.Player, true));
                    if (!HasBattleEventConsumers && feedback)
                        feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Player, true);
                }

                BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                BattleLogger.Log($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", LogScope.Battle);                                        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Focus"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Focus
                                        );
                NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Focus);
                RefreshStatusIconsFromState();
                break;
            }

            case PlayerAction.Swap:
            {
                ResetDefendStreak();
                ClearPlayerGuardStateForActive(); // ✅ guard must never carry to a swapped-in monster
                yield return ResolveQueuedSwap();
                NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Swap);
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
                NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Run);
                RefreshStatusIconsFromState();

                if (escaped)
                {
                    BattleLogger.Log($"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                    EndBattleRouted(false, true);
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

        // Auto-resolve: status tick at start of the player's action (manual path ticks before input).
        if (!manualTurns)
        {
            if (TryProcessTurnStartStatus_PlayerActive_OncePerRound(out var skipBy))
            {
                if (!ShouldSkipNarration(BattleLineTag.Result))
                {
                    string who = GetName(activeIndex);
                    if (skipBy == StatusType.Shock)
                        yield return Say($"{who} is Shocked! Action failed!", BattleLineTag.Result);
                    else
                        yield return Say($"{who} is Frozen and can't act!", BattleLineTag.Result);
                }
                isResolvingPlayerTurn = false;
                yield break;
            }

            // If a DOT tick KO'd the active monster, stop here (battle loop will detect KO and swap/end).
            if (teamHP != null && activeIndex >= 0 && activeIndex < teamHP.Length && teamHP[activeIndex] <= 0.01f)
            {
                isResolvingPlayerTurn = false;
                yield break;
            }
        }

        string _playerTurnOwnerId = (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length) ? teamIds[activeIndex] : null;

        try
        {
            if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            {
            isResolvingPlayerTurn = false;
            yield break;
        }

        var playerDef = teamDefs[activeIndex];
        string attacker = GetName(activeIndex);
        string move = GetBasicMoveName(playerDef);
        string foeName = GetWildDisplayName("Foe");

        if (!ShouldSkipNarration(BattleLineTag.Flavor))
            yield return Say($"{attacker} used {move}!", BattleLineTag.Flavor);
        Emit(BattleEvent.ActionWindup(BattleSide.Player));
        if (!HasBattleEventConsumers && feedback) feedback.PlayAttackWindup(BattleFeedbackManager.BattleFeedbackSide.Player);
if (feedback)
            feedback.SpawnBasicAttackVfx(isPlayerSide: true, playerDef: playerDef, wildDef: wildDef);

        yield return CoWaitScaled(0.10f);

                // Centralized ATK stat for this attacker (Adjusted + job + titles + boosters + temp).
        int atkForResolve = 1;
        if (_stats != null)
            atkForResolve = Mathf.Max(1, _stats.GetEffectivePlayer(activeIndex).atk);
        else
            GetProgressionTotalsForIndex(activeIndex, out _, out atkForResolve, out _, out _, out _);


        var jctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float playerCrit = critChancePlayer;
        if (jctx != null)
        {
            playerCrit += jctx.critChanceFlat;
            if (jctx.critBuffTurns > 0)
                playerCrit += jctx.critChanceBonusFirstTurns;
        }
        float playerFailDefendCritBonus = GetFailedDefendCritBonusForAttacker(BattleSide.Player);
        bool playerHadFailDefendCritBonus = playerFailDefendCritBonus > 0f;
        playerCrit += playerFailDefendCritBonus;
        playerCrit = Mathf.Clamp01(playerCrit);

        // IMPORTANT: pass the wild combatant id so wild Titles participate in damage filters,
        // effectiveness modifiers, and defender-side defenses.
        int wildDefForResolve = 0;
        if (_stats != null)
            wildDefForResolve = Mathf.Max(0, _stats.GetEffectiveWild().def);
        else
            wildDefForResolve = Mathf.Max(0, wildBaseDefense);

        if (IsWildSundered())
        {
            int beforeDef = wildDefForResolve;
            wildDefForResolve = Mathf.Max(0, wildDefForResolve - Mathf.Max(0, sunderedDefReduction));
            BattleLogger.Log($"[Status] Sundering reduces {foeName}'s DEF by {Mathf.Max(0, beforeDef - wildDefForResolve)} ({beforeDef}->{wildDefForResolve}).", LogScope.Battle);
        }

        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            _wildCombatIdForTitles, wildDef, wildLevel,
            atkForResolve,
            playerCrit,
            critMultiplier,
            defenderFlatDefenseBonus: 0,
            defenderEffectiveDefenseStat: wildDefForResolve
        );

        ConsumeFailedDefendCritBonusForAttacker(BattleSide.Player);

        if (playerHadFailDefendCritBonus && dr.crit)
            BattleLogger.Log("Critical hit empowered by failed wild defend!", LogScope.Battle);

        TitlesAdapter.OnAttackLanded(teamTitleIds[activeIndex], dr.crit);
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

        
// Status: Rally (Clash) — allies gain minor Attack boost.
// Applies to outgoing damage while any living ally has Rally.
float rallyBonusPct = GetPlayerTeamRallyBonusPct();
if (rallyBonusPct > 0f)
{
    int before = dr.damage;
    dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + rallyBonusPct)));
    if (BattleLogger.Enabled)
        BattleLogger.Log($"[Status] Rally boosts {attacker}'s damage: {before}→{dr.damage} (+{Mathf.RoundToInt(rallyBonusPct * 100f)}%).", LogScope.Battle);
}
// Status: Tailwind (Sky) — first attack during effect deals bonus damage (consumed on use).
float tailwindBonusPct = GetActivePlayerTailwindBonusPct();
bool tailwindConsumed = false;
if (tailwindBonusPct > 0f || _entryTailwindActive)
{
    float effectiveTailwindBonusPct = (tailwindBonusPct > 0f) ? tailwindBonusPct : 0.25f;
    int before = dr.damage;
    dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + effectiveTailwindBonusPct)));
    tailwindConsumed = true;
    _entryTailwindActive = false;
    if (BattleLogger.Enabled)
        BattleLogger.Log($"[Status] Tailwind empowers {attacker}'s first strike: {before}→{dr.damage} (+{Mathf.RoundToInt(effectiveTailwindBonusPct * 100f)}%).", LogScope.Battle);
}

float preventedByWildGuard = 0f;
        bool shadowVeilBlockedWild = false;
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

        // Capture the incoming damage amount AFTER guard reduction but BEFORE shields.
        // TitlesAdapter.OnHitTaken expects a pre-shield damage amount so BattleStartShield consumption
        // remains in sync between BattleManager and TitleManager.
        int dmg_incoming_wild = dmgToApply;

        float absorbedByWildShield = 0f;

        float absorbedByWildTitleShield = 0f;

        // ShadowVeil: immune to damage for the duration.
        if (dmgToApply > 0 && IsWildShadowVeiled())
        {
            shadowVeilBlockedWild = true;
            BattleLogger.Log($"{foeName} is shrouded - damage nullified!", LogScope.Battle);
            BattleLogger.AddKeyMoment($"Shadow Veil: {foeName} ignored damage.");
            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"{foeName} fades into a Shadow Veil—no damage!", BattleLineTag.Flavor);

            dmgToApply = 0;
            ClearWildStatus(reason: "absorbed a hit");
        }

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

            // If this shield came from the Shielded status, track remaining so cleanup only removes the unspent portion.
            _shieldedGrantWild = Mathf.Max(0f, _shieldedGrantWild - absorb);

            dmgToApply = Mathf.Max(0, dmgToApply - Mathf.RoundToInt(absorb));

            if (absorb > 0f)
                if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                    yield return Say($"{foeName}'s shield absorbed {Mathf.RoundToInt(absorb)}!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        // ── Wild shield-break detection: if total shield went from >0 to 0, fire title hook ──
        if ((absorbedByWildTitleShield + absorbedByWildShield) > 0f)
        {
            float wildTotalShieldAfter = wildTitleShieldHP + wildShieldHP;
            if (wildTotalShieldAfter <= 0f && !string.IsNullOrEmpty(_wildCombatIdForTitles))
            {
                try { TitlesAdapter.OnPlayerShieldBroke(_wildCombatIdForTitles); }
                catch (System.Exception ex) { Debug.LogException(ex); }
            }
        }

        if (preventedByWildGuard > 0f && guardConvertPct > 0f && dr.crit)
        {
            float gain = preventedByWildGuard * guardConvertPct;
            wildPendingGuardShield += gain;
            if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
            yield return Say($"{foeName} stores {Mathf.RoundToInt(gain)} damage as a guard shield for the next round (critical hit blocked)!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        float preWildHP = wildHP;
        wildHP = Mathf.Max(0f, wildHP - dmgToApply);
        _totalDamageDealtThisBattle += Mathf.Max(0, dmgToApply);
        PushHPBars();

        // Titles: defender hit hooks for wild (e.g., EventStacks, defensive triggers)
        // Use the pre-shield damage amount so BattleStartShieldTitle consumption is correct.
        if (!string.IsNullOrEmpty(_wildCombatIdForTitles) && dmg_incoming_wild > 0)
            TitlesAdapter.OnHitTaken(_wildCombatIdForTitles, dmg_incoming_wild, dr.crit);

        // Wild conditional titles (e.g., Clutch Booster) depend on current HP.
        // Recompute wild effective stats after HP changes and refresh the UI so stat numbers/colors update.
        RefreshWildEffectiveStatsFromTitles();
        UpdateWildInfoUI();


        float wRatio = wildMaxHP > 0.01f ? (float)dmgToApply / wildMaxHP : 0f;
        Emit(BattleEvent.Damage(BattleSide.Player, BattleSide.Wild, dmgToApply, dr.crit, dr.effectiveness, wRatio, (preventedByWildGuard > 0f) || (absorbedByWildShield > 0f) || (absorbedByWildTitleShield > 0f)));
        if (!HasBattleEventConsumers && feedback) feedback.PlayHitReaction(BattleFeedbackManager.BattleFeedbackSide.Wild, dr.crit, wRatio, wasGuarded: (preventedByWildGuard > 0f) || (absorbedByWildShield > 0f) || (absorbedByWildTitleShield > 0f));
if (!playerLandedFirstHitThisBattle && dr.damage > 0)
            playerLandedFirstHitThisBattle = true;

        if (shadowVeilBlockedWild)
        {
            // Already narrated the veil; keep result line compact.
            yield return Say($"{foeName} takes no damage!", BattleLineTag.Result);
        }
        else
        {
            yield return Say($"{attacker} hits {foeName} for {dmgToApply}!", BattleLineTag.Result);
        }

        // Status: Tailwind is consumed on the first attack it empowers.
        if (tailwindConsumed && activeIndex >= 0 && teamStatus != null && activeIndex < teamStatus.Length && teamStatus[activeIndex] == StatusType.Tailwind)
            ClearTeamStatus(activeIndex, reason: "spent");

        // Status: Leeching (Bug) — heal a portion of damage dealt.
        ApplyLeechHeal(BattleSide.Player, dmgToApply);

        // Status: Phantasmal (Specter) — lose HP when attacking.
        float phantasmalPct = GetActivePlayerPhantasmalSelfDmgPct();
        if (phantasmalPct > 0f && activeIndex >= 0 && teamHP != null && activeIndex < teamHP.Length)
        {
            int selfDmg = Mathf.Max(1, Mathf.RoundToInt(GetFinalMaxHPForIndex(activeIndex) * Mathf.Clamp(phantasmalPct, 0f, 0.9f)));
            float pre = teamHP[activeIndex];
            teamHP[activeIndex] = Mathf.Max(0f, teamHP[activeIndex] - selfDmg);
            PushHPBars();

            if (BattleLogger.Enabled)
                BattleLogger.Log($"[Status] {GetName(activeIndex)} loses {selfDmg} HP from Phantasmal backlash. ({Mathf.CeilToInt(pre)}→{Mathf.CeilToInt(teamHP[activeIndex])})", LogScope.Battle);

            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"{GetName(activeIndex)} is hurt by Phantasmal backlash (-{selfDmg})!", BattleLineTag.Flavor);
        }

        // Status: Foresight — check repeat action to schedule a stun next turn.
        NotifyPlayerActionResolved_ForForesight(activeIndex, PlayerAction.Attack);


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
        }
        finally
        {
            // Tick owner-turn-based Title durations (e.g., BattleStartFlat durationTurns).
            if (inBattle && !string.IsNullOrEmpty(_playerTurnOwnerId))
            {
                TitlesAdapter.OnCombatantTurnEnded(_playerTurnOwnerId);
                RequestBattleStatRebuild(BattleStatRebuildReason.HPChanged);
            }

            isResolvingPlayerTurn = false;
        }
    }




    private IEnumerator EnemyTurn(EnemyAction choice)
    {
        string _wildTurnOwnerId = _wildCombatIdForTitles;

        try
        {
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            yield break;
    // Status tick at start of the wild's turn (Phase 4)
    // If an action-skip status triggers (Freeze/Shock), skip action entirely this turn.
    if (TryProcessTurnStartStatus_Wild_OncePerRound(out var skipBy))
    {
        string who = !string.IsNullOrEmpty(wildNameText?.text) ? wildNameText.text : GetWildDisplayName("Wild");
        if (skipBy == StatusType.Shock)
            yield return Say($"{who} is Shocked! Action failed!", BattleLineTag.Result);
        else
            yield return Say($"{who} is Frozen and can't act!", BattleLineTag.Result);

        // If the tick caused a KO, stop here (battle loop will detect KO and end).
        if (wildHP <= 0.01f)
            yield break;

        yield break;
    }

    // If a DOT tick KO'd the wild, stop here.
    if (wildHP <= 0.01f)
        yield break;

            // If the wild's AI returned no action (usually due to a status), do nothing.
            if (choice == EnemyAction.None)
                yield break;


            if (choice != EnemyAction.Defend)
                yield return CoWaitScaled(0.15f);

            if (choice != EnemyAction.Defend)
                ResetEnemyDefendStreak();

            if (choice != EnemyAction.Focus)
                ResetEnemyFocusStreak();

            if (choice == EnemyAction.Defend)
            {
                string name = GetWildDisplayName("Foe");
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
                    GrantFailedDefendCritBonus(BattleSide.Wild);
                }

                NotifyWildActionResolved_ForForesight(EnemyAction.Defend);

                yield break;
            }

            if (choice == EnemyAction.Focus)
            {
                string name = GetWildDisplayName("Foe");
                bool success = RollEnemyFocusSuccess();

                if (!success)
                {
                    if (!ShouldSkipNarration(BattleLineTag.Flavor))
                        yield return Say($"{name} tried to charge, but lost momentum!", BattleLineTag.Flavor);

                    NotifyWildActionResolved_ForForesight(EnemyAction.Focus);
                    yield break;
                }

                wildChargedNextAttack = true;

                // Ensure charge icon updates immediately when focus succeeds.
                Emit(BattleEvent.ChargeChanged(BattleSide.Wild, true));
                if (!HasBattleEventConsumers && feedback)
                    feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Wild, true);

                if (!ShouldSkipNarration(BattleLineTag.Flavor))
                    yield return Say($"{name} is charging up.", BattleLineTag.Flavor);
                if (!ShouldSkipNarration(BattleLineTag.Flavor))
                    yield return Say($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", BattleLineTag.Flavor);

                Emit(BattleEvent.ActionQueued(BattleSide.Wild, "Focus"));
                if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                    BattleFeedbackManager.BattleFeedbackSide.Wild,
                    BattleFeedbackManager.BattleFeedbackAction.Focus
                );

                NotifyWildActionResolved_ForForesight(EnemyAction.Focus);

                yield break;
            }

            if (choice == EnemyAction.Run)
            {
                string name = GetWildDisplayName("Foe");
                float chance = ComputeEnemyRunChance();
                bool fled = Rng01() < chance;

                if (fled)
                {
                    yield return Say($"{name} fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", BattleLineTag.Result);
                    NotifyWildActionResolved_ForForesight(EnemyAction.Run);
                    _wildEscapedThisBattle = true;
                    EndBattleRouted(false, escaped: true);
                    yield break;
                }
                else
                {
                    yield return Say($"{name} tried to flee, but couldn't! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", BattleLineTag.Result);
                    NotifyWildActionResolved_ForForesight(EnemyAction.Run);
                    yield break;
                }
            }

            if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
                yield break;

            string attackerName = GetWildDisplayName("Foe");
            string move = GetBasicMoveName(wildDef);

            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"{attackerName} used {move}!", BattleLineTag.Flavor);
            Emit(BattleEvent.ActionWindup(BattleSide.Wild));
            if (!HasBattleEventConsumers && feedback) feedback.PlayAttackWindup(BattleFeedbackManager.BattleFeedbackSide.Wild);
    if (feedback)
                feedback.SpawnBasicAttackVfx(isPlayerSide: false, playerDef: teamDefs[activeIndex], wildDef: wildDef);

            yield return CoWaitScaled(0.10f);

            int enemyAtk = 1;
            if (_stats != null)
                enemyAtk = Mathf.Max(1, _stats.GetEffectiveWild().atk);
            else
                enemyAtk = Mathf.Max(1, Mathf.RoundToInt(wildAttackPerTurn));

            var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
            float preHP = teamHP[activeIndex];

            var cmods = GetConditionalModsForActive();

            var df = TitlesAdapter.GetDamageFilter(teamTitleIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);

            float playerCritResist = 0f;
            if (ctx != null)
            {
                playerCritResist += ctx.critResistFlat;
                if (ctx.critResistBuffTurns > 0)
                    playerCritResist += ctx.critResistBonusFirstTurns;
            }

            float wildCritChance = df.cannotBeCrit ? 0f : Mathf.Clamp01(critChanceWild - playerCritResist);
            float wildFailDefendCritBonus = GetFailedDefendCritBonusForAttacker(BattleSide.Wild);
            bool wildHadFailDefendCritBonus = wildFailDefendCritBonus > 0f;
            wildCritChance += wildFailDefendCritBonus;
            wildCritChance = Mathf.Clamp01(wildCritChance);

            // Centralized DEF stat for the defender (Adjusted + job + titles + boosters + temp).
            int defenderEffectiveDefenseStat = 0;
            if (_stats != null)
                defenderEffectiveDefenseStat = Mathf.Max(0, _stats.GetEffectivePlayer(activeIndex).def);
            else
                GetProgressionTotalsForIndex(activeIndex, out _, out _, out defenderEffectiveDefenseStat, out _, out _);

            if (IsActivePlayerSundered())
            {
                int beforeDef = defenderEffectiveDefenseStat;
                defenderEffectiveDefenseStat = Mathf.Max(0, defenderEffectiveDefenseStat - Mathf.Max(0, sunderedDefReduction));
                BattleLogger.Log($"[Status] Sundering reduces {GetName(activeIndex)}'s DEF by {Mathf.Max(0, beforeDef - defenderEffectiveDefenseStat)} ({beforeDef}->{defenderEffectiveDefenseStat}).", LogScope.Battle);
            }

            var dr = BattleCalc.ResolveHit(
                _wildCombatIdForTitles, wildDef, wildLevel,
                teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
                enemyAtk, wildCritChance, critMultiplier,
                defenderFlatDefenseBonus: 0,
                defenderEffectiveDefenseStat: defenderEffectiveDefenseStat
            );

            if (WorldEventSystem.I != null &&
                teamDefs != null &&
                activeIndex >= 0 &&
                activeIndex < teamDefs.Length &&
                teamDefs[activeIndex] != null &&
                teamDefs[activeIndex].type == WorldEventSystem.I.GetBoostedMonsterType())
            {
                int before = dr.damage;
                float defensiveFactor = 1f - (WorldEventSystem.I.GetTypeDamageMultiplier() * 0.5f);
                dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * defensiveFactor));
                if (dr.damage < before)
                    BattleLogger.Log($"World Event defense bonus: {GetName(activeIndex)} reduced incoming damage {before}->{dr.damage}.", LogScope.Battle);
            }

            ConsumeFailedDefendCritBonusForAttacker(BattleSide.Wild);

            if (wildHadFailDefendCritBonus && dr.crit)
                BattleLogger.Log("Critical hit empowered by failed player defend!", LogScope.Battle);

            // Titles: attacker hit hooks for wild
            if (!string.IsNullOrEmpty(_wildCombatIdForTitles))
                TitlesAdapter.OnAttackLanded(_wildCombatIdForTitles, dr.crit);


            if (wildChargedNextAttack && chargeBonusPct > 0f)
            {
                dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + chargeBonusPct)));
                wildChargedNextAttack = false;

                if (!ShouldSkipNarration(BattleLineTag.Flavor))
                    yield return Say($"{attackerName} unleashes a charged attack (+{Mathf.RoundToInt(chargeBonusPct * 100f)}% dmg)!", BattleLineTag.Flavor);
            }

// Status: Rally (Clash) — allies gain minor Attack boost.
float wildRallyBonusPct = GetWildRallyBonusPct();
if (wildRallyBonusPct > 0f)
{
    int before = dr.damage;
    dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + wildRallyBonusPct)));
    if (BattleLogger.Enabled)
        BattleLogger.Log($"[Status] Rally boosts Wild damage: {before}→{dr.damage} (+{Mathf.RoundToInt(wildRallyBonusPct * 100f)}%).", LogScope.Battle);
}


// Status: Tailwind (Sky) — first attack during effect deals bonus damage (consumed on use).
float wildTailwindBonusPct = GetWildTailwindBonusPct();
bool wildTailwindConsumed = false;
if (wildTailwindBonusPct > 0f)
{
    int before = dr.damage;
    dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + wildTailwindBonusPct)));
    wildTailwindConsumed = true;
    if (BattleLogger.Enabled)
        BattleLogger.Log($"[Status] Tailwind empowers Wild's first strike: {before}→{dr.damage} (+{Mathf.RoundToInt(wildTailwindBonusPct * 100f)}%).", LogScope.Battle);
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

        
        // Titles: defender-side type resist + damage filter (post-DEF modifiers, before shields)
        float title_scalarBeforeDefTitles = incomingScalar;

        float title_typeResistMul = 1f;
        float title_dmgFilterPct = 0f;
        int title_dmgFilterFlat = 0;

        try
        {
            var incomingType = (wildDef != null) ? wildDef.type : MonsterType.None;
            title_typeResistMul = TitlesAdapter.GetIncomingEffectivenessMult(teamTitleIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex], incomingType);
        }
        catch { title_typeResistMul = 1f; }

        try
        {
            // NOTE: avoid shadowing the earlier 'df' (used for cannotBeCrit) in this method.
            var df2 = TitlesAdapter.GetDamageFilter(teamTitleIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);
            title_dmgFilterPct = Mathf.Clamp01(df2.percentReduce);
            title_dmgFilterFlat = Mathf.Max(0, df2.flatReduce);
        }
        catch { title_dmgFilterPct = 0f; title_dmgFilterFlat = 0; }

        float title_scalarAfterTypeResist = incomingScalar * Mathf.Max(0f, title_typeResistMul);
        float title_scalarAfterPct = title_scalarAfterTypeResist * Mathf.Max(0f, 1f - title_dmgFilterPct);

        // Precompute reductions for quick UX + mathy logger
        int title_dmg_pre = Mathf.Max(1, Mathf.RoundToInt(dr.damage * title_scalarBeforeDefTitles));
        int title_dmg_afterTypeResist = Mathf.Max(1, Mathf.RoundToInt(dr.damage * title_scalarAfterTypeResist));
        int title_dmg_afterPct = Mathf.Max(1, Mathf.RoundToInt(dr.damage * title_scalarAfterPct));

        int title_reducedByTypeResist = Mathf.Max(0, title_dmg_pre - title_dmg_afterTypeResist);
        int title_reducedByDmgFilterPct = Mathf.Max(0, title_dmg_afterTypeResist - title_dmg_afterPct);
        int title_reducedByDmgFilterFlat = 0;

        incomingScalar = title_scalarAfterPct;

        // Booster: Type Resist (turn-based) reduces incoming super-effective damage.
        // Log only when it actually mitigates damage.
        if (_rules.allowBoosters && BattleBoosterController.I != null)
        {
            var boosterCtrl = BattleBoosterController.I;
            if (boosterCtrl.IsBoosterActive(BoosterType.TypeResist) && dr.effectiveness > 1f)
            {
                int beforeBoosterResist = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

                float resistMul = Mathf.Clamp01(boosterCtrl.GetResistMul());
                incomingScalar *= resistMul;

                int afterBoosterResist = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));
                if (afterBoosterResist < beforeBoosterResist)
                {
                    int reducedBy = beforeBoosterResist - afterBoosterResist;
                    BattleLogger.Log($"Type Resist triggered: incoming super-effective damage reduced by {reducedBy} ({beforeBoosterResist}→{afterBoosterResist}).", LogScope.Battle);
                }
            }
        }

        // Team aura damage reduction
        float title_auraDmgReduce = 0f;
        try
        {
            var auraCtx = BuildTitleContextForIndexSafe(activeIndex);
            title_auraDmgReduce = TitlesAdapter.GetTeamAuraDamageReduction(
                GetTeamTitleIdSafe(activeIndex), in auraCtx);
            if (title_auraDmgReduce > 0f)
                incomingScalar *= 1f - Mathf.Clamp01(title_auraDmgReduce);
        }
        catch { title_auraDmgReduce = 0f; }

int dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        if (title_dmgFilterFlat > 0)
        {
            int beforeFlat = dmg_afterScalar;
            dmg_afterScalar = Mathf.Max(1, dmg_afterScalar - title_dmgFilterFlat);
            title_reducedByDmgFilterFlat = Mathf.Max(0, beforeFlat - dmg_afterScalar);
        }

        if (BattleLogger.Enabled && (title_reducedByTypeResist > 0 || title_reducedByDmgFilterPct > 0 || title_reducedByDmgFilterFlat > 0))
        {
            int titleTotalReduced = title_reducedByTypeResist + title_reducedByDmgFilterPct + title_reducedByDmgFilterFlat;
            BattleLogger.Log($"Title defenses triggered: incoming damage reduced by {titleTotalReduced} ({title_dmg_pre}→{dmg_afterScalar}). [eff x{title_typeResistMul:0.##}:-{title_reducedByTypeResist}, pct:-{title_reducedByDmgFilterPct}, flat:-{title_reducedByDmgFilterFlat}]", LogScope.Battle);
        }


        // Title battle-start shield (separate pool, consumed before normal shields)
        float titleShieldBefore = (titleShieldHP != null && activeIndex >= 0 && activeIndex < titleShieldHP.Length) ? titleShieldHP[activeIndex] : 0f;
        float titleShieldAbsorbF = 0f;

        int dmg_incoming = dmg_afterScalar;
        bool shadowVeilBlockedPlayer = false;

        int dmg_final = dmg_incoming;
        // ShadowVeil: immune to damage for the duration.
        if (dmg_final > 0 && IsActivePlayerShadowVeiled())
        {
            shadowVeilBlockedPlayer = true;
            BattleLogger.Log($"{GetName(activeIndex)} is shrouded - damage nullified!", LogScope.Battle);
            BattleLogger.AddKeyMoment($"Shadow Veil: {GetName(activeIndex)} ignored damage.");
            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"{GetName(activeIndex)} fades into a Shadow Veil—no damage!", BattleLineTag.Flavor);

            dmg_final = 0;
            ClearTeamStatus(activeIndex, reason: "absorbed a hit");
        }

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

            // If this shield came from the Shielded status, track remaining so cleanup only removes the unspent portion.
            if (_shieldedGrantTeam != null && activeIndex >= 0 && activeIndex < _shieldedGrantTeam.Length)
                _shieldedGrantTeam[activeIndex] = Mathf.Max(0f, _shieldedGrantTeam[activeIndex] - shieldAbsorbF);

            dmg_final = Mathf.Max(0, dmg_final - Mathf.RoundToInt(shieldAbsorbF));

            if (shieldAbsorbF > 0f)
                if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                    yield return Say($"{GetName(activeIndex)}'s shield absorbed {Mathf.RoundToInt(shieldAbsorbF)}!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        // ── Shield-break detection: if total shield went from >0 to 0, fire title hook ──
        if ((titleShieldBefore + shieldBefore) > 0f)
        {
            float totalShieldAfter = ((titleShieldHP != null && activeIndex >= 0 && activeIndex < titleShieldHP.Length) ? titleShieldHP[activeIndex] : 0f)
                                   + ((shieldHP != null && activeIndex >= 0 && activeIndex < shieldHP.Length) ? shieldHP[activeIndex] : 0f);
            if (totalShieldAfter <= 0f)
            {
                try { TitlesAdapter.OnPlayerShieldBroke(GetTeamTitleIdSafe(activeIndex)); }
                catch (System.Exception ex) { Debug.LogException(ex); }
            }
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
            guardConvertPct > 0f &&
            dr.crit && !df.cannotBeCrit)
        {
            float shieldGain = preventedByGuardRaw * guardConvertPct;
            pendingGuardShield[activeIndex] += shieldGain;
            if (!ShouldSkipNarration(BattleLineTag.Shield | BattleLineTag.Flavor))
                yield return Say($"{GetName(activeIndex)} stores {Mathf.RoundToInt(shieldGain)} damage as a guard shield for the next round (critical hit blocked)!", BattleLineTag.Shield | BattleLineTag.Flavor);
        }

        TitlesAdapter.OnHitTaken(teamTitleIds[activeIndex], dmg_incoming, dr.crit && !df.cannotBeCrit);

        if (shadowVeilBlockedPlayer)
        {
            yield return Say($"{GetName(activeIndex)} takes no damage!", BattleLineTag.Result);
        }
        else
        {
            yield return Say($"{attackerName} hits {GetName(activeIndex)} for {dmg_final}!", BattleLineTag.Result);
        }



        // Status: Tailwind is consumed on the first attack it empowers.
        if (wildTailwindConsumed && wildStatus == StatusType.Tailwind)
            ClearWildStatus(reason: "spent");

        // Status: Leeching (Bug) — heal a portion of damage dealt.
        ApplyLeechHeal(BattleSide.Wild, dmg_final);

        // Status: Phantasmal (Specter) — lose HP when attacking.
        float wildPhantasmalPct = GetWildPhantasmalSelfDmgPct();
        if (wildPhantasmalPct > 0f)
        {
            int selfDmg = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1f, wildMaxHP) * Mathf.Clamp(wildPhantasmalPct, 0f, 0.9f)));
            float pre = wildHP;
            wildHP = Mathf.Max(0f, wildHP - selfDmg);
            PushHPBars();

            if (BattleLogger.Enabled)
                BattleLogger.Log($"[Status] Wild loses {selfDmg} HP from Phantasmal backlash. ({Mathf.CeilToInt(pre)}→{Mathf.CeilToInt(wildHP)})", LogScope.Battle);

            if (!ShouldSkipNarration(BattleLineTag.Flavor))
                yield return Say($"Wild is hurt by Phantasmal backlash (-{selfDmg})!", BattleLineTag.Flavor);
        }

        // Status: Foresight — check repeat action to schedule a stun next turn.
        NotifyWildActionResolved_ForForesight(EnemyAction.Attack);
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

        
        // Titles: quick UX callouts (mathy details stay in BattleLogger)
        if (title_reducedByTypeResist > 0 && !ShouldSkipNarration(BattleLineTag.NotEffective))
            yield return Say("Resisted!", BattleLineTag.NotEffective);

        if ((title_reducedByDmgFilterPct + title_reducedByDmgFilterFlat) > 0 && !ShouldSkipNarration(BattleLineTag.Shield))
            yield return Say("Reduced!", BattleLineTag.Shield);

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
                if (feedback)
                    feedback.PlayClutchMoment(BattleFeedbackManager.BattleFeedbackSide.Player);
            }
        }

        }

        finally
        {
            // Tick owner-turn-based Title durations for the wild side.
            if (inBattle && !string.IsNullOrEmpty(_wildTurnOwnerId))
            {
                TitlesAdapter.OnCombatantTurnEnded(_wildTurnOwnerId);
                RequestBattleStatRebuild(BattleStatRebuildReason.HPChanged);
            }
        }

        yield break;

    }




    private bool CheckEnd()
    {
        if (IsWildKO())
        {
            BattleLogger.Log("Wild monster fainted!", LogScope.Battle);
            AudioManager.I?.PlaySfx(SfxType.KO);
            Emit(BattleEvent.KO(BattleSide.Wild));
            if (!HasBattleEventConsumers && feedback) feedback.PlayKO(BattleFeedbackManager.BattleFeedbackSide.Wild);

            // OnEventTriggerTitleSO: notify titles the player scored a kill
            try
            {
                string killerId = GetTeamTitleIdSafe(activeIndex);
                if (!string.IsNullOrEmpty(killerId))
                    TitlesAdapter.OnKill(killerId);
            }
            catch (System.Exception ex) { UnityEngine.Debug.LogException(ex); }

EndBattleRouted(true);
            return true;
        }
        if (IsTeamKO())
        {
            BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
            AudioManager.I?.PlaySfx(SfxType.KO);
            Emit(BattleEvent.KO(BattleSide.Player));
            if (!HasBattleEventConsumers && feedback) feedback.PlayKO(BattleFeedbackManager.BattleFeedbackSide.Player);
EndBattleRouted(false);
            return true;
        }
        return false;
    }


    public void ForceEndBattleEarly(bool victory, bool escaped = false)
    {
        inBattle = false;
        BattleCalc.ResetRng();
        _rng.ClearAll();
        ConfigureForAuto(false);
        SetIsPlayerTurn(false);
        GameEvents.OnBattleStateChanged?.Invoke();
        pendingAction = PlayerAction.None;
        defendActiveThisRound = false;
        wildDefendActiveThisRound = false;
        wildFocusConsecutiveUses = 0;
        wildFocusCurrentSuccess = focusFirstUseSuccess;
        wildChargedNextAttack = false;
        ResetStatusIcons();

        if (turnCR != null)
        {
            StopCoroutine(turnCR);
            turnCR = null;
        }

        if (benchBtn1) benchBtn1.interactable = false;
        if (benchBtn2) benchBtn2.interactable = false;

        BattleTempBuffs.I?.ClearAll();

        var result = new BattleResult
        {
            victory = victory,
            escaped = escaped,
            creditsGained = 0,
            creditsBase = 0,
            creditsTitleBonus = 0,
            activeMonsterOwnedId = null,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = 0f,
            critCount = 0,
            turnsSurvived = 0,
            damageTaken = 0,
            damageDealt = 0,
            gotFirstHit = false,
            statusesAppliedToWild = 0,
            hadTypeAdvantage = false,
            hadTypeDisadvantage = false,
            isSoloBattle = false,
            wasManualBattle = false
        };

        onEnd?.Invoke(result);

        if (!IronCareerRuntime.IsActive)
            GameEvents.BattleFinished?.Invoke(result);
    }

    private bool ShouldSkipNarration(BattleLineTag tags)
    {
        // Never suppress lines that carry an icon tag — they must reach the UI.
        const BattleLineTag iconMask = BattleLineTag.Crit | BattleLineTag.Shield
                                     | BattleLineTag.SuperEffective | BattleLineTag.NotEffective;
        if ((tags & iconMask) != 0)
            return false;

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