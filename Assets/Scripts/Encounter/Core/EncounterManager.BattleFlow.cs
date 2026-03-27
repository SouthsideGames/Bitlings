using UnityEngine;
using System;
using System.Collections;

// ─────────────────────────────────────────────────────────────
// EncounterManager.BattleFlow
// Encounter start validation, battle bootstrapping, result flow, auto toggle.
// ─────────────────────────────────────────────────────────────

public partial class EncounterManager
{
    private const string TutorialBattleKey = "tut_battle_v1";
    private const string TutorialBattleAdvancedKey = "tut_battle_advanced_v1";

    // ============================ PUBLIC API (UI) ===============================

    public void RequestEncounterTap()
    {
        if (IronCareerRuntime.IsActive)
        {
            DevLog.Log("[EncounterManager] Ignoring encounter tap while Iron is active.");
            return;
        }

        if (inBattle) return;
        StartEncounter(spendEnergy: true);
    }

    // ============================= ENCOUNTER FLOW ===============================

    private bool TryValidateEncounterStart(bool spendEnergy)
    {
        if (WorldEventSystem.I != null && WorldEventSystem.I.AreEncountersDisabled())
        {
            EmitStatus("Encounters are temporarily suspended.", LogScope.System);
            StopAuto_NoEnergy();
            return false;
        }

        if (inBattle)
        {
            EmitStatus("Already in battle.", LogScope.System);
            return false;
        }

        var preData = SaveManager.Data;
        if (preData == null || preData.team == null || preData.team.Count == 0)
        {
            EmitStatus("No team yet. Catch something to begin!", LogScope.System);
            StopAuto_NoEnergy();
            return false;
        }

        if (!EligibilityRules.HasMinimumAliveTeam(minMembers: 1))
        {
            EmitStatus("All team members are down. Heal up first.", LogScope.System);
            StopAuto_NoEnergy();
            return false;
        }

        if (spendEnergy && !EligibilityRules.HasRequiredEnergyOrFree(out int needed, out int current))
        {
            StopAuto_NoEnergy();
            EmitStatus($"Need {needed} energy (have {current}).", LogScope.System);
            return false;
        }

        var data = SaveManager.Data;
        if (data == null || data.team == null || data.team.Count == 0)
        {
            EmitStatus("No team yet. Catch something to begin!", LogScope.System);
            StopAuto_NoEnergy();
            return false;
        }

        if (!HasHealthyMonsters())
        {
            EmitStatus("All team members are down. Heal up first.", LogScope.System);
            StopAuto_NoEnergy();
            return false;
        }

        if (spendEnergy && !SpendEnergy())
        {
            StopAuto_NoEnergy();
            EmitStatus("Out of energy!", LogScope.System);
            return false;
        }

        return true;
    }

