using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// BattleManager.Ending
// Battle termination flow, rewards persistence, and Iron carry-over.
// ─────────────────────────────────────────────────────────────

public partial class BattleManager : MonoBehaviour
{
    private void EndBattleRouted(bool victory, bool escaped = false)
    {
        if (IronCareerRuntime.IsActive)
            EndBattle_Iron(victory, escaped);
        else
            EndBattle(victory, escaped);
    }

    private void EndBattle_Iron(bool victory, bool escaped = false)
    {
        if (!inBattle) return;

        inBattle = false;
        SetIsPlayerTurn(false);

        ConfigureForAuto(false);

        if (benchBtn1) benchBtn1.interactable = false;
        if (benchBtn2) benchBtn2.interactable = false;

        pendingAction = PlayerAction.None;
        defendActiveThisRound = false;
        wildDefendActiveThisRound = false;
        wildChargedNextAttack = false;
        ResetStatusIcons();

        if (turnCR != null) { StopCoroutine(turnCR); turnCR = null; }

        BattleCalc.ResetRng();
        _rng.ClearAll();
        float survived = Mathf.Max(0f, Time.unscaledTime - startTime);

        int basecredits = 0;
        int finalcredits = 0;
        int creditTitleBonus = 0;

        if (!escaped)
        {
            basecredits = BattleRewards.creditsFor(victory, wildLevel, survived);
            finalcredits = basecredits;

            if (victory && teamTitleIds != null && activeIndex >= 0 && activeIndex < teamTitleIds.Length)
            {
                float cm = _cachedCreditMult;
                if (cm > 0f)
                {
                    finalcredits = Mathf.Max(0, Mathf.RoundToInt(basecredits * cm));
                    creditTitleBonus = Mathf.Max(0, finalcredits - basecredits);
                }
            }

            if (finalcredits < 0) finalcredits = 0;
        }

        int growthCoreBaseAfterShiny = 0;
        int growthCoreTitleBonus = 0;
        int growthCoreTotal = 0;

        if (victory && !escaped)
        {
            int baseCores = Mathf.Max(1, 2 + wildLevel);

            var m = (teamOwnedEffective != null && activeIndex >= 0 && activeIndex < teamOwnedEffective.Length)
                ? teamOwnedEffective[activeIndex]
                : default;

            float shinyMul = ShinySystems.TrainingXpMult(m);
            int baseAfterShiny = Mathf.RoundToInt(baseCores * shinyMul);
            growthCoreBaseAfterShiny = Mathf.Max(0, baseAfterShiny);

            float titleCoreMul = 1f;
            if (teamTitleIds != null && activeIndex >= 0 && activeIndex < teamTitleIds.Length)
                titleCoreMul = Mathf.Max(0f, TitlesAdapter.GetGrowthCoreMultOnVictory(teamTitleIds[activeIndex], wildDef, wildLevel));

            int growthCoreAfterTitles = Mathf.Max(0, Mathf.RoundToInt(baseAfterShiny * titleCoreMul));

            float globalMul = 1f;
            if (GameBalance.TryGet(out var bal))
                globalMul = Mathf.Max(0f, bal.xpGainMultiplier);

            growthCoreTotal = Mathf.Max(0, Mathf.RoundToInt(growthCoreAfterTitles * globalMul));
            growthCoreTitleBonus = Mathf.Max(0, growthCoreAfterTitles - growthCoreBaseAfterShiny);
        }

        try
        {
            if (teamTitleIds != null && activeIndex >= 0 && activeIndex < teamTitleIds.Length)
            {
                string tid = teamTitleIds[activeIndex];
                if (!string.IsNullOrEmpty(tid))
                    TitlesAdapter.OnBattleEnd(tid, victory, wildDef, wildLevel);
            }

            if (!string.IsNullOrEmpty(_wildCombatIdForTitles))
                TitlesAdapter.OnBattleEnd(_wildCombatIdForTitles, victory, wildDef, wildLevel);
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleEnd(Iron) exception: {ex.Message}", LogScope.Battle);
        }

        ExtractIronCarryFromPlayerField(out var carryStatus, out var carryShield);

        var snap = new IronBattleOutcome
        {
            victory = victory,
            escaped = escaped,
            wildEscaped = _wildEscapedThisBattle,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived,
            turnsSurvived = _turnIndex,
            critCount = _totalCritsThisBattle,
            damageTaken = _totalDamageTakenThisBattle,
            damageDealt = _totalDamageDealtThisBattle,
            creditsGained = finalcredits,
            creditsBase = basecredits,
            creditsTitleBonus = creditTitleBonus,
            growthCoresGained = growthCoreTotal,
            growthCoresBase = growthCoreBaseAfterShiny,
            growthCoresTitleBonus = growthCoreTitleBonus,
            teamHP = (teamHP != null) ? (float[])teamHP.Clone() : null,
            teamMaxHP = (teamMaxHP != null) ? (float[])teamMaxHP.Clone() : null,
            shieldHP = carryShield,
            playerFieldStatus = carryStatus,
        };

        _battleContext?.OnBattleResolved(snap);

        var result = new BattleResult
        {
            victory = victory,
            escaped = escaped,
            creditsGained = finalcredits,
            creditsBase = basecredits,
            creditsTitleBonus = creditTitleBonus,
            creditsMultiplier = 1f,
            growthCoresGained = growthCoreTotal,
            growthCoresBase = growthCoreBaseAfterShiny,
            growthCoresTitleBonus = growthCoreTitleBonus,
            activeMonsterOwnedId = null,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived,
            critCount = _totalCritsThisBattle,
            turnsSurvived = _turnIndex,
            damageTaken = _totalDamageTakenThisBattle,
            damageDealt = _totalDamageDealtThisBattle,
            gotFirstHit = playerLandedFirstHitThisBattle
        };

        onEnd?.Invoke(result);
    }