    private int CalculateAverageTeamLevel()
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null || data.team.Count == 0)
            return 1;

        int sum = 0;
        for (int i = 0; i < data.team.Count; i++)
        {
            var m = data.team[i];
            if (m != null) sum += m.level;
        }

        return Mathf.Max(1, Mathf.RoundToInt((float)sum / data.team.Count));
    }

    private int GetDifficultyMode()
    {
        if (SaveManager.Data == null || SaveManager.Data.promotionRank < 15)
            return 0;

        var settings = SaveManager.Data.settings;
        return settings != null ? Mathf.Clamp(settings.difficultyMode, 0, 2) : 0;
    }

    private int CalculateWildLevel(bool isBoss)
    {
        int wildLevel = Mathf.Clamp(CalculateAverageTeamLevel() + UnityEngine.Random.Range(-1, 2), 1, 99);
        if (isBoss)
            wildLevel = Mathf.Max(1, wildLevel + bossLevelBonus);

        int difficultyMode = GetDifficultyMode();
        if (difficultyMode == 1) wildLevel += 2;
        else if (difficultyMode == 2) wildLevel += 5;

        return Mathf.Clamp(wildLevel, 1, 99);
    }

    private void EnsureNonIronHudBindings()
    {
        if (IronCareerRuntime.IsActive)
            return;

        var ironHud = FindFirstObjectByType<IronBattleUIRoot>(FindObjectsInactive.Include);
        if (ironHud != null)
        {
            ironHud.RestoreBattleManagerDefaults();
            return;
        }

        battleManager.ClearUIBindingsOverride();
        battleManager.ClearUIOverride();
    }

    void StartEncounter(bool spendEnergy)
    {
        if (IronCareerRuntime.IsActive)
        {
            Debug.LogWarning("[EncounterManager] StartEncounter blocked during Iron.");
            return;
        }

        // Bundle A: Busy gate to prevent rapid-tap double starts.
        // Time-based so it auto-expires even if an early return happens.
        if (!BusyLock.TryEnter("EncounterStart", 0.35f))
            return;

        ClearWildTitleInjection();

        if (!TryValidateEncounterStart(spendEnergy))
            return;

        var data = SaveManager.Data;

        int cadence = (bossEveryNOverride > 0)
            ? bossEveryNOverride
            : (data != null && data.bossEveryN > 0 ? data.bossEveryN : 10);

        // World Events: High Alert can increase boss frequency by reducing cadence.
        if (WorldEventSystem.I != null)
        {
            float mul = WorldEventSystem.I.GetBossCadenceMultiplier();
            cadence = Mathf.Max(1, Mathf.RoundToInt(cadence * mul));
        }

        _currentEncounterIsBoss = ShouldSpawnBoss(
            data != null ? data.encountersSinceBoss : 0,
            cadence
        );
        _currentBossUsed = null;

        MonsterDataSO wild = null;

        if (_currentEncounterIsBoss)
        {
            var lib = MonsterLibraryLocator.Lib;
            _currentBossUsed = PickBossWeighted(lib, data != null ? data.lastBossId : null);

            if (_currentBossUsed != null)
                wild = _currentBossUsed;
            else
                _currentEncounterIsBoss = false;
        }

        if (wild == null)
            wild = PickWildConsideringFlyers();

        if (wild == null)
        {
            EmitStatus("No monsters available.", LogScope.System);
            return;
        }

        FieldOpsTracker.RecordEncounter(wild);
        NotifyAuto_SpecialSpawn(wild);

        int wildLevel = CalculateWildLevel(_currentEncounterIsBoss);

        ResolveWildTitles(wild, wildLevel);

        _currentWildIsShiny = RollWildShiny(wild);
        string wildName = MonsterNameFormatter.Format(wild, _currentWildIsShiny);

        EncounterPanelUI.I?.OnWildSpawned(wild);

        PlayEncounterSfx(wild);

        var p = (data != null && data.team != null && data.team.Count > 0) ? data.team[0] : null;
        string titleSuffix = string.IsNullOrEmpty(WildTitleLabel) ? "" : $" — {WildTitleLabel}";

        if (_currentEncounterIsBoss)
            EmitStatus($"⚠️ BOSS ENCOUNTER! {wildName} (Lv {wildLevel}){titleSuffix} appears.{(p != null && p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");
        else
            EmitStatus($"Encounter! A wild {wildName} (Lv {wildLevel}){titleSuffix} appears.{(p != null && p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");

        BattleLogger.BeginEncounter(_currentEncounterIsBoss
            ? $"BOSS: {wildName} Lv{wildLevel}{titleSuffix}"
            : $"{wildName} Lv{wildLevel}{titleSuffix}");

        if (_currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossSpawned?.Invoke(_currentBossUsed.id, _currentBossUsed);

        _autoResolveSnapshot = autoMode;

        inBattle = true;
        OnStateChanged?.Invoke();

        if (!battleManager)
        {
            EmitStatus("No BattleManager assigned.", LogScope.System);
            inBattle = false;
            OnStateChanged?.Invoke();
            ClearWildTitleInjection();
            return;
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();

        _manualHirePending = false;

        battleManager.ConfigureForAuto(_autoResolveSnapshot);

        // Safety: if we are NOT in Iron Career, make sure any Iron HUD override is cleared
        // so regular battles always bind to the regular (yellow) HUD.
        EnsureNonIronHudBindings();

        // Deterministic battle RNG: derive a per-battle seed from the active global seed
        // (daily/custom/session) + encounter serial + wild identifiers.
        // This makes battles reproducible for debugging and daily runs.
        int battleSeed = BuildBattleSeed(wild, wildLevel, _currentEncounterIsBoss);
        string seedLabel = $"{SeedService.GetDisplaySeedPrefix()}{SeedService.GetDisplaySeedToken()}";
        battleManager.SetBattleSeed(battleSeed, seedLabel);
        TryQueueBattleTutorial();
        battleManager.Begin(wild, wildLevel, OnBattleEnded);
    }

    private void TryQueueBattleTutorial()
    {
        var data = SaveManager.Data;
        if (data == null) return;

        if (!SaveManager.IsTutorialComplete(TutorialBattleKey))
        {
            TutorialOverlayPanel.RequestOpen(TutorialBattleKey);
            return;
        }

        if (data.HasSynergyUnlocked && !SaveManager.IsTutorialComplete(TutorialBattleAdvancedKey))
            TutorialOverlayPanel.RequestOpen(TutorialBattleAdvancedKey);
    }

    private int BuildBattleSeed(MonsterDataSO wild, int level, bool isBoss)
    {
        SeedService.ApplyGlobalSeedForSession();
        int baseSeed = SeedService.ActiveSeed != 0 ? SeedService.ActiveSeed : 1;

        string wildId = (wild != null && !string.IsNullOrEmpty(wild.id)) ? wild.id : "UNKNOWN";
        string raw = $"{baseSeed}|{_wildEncounterSerial}|{(isBoss ? 1 : 0)}|{wildId}|{Mathf.Max(1, level)}";
        int h = StableHash(raw);
        if (h == 0) h = 1;
        return h;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = 17;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                    hash = hash * 31 + s[i];
            }
            return hash;
        }
    }

    void OnBattleEnded(BattleResult result)
    {
        _lastBattleResult = result;

        DevLog.Log($"[EncounterManager] OnBattleEnded incoming result: base={result.creditsBase}, bonus={result.creditsTitleBonus}, totalPreScale={result.creditsGained}, active={result.activeMonsterOwnedId}");

        // Reset encounter-spawn presentation state.
        _lastWildWasShiny = _currentWildIsShiny;
        _currentWildIsShiny = false;

        ClearWildTitleInjection();

        bool escaped = result.escaped;
        bool victory = result.victory;
        bool defeat = !victory && !escaped;

        if (AudioManager.I)
        {
            if (victory) AudioManager.I?.PlaySfx(SfxType.Victory);
            else if (defeat) AudioManager.I?.PlaySfx(SfxType.Defeat);
        }

        int finalcredits = 0;
        int creditTitleBonus = 0;
        if (!escaped)
        {
            finalcredits = ApplyCreditsGainedMultiplier(result.creditsGained);
            finalcredits = Mathf.Max(0, finalcredits);

            // Apply title-based credit multiplier (if any) and compute the explicit title bonus
            try
            {
                // Use the actual active monster from the battle, not just team[0]
                string leadId = !string.IsNullOrEmpty(result.activeMonsterOwnedId) ? result.activeMonsterOwnedId : null;

                if (!string.IsNullOrEmpty(leadId))
                {
                    float cm = TitlesAdapter.GetCreditMultOnVictory(leadId, result.wildDef, result.wildLevel);
                    DevLog.Log($"[EncounterManager] Title credit mult for {leadId}: {cm}");
                    if (cm > 0f && cm != 1f)
                    {
                        int withTitles = Mathf.Max(0, Mathf.RoundToInt(finalcredits * cm));
                        creditTitleBonus = Mathf.Max(0, withTitles - finalcredits);
                        finalcredits = withTitles;
                        DevLog.Log($"[EncounterManager] Applied title bonus: base={finalcredits - creditTitleBonus}, bonus={creditTitleBonus}, total={finalcredits}");
                    }
                }
            }
            catch (Exception)
            {
                // Safe no-op if TitlesAdapter or Save data is unavailable at runtime
            }

            if (finalcredits > 0)
            {
                if (ResourceManager.I != null)
                {
                    ResourceManager.I?.Add(ResourceType.Credits, finalcredits);
                }
                else
                {
                    ResourceBank.Add(ResourceType.Credits, finalcredits);
                    GameEvents.OnResourcesChanged?.Invoke();
                    GameEvents.ResourceAdded?.Invoke(ResourceType.Credits, finalcredits);
                }
            }
        }

        if (victory) EmitStatus($"Victory! +{finalcredits} credits");
        else if (defeat) EmitStatus("Defeat.");
        else if (escaped) EmitStatus("The wild Bitling fled.");

        if (ExchangeManager.I != null && result.wildDef != null && !string.IsNullOrEmpty(result.wildDef.id))
            ExchangeManager.I.RecordBattleOutcome(result.wildDef.id, victory, defeat, escaped);

        if (victory && _currentEncounterIsBoss && _currentBossUsed != null)
        {
            GameEvents.BossDefeated?.Invoke(_currentBossUsed.id);
            FieldOpsTracker.RecordRiftStabilization(_currentBossUsed);
        }

        if (SaveManager.Data != null)
        {
            AfterBattleCadenceUpdate(
                ref SaveManager.Data.encountersSinceBoss,
                _currentEncounterIsBoss,
                _currentBossUsed,
                ref SaveManager.Data.lastBossId
            );
        }

        if (victory && autoMode)
        {
            if (_currentEncounterIsBoss || (result.wildDef != null && result.wildDef.uncatchable))
            {
                EmitStatus(AppendLine(GetLastStatus(), "(This Bitling can’t be captured.)"));
            }
            else
            {
                TryCatch(result.wildDef, result.wildLevel);
            }
        }

        if (victory) SetWinStreak(_currentWinStreak + 1);
        else if (defeat) SetWinStreak(0);

        ReconcileHPWithCurrentWinStreak();
        OnStateChanged?.Invoke();

        if (_autoResolveSnapshot
            && SaveManager.Data != null
            && FeatureUnlockManager.I != null
            && FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_LogArchive))
        {
            string opponentId = null;
            int opponentLevel = result.wildLevel;
            if (result.wildDef != null) opponentId = result.wildDef.id;

            AutoBattleLogArchive.AddEntry(
                SaveManager.Data,
                opponentId,
                opponentLevel,
                victory,
                escaped,
                BattleLogger.GetLinesSnapshot()
            );
        }

        SaveManager.Save();

        var finished = result;
        finished.creditsGained = finalcredits;

        GameEvents.BattleFinished?.Invoke(finished);
        BattleLogger.EndEncounter(victory);

        bool holdForHireDecision =
            victory &&
            !escaped &&
            !autoMode &&
            !_currentEncounterIsBoss &&
            finished.wildDef != null &&
            !finished.wildDef.uncatchable &&
            EncounterPanelUI.I != null;

        _manualHirePending = holdForHireDecision;

        if (holdForHireDecision)
            PostBattleSummaryManager.I?.SetAutoBattling(true);
        else
            PostBattleSummaryManager.I?.SetAutoBattling(_autoResolveSnapshot);

        int displayCreditsBase = Mathf.Max(0, finalcredits - creditTitleBonus);
        DevLog.Log($"[PostBattleSummary] Passing credits: base={displayCreditsBase}, bonus={creditTitleBonus}");
        PostBattleSummaryManager.I?.NotifyBattleEnd(
            finished,
            isAuto: _autoResolveSnapshot,
            growthCoresGained: Mathf.Max(0, result.growthCoresGained),
            monstersLeveledUp: 0,
            captured: false,
            capturedMonsterId: null,
            capturedLevel: 0,
            capturedShiny: false,
            wildWasShiny: _lastWildWasShiny,
            levelUpSummaries: null,
            creditsBase: displayCreditsBase,
            creditsTitleBonus: creditTitleBonus,
            growthCoresBase: result.growthCoresBase,
            growthCoresTitleBonus: result.growthCoresTitleBonus,
            growthCoresDetailLines: null
        );

        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        postResultCo = StartCoroutine(PostResultFlow(victory, escaped));
    }

    private int ApplyCreditsGainedMultiplier(int basecredits)
    {
        if (basecredits <= 0) return 0;
        float mult = 1f;
        if (GameBalance.TryGet(out var bal))
            mult = Mathf.Max(0f, bal.creditGainMultiplier);

        return Mathf.Max(0, Mathf.FloorToInt(basecredits * mult));
    }

    IEnumerator PostResultFlow(bool victory, bool escaped)
    {
        yield return new WaitForSeconds(postResultDelay);
        inBattle = false;
        // IMPORTANT: many UI elements (including Encounter button interactivity)
        // depend on this event to refresh when a battle ends.
        OnStateChanged?.Invoke();

        if (escaped)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;
            OnStateChanged?.Invoke();

            if (autoMode)
            {
                if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }

                EmitStatus("The wild Bitling fled. Starting next encounter (AUTO)…", LogScope.System);
                StartEncounter(false);
            }
            else
            {
                EmitStatus("The wild Bitling fled. Showing summary…", LogScope.System);
                PostBattleSummaryManager.I?.SetAutoBattling(false);
                PostBattleSummaryManager.I?.FlushNowIfPossible();
            }
            yield break;
        }

        if (!victory)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;
            OnStateChanged?.Invoke();

            if (autoMode)
            {
                EmitStatus("Defeat. Retrying (AUTO)…", LogScope.System);
                yield break;
            }

            EmitStatus("Battle finished. Showing summary…", LogScope.System);
            PostBattleSummaryManager.I?.SetAutoBattling(false);
            PostBattleSummaryManager.I?.FlushNowIfPossible();
            yield break;
        }

        if (autoMode)
        {
            if (!autoRunPaidEnergy)
            {
                if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }
                autoRunPaidEnergy = true;
            }
            StartEncounter(false);
            yield break;
        }

        nextEncounterFree = true;
        OnStateChanged?.Invoke();

        bool canAskHire =
            !_currentEncounterIsBoss &&
            _lastBattleResult.wildDef != null &&
            !_lastBattleResult.wildDef.uncatchable &&
            EncounterPanelUI.I != null;

        if (canAskHire)
        {
            EmitStatus("Victory. Hire decision…", LogScope.System);

            PostBattleSummaryManager.I?.SetAutoBattling(true);

            EncounterPanelUI.I?.ShowHireDecision(_lastBattleResult.wildDef, _lastBattleResult.wildLevel, isShiny: _lastWildWasShiny);
            yield break;
        }

        EmitStatus("Battle finished. Showing summary…", LogScope.System);
        PostBattleSummaryManager.I?.SetAutoBattling(false);
        PostBattleSummaryManager.I?.FlushNowIfPossible();
    }

    public void OnHireDecisionResolved(bool hiredYes, bool captureSucceeded)
    {
        if (!_manualHirePending)
        {
            PostBattleSummaryManager.I?.SetAutoBattling(false);
            PostBattleSummaryManager.I?.FlushNowIfPossible();
            return;
        }

        _manualHirePending = false;

        if (hiredYes && captureSucceeded && _lastBattleResult.wildDef != null)
        {
            PostBattleSummaryManager.I?.TryUpdateLatestQueuedCapture(
                true,
                _lastBattleResult.wildDef.id,
                _lastBattleResult.wildLevel,
                capturedShiny: _lastWildWasShiny
            );
        }
        else
        {
            PostBattleSummaryManager.I?.TryUpdateLatestQueuedCapture(false, null, 0);
        }

        PostBattleSummaryManager.I?.SetAutoBattling(false);
        PostBattleSummaryManager.I?.FlushNowIfPossible();
    }

    public void ToggleAutoMode()
    {
        autoMode = !autoMode;

        if (autoMode)
        {
            if (!inBattle)
            {
                if (!HasEnergy())
                {
                    EmitStatus("Out of energy!", LogScope.System);
                    autoMode = false;
                    GameEvents.RaiseAutoBattleModeChanged(autoMode);
                    return;
                }

                // Start an encounter immediately in auto-mode and spend energy
                StartEncounter(spendEnergy: true);
            }
            else
            {
                EmitStatus("AUTO mode ON. Will continue after this battle…", LogScope.System);
            }
        }
        else
        {
            EmitStatus("AUTO mode OFF. Tap ENCOUNTER for the next fight.", LogScope.System);
        }

        GameEvents.RaiseAutoBattleModeChanged(autoMode);
    }

    private void PlayEncounterSfx(MonsterDataSO wild)
    {
        if (AudioManager.I == null || wild == null)
            return;

        if (_currentEncounterIsBoss)
        {
            AudioManager.I?.PlaySfx(SfxType.BossEncounter);
            return;
        }

        if (_currentWildIsShiny || IsShinyMonster(wild))
        {
            AudioManager.I?.PlaySfx(SfxType.ShinyEncounter);
            return;
        }

        if (IsUniqueMonster(wild))
        {
            AudioManager.I?.PlaySfx(SfxType.UniqueEncounter);
            return;
        }
    }
}