    private IronFieldStatusSnapshot GetPlayerFieldStatusSnapshot()
    {
        var snap = new IronFieldStatusSnapshot
        {
            type = StatusType.None,
            turns = 0,
            magnitude = 0f,
            persistent = false
        };

        if (teamStatus == null || teamStatusTurns == null || teamStatusMagnitude == null || teamStatusPersistent == null)
            return snap;

        int bestIdx = -1;
        int bestTurns = -1;

        for (int i = 0; i < teamStatus.Length; i++)
        {
            var t = teamStatus[i];
            if (t == StatusType.None) continue;

            int turns = (i < teamStatusTurns.Length) ? teamStatusTurns[i] : 0;
            if (turns > bestTurns)
            {
                bestTurns = turns;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
        {
            snap.type = teamStatus[bestIdx];
            snap.turns = (bestIdx < teamStatusTurns.Length) ? teamStatusTurns[bestIdx] : 0;
            snap.magnitude = (bestIdx < teamStatusMagnitude.Length) ? teamStatusMagnitude[bestIdx] : 0f;
            snap.persistent = (bestIdx < teamStatusPersistent.Length) && teamStatusPersistent[bestIdx];
        }

        return snap;
    }

    public void ApplyIronCarryToPlayerField(IronFieldStatusSnapshot snap, float[] shieldBySlot)
    {
        if (!IronCareerRuntime.IsActive) return;

        if (teamStatus != null && teamStatusTurns != null && teamStatusMagnitude != null && teamStatusPersistent != null)
        {
            for (int i = 0; i < teamStatus.Length; i++)
            {
                if (teamDefs != null && i < teamDefs.Length && teamDefs[i] == null) continue;

                teamStatus[i] = snap.type;
                if (i < teamStatusTurns.Length) teamStatusTurns[i] = snap.turns;
                if (i < teamStatusMagnitude.Length) teamStatusMagnitude[i] = snap.magnitude;
                if (i < teamStatusPersistent.Length) teamStatusPersistent[i] = snap.persistent;
            }

            RefreshPrimaryStatusUI();
        }

        if (shieldHP != null && shieldBySlot != null)
        {
            EnsureShieldGrantPools();

            int n = Mathf.Min(shieldHP.Length, shieldBySlot.Length);
            for (int i = 0; i < n; i++)
            {
                shieldHP[i] = Mathf.Max(0f, shieldBySlot[i]);

                if (_shieldedGrantTeam != null && i < _shieldedGrantTeam.Length)
                    _shieldedGrantTeam[i] = 0f;

                if (_ironShieldCarrySlots != null && i < _ironShieldCarrySlots.Length)
                    _ironShieldCarrySlots[i] = (shieldHP[i] > 0f);
            }

            PushHPBars();
        }
    }

    public void ExtractIronCarryFromPlayerField(out IronFieldStatusSnapshot snap, out float[] shieldBySlot)
    {
        snap = GetPlayerFieldStatusSnapshot();
        shieldBySlot = (shieldHP != null) ? (float[])shieldHP.Clone() : null;
    }

    private void EndBattle(bool victory, bool escaped = false)
    {
        if (!inBattle) return;

        inBattle = false;
        SetIsPlayerTurn(false);
        GameEvents.OnBattleStateChanged?.Invoke();

        ConfigureForAuto(false);

        if (benchBtn1) benchBtn1.interactable = false;
        if (benchBtn2) benchBtn2.interactable = false;

        pendingAction = PlayerAction.None;
        defendActiveThisRound = false;
        wildDefendActiveThisRound = false;
        wildChargedNextAttack = false;
        ResetStatusIcons();

        if (turnCR != null) { StopCoroutine(turnCR); turnCR = null; }

        // Restore BattleCalc RNG to default (UnityEngine.Random)
        BattleCalc.ResetRng();
        _rng.ClearAll();
        float survived = Mathf.Max(0f, Time.unscaledTime - startTime);

        int basecredits = 0;
        int finalcredits = 0;
        int creditTitleBonus = 0;

        if (!escaped)
        {
            basecredits = BattleRewards.creditsFor(victory, wildLevel, survived);
            finalcredits = basecredits;

            if (victory && teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            {
                string leadId = teamIds[activeIndex];
                float cm = _cachedCreditMult;
                Debug.Log($"[BattleManager] Title mult for lead '{leadId}': {cm} (basecredits={basecredits}) [cached]");
                if (cm > 0f)
                {
                    finalcredits = Mathf.Max(0, Mathf.RoundToInt(basecredits * cm));
                    creditTitleBonus = Mathf.Max(0, finalcredits - basecredits);
                    Debug.Log($"[BattleManager] finalcredits={finalcredits}, creditTitleBonus={creditTitleBonus}");
                }
            }

            if (finalcredits < 0) finalcredits = 0;
        }

        int baseCores = Mathf.Max(1, 2 + wildLevel);
        int growthCoreBaseAfterShiny = 0;
        int growthCoreTitleBonus = 0;
        int growthCoreTotal = 0;

        var data = SaveManager.Data;

         if (victory && !escaped)
        {
            var m = (teamOwnedEffective != null && activeIndex >= 0 && activeIndex < teamOwnedEffective.Length)
                ? teamOwnedEffective[activeIndex]
                : ((data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count) ? data.team[activeIndex] : default);

            float shinyMul = ShinySystems.TrainingXpMult(m);
            int baseAfterShiny = Mathf.RoundToInt(baseCores * shinyMul);
            growthCoreBaseAfterShiny = Mathf.Max(0, baseAfterShiny);

            float titleCoreMul = 1f;
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
                titleCoreMul = Mathf.Max(0f, TitlesAdapter.GetGrowthCoreMultOnVictory(teamTitleIds[activeIndex], wildDef, wildLevel));

            int growthCoreAfterTitles = Mathf.Max(0, Mathf.RoundToInt(baseAfterShiny * titleCoreMul));

            float globalMul = 1f;
            if (GameBalance.TryGet(out var bal))
                globalMul = Mathf.Max(0f, bal.xpGainMultiplier);

            growthCoreTotal = Mathf.Max(0, Mathf.RoundToInt(growthCoreAfterTitles * globalMul));

            growthCoreTitleBonus = Mathf.Max(0, growthCoreAfterTitles - growthCoreBaseAfterShiny);

            if (growthCoreTotal > 0)
                ResourceManager.I?.Add(ResourceType.GrowthCore, growthCoreTotal);

            BattleLogger.Log($"Gained {growthCoreTotal} Growth Cores.", LogScope.Battle);
        }

        var teamList = data != null && data.team != null ? data.team : new List<OwnedMonsterData>();
        var ownedList = data != null && data.owned != null ? data.owned : new List<OwnedMonsterData>();
        long nowUnix = SaveManager.NowUnix();

        // If the player has a preferred variant (shiny/non-shiny) for a given monsterId,
        // battles may have been simulated using that preferred OwnedMonsterData (ownedUID).
        // Ensure the team list points at the same owned copy so HP/progression writes back to the correct variant.
        if (teamOwnedUidEffective != null && ownedList != null && teamList != null)
        {
            var uidMap = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);
            for (int j = 0; j < ownedList.Count; j++)
            {
                var o = ownedList[j];
                if (o == null) continue;
                if (string.IsNullOrEmpty(o.ownedUID)) continue;
                if (!uidMap.ContainsKey(o.ownedUID))
                    uidMap.Add(o.ownedUID, o);
            }

            int max = Mathf.Min(teamCount, Mathf.Min(teamList.Count, teamOwnedUidEffective.Length));
            for (int i = 0; i < max; i++)
            {
                string uid = teamOwnedUidEffective[i];
                if (string.IsNullOrEmpty(uid)) continue;

                if (uidMap.TryGetValue(uid, out var preferredOwned) && preferredOwned != null)
                {
                    // Swap the team slot to the preferred owned copy (same monsterId).
                    teamList[i] = preferredOwned;
                }
            }
        }

        for (int i = 0; i < teamCount && i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            int hp = Mathf.CeilToInt(Mathf.Max(0f, teamHP[i]));

            // AUTO-REMOVE DEAD TEAM MEMBERS:
            // When a team monster hits 0 HP in battle, it is automatically removed from the player's team.
            // We clear the slot (instead of removing list entries) to preserve stable UI slot indices.
            if (hp <= 0)
            {
                // IMPORTANT:
                // Even though we auto-remove dead team members from the TEAM list,
                // we must still persist the KO state back to the OWNED instance so:
                // - cooldown timers can show (OwnedMonsterListItemUI)
                // - battle eligibility correctly blocks 0 HP monsters
                // - healing services can find + heal the KO'd monster

                // Write KO back to owned list using ownedUID first (strong match).
                if (ownedList != null)
                {
                    int ownedIdx = -1;

                    if (!string.IsNullOrEmpty(t.ownedUID))
                    {
                        for (int j = 0; j < ownedList.Count; j++)
                        {
                            var o = ownedList[j];
                            if (o != null && !string.IsNullOrEmpty(o.ownedUID) && o.ownedUID == t.ownedUID)
                            {
                                ownedIdx = j;
                                break;
                            }
                        }
                    }

                    // Fallback: monsterId only if unique.
                    if (ownedIdx < 0)
                    {
                        int count = 0;
                        int singleIdx = -1;
                        for (int j = 0; j < ownedList.Count; j++)
                        {
                            var o = ownedList[j];
                            if (o != null && !string.IsNullOrEmpty(o.monsterId) && o.monsterId == t.monsterId)
                            {
                                count++;
                                singleIdx = j;
                                if (count > 1) break;
                            }
                        }
                        if (count == 1) ownedIdx = singleIdx;
                    }

                    if (ownedIdx >= 0 && ownedIdx < ownedList.Count)
                    {
                        var o = ownedList[ownedIdx];
                        if (o != null)
                        {
                            // Centralized HP contract (no Save() here; battle end saves once)
                            SaveManager.SetOwnedMonsterHP(o.ownedUID, 0, stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
                            // Refresh local list entry from SaveManager in case of clamping/normalization
                            var refreshed = SaveManager.GetOwnedByUid(o.ownedUID);
                            if (refreshed != null) ownedList[ownedIdx] = refreshed;
                        }
                    }
                }

                // Clear slot
                teamList[i] = new OwnedMonsterData { monsterId = null, currentHP = 0 };
                continue;
            }

            // Centralized HP contract: update team slot (syncs owned via ownedUID / unique monsterId).
            SaveManager.SetTeamSlotHP(i, hp, stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
            // teamList references SaveManager.Data.team, so it is already updated in-place.
        }

        // Sync HP back to owned list.
        // Prefer ownedUID matching so shiny/normal variants (same monsterId) don't cross-contaminate.
        for (int i = 0; i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            int idx = -1;

            // 1) Strong match: ownedUID
            if (!string.IsNullOrEmpty(t.ownedUID))
            {
                for (int j = 0; j < ownedList.Count; j++)
                {
                    var o = ownedList[j];
                    if (o != null && !string.IsNullOrEmpty(o.ownedUID) && o.ownedUID == t.ownedUID)
                    {
                        idx = j;
                        break;
                    }
                }
            }

            // 2) Fallback: monsterId only if unique in owned list
            if (idx < 0)
            {
                int count = 0;
                int singleIdx = -1;
                for (int j = 0; j < ownedList.Count; j++)
                {
                    var o = ownedList[j];
                    if (o != null && !string.IsNullOrEmpty(o.monsterId) && o.monsterId == t.monsterId)
                    {
                        count++;
                        singleIdx = j;
                        if (count > 1) break;
                    }
                }

                if (count == 1) idx = singleIdx;
            }

            if (idx >= 0 && idx < ownedList.Count)
            {
                var o = ownedList[idx];
                if (o != null)
                {
                                        if (!string.IsNullOrEmpty(o.ownedUID))
                    {
                        SaveManager.SetOwnedMonsterHP(o.ownedUID, Mathf.Max(0, t.currentHP), stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
                        var refreshed = SaveManager.GetOwnedByUid(o.ownedUID);
                        if (refreshed != null) ownedList[idx] = refreshed;
                    }
                    else
                    {
                        // No ownedUID on owned entry: rely on team-slot HP contract fallback (unique monsterId)
                        // to propagate HP safely without cross-contamination.
                        SaveManager.SetTeamSlotHP(i, Mathf.Max(0, t.currentHP), stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
                    }
                }
            }
        }

        for (int i = 0; i < teamList.Count; i++)
        {
            var e = teamList[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;
            e.lastHPUnix = nowUnix;
            teamList[i] = e;
        }

        if (data != null)
        {
            data.owned = ownedList;
            data.team = teamList;
            SaveManager.Save();
        }

        GameEvents.OnTeamChanged?.Invoke();

        BattleTempBuffs.I?.ClearPlayerAtkBonus();
        BattleTempBuffs.I?.ClearPlayerSpeedBonus();
        BattleTempBuffs.I?.ClearPlayerHPBonus();
        BattleTempBuffs.I?.ClearPlayerDefenseBonus();

        string outcomeLabel = escaped ? "Escaped" : (victory ? "Victory" : "Defeat");
        BattleLogger.Log($"Battle ends: {outcomeLabel} (+{finalcredits} credits).", LogScope.Battle);
        BattleLogger.EndBattle(victory);

        var result = new BattleResult
        {
            victory = victory,
            escaped = escaped,
            creditsGained = finalcredits,
            creditsBase = basecredits,
            creditsTitleBonus = creditTitleBonus,
            creditsMultiplier = _cachedCreditMult,

            growthCoresGained = growthCoreTotal,
            growthCoresBase = growthCoreBaseAfterShiny,
            growthCoresTitleBonus = growthCoreTitleBonus,

            activeMonsterOwnedId = (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length) ? teamIds[activeIndex] : null,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived,
            critCount = _totalCritsThisBattle,
            turnsSurvived = _turnIndex,
            damageTaken = _totalDamageTakenThisBattle,
            damageDealt = _totalDamageDealtThisBattle,
            gotFirstHit = playerLandedFirstHitThisBattle
        };

        Debug.Log($"[BattleManager] BattleResult: base={result.creditsBase}, bonus={result.creditsTitleBonus}, totalPreScale={result.creditsGained}, active={result.activeMonsterOwnedId}");

        if (!victory && !escaped && AutoResolveActive)
        {
            EncounterManager.I?.NotifyAuto_TeamKO();
        }

        SetPostBattleWinnerVisible(victory, escaped);

        // Titles: make sure BOTH combatants end the session so per-battle stacks/buffs reset.
        // TitleManager registers multiple participants (player + wild) on battle start.
        // If we only call OnBattleEnd for the player, the wild participant remains registered
        // and the session never fully clears.
        try
        {
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            {
                string ownedId = GetTeamTitleIdSafe(activeIndex);
                if (!string.IsNullOrEmpty(ownedId))
                    TitlesAdapter.OnBattleEnd(ownedId, victory, wildDef, wildLevel);
            }

            if (!string.IsNullOrEmpty(_wildCombatIdForTitles))
                TitlesAdapter.OnBattleEnd(_wildCombatIdForTitles, victory, wildDef, wildLevel);
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleEnd exception: {ex.Message}", LogScope.Battle);
        }

        onEnd?.Invoke(result);
        GameEvents.BattleFinished?.Invoke(result);
    }
}
