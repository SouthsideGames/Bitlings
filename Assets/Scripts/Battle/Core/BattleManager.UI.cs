using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public partial class BattleManager : MonoBehaviour
{

    private void ClampAndPushActiveHP()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        teamHP[activeIndex] = Mathf.Min(teamHP[activeIndex], curMax);

        // UI/FX should be driven by events when consumers exist.
        Emit(BattleEvent.UIRefreshHP());

        // Legacy fallback (no event consumers): keep old direct wiring working.
        if (!HasBattleEventConsumers)
        {
            if (feedback != null)
            {
                feedback.SetHPBars(
                    playerCur: teamHP[activeIndex],
                    playerMax: curMax,
                    wildCur: wildHP,
                    wildMax: wildMaxHP
                );
            }
            else
            {
                if (playerHPBar)
                {
                    playerHPBar.maxValue = curMax;
                    playerHPBar.value = Mathf.Clamp(teamHP[activeIndex], 0f, curMax);
                }
                if (wildHPBar)
                {
                    wildHPBar.maxValue = wildMaxHP;
                    wildHPBar.value = Mathf.Clamp(wildHP, 0f, wildMaxHP);
                }
            }

            UpdatePlayerInfoUI();
            UpdateHPTextUI();
        }
    }




    private void PushHPBars()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);

        Emit(BattleEvent.UIRefreshHP());

        if (!HasBattleEventConsumers)
        {
            if (feedback != null)
            {
                feedback.SetHPBars(
                    playerCur: teamHP[activeIndex],
                    playerMax: curMax,
                    wildCur: wildHP,
                    wildMax: wildMaxHP
                );
            }
            else
            {
                if (wildHPBar)
                {
                    wildHPBar.maxValue = wildMaxHP;
                    wildHPBar.value = Mathf.Clamp(wildHP, 0f, wildMaxHP);
                }
                if (playerHPBar)
                {
                    playerHPBar.maxValue = curMax;
                    playerHPBar.value = Mathf.Clamp(teamHP[activeIndex], 0f, curMax);
                }
            }

            UpdatePlayerInfoUI();
            UpdateHPTextUI();
        }
    }





    private void UpdateHPTextUI()
    {
        float playerMax = GetFinalMaxHPForIndex(activeIndex);
        playerMax = Mathf.Max(1f, playerMax);

        float playerCur =
            (teamHP != null && activeIndex >= 0 && activeIndex < teamHP.Length)
                ? teamHP[activeIndex]
                : playerMax;

        playerCur = Mathf.Clamp(playerCur, 0f, playerMax);

        float wildMax = Mathf.Max(1f, wildMaxHP);
        float wildCur = Mathf.Clamp(wildHP, 0f, wildMax);

        if (feedback != null && feedback.HasHPTextWired)
        {
            feedback.SetHPTexts(
                playerCur: playerCur,
                playerMax: playerMax,
                wildCur: wildCur,
                wildMax: wildMax
            );
            return;
        }

        int pCurI = Mathf.CeilToInt(playerCur);
        int pMaxI = Mathf.CeilToInt(playerMax);
        int wCurI = Mathf.CeilToInt(wildCur);
        int wMaxI = Mathf.CeilToInt(wildMax);

        if (playerHPText)
        {
            playerHPText.text = $"HP: {pCurI}/{pMaxI}";
            playerHPText.color = StatNeutral;
        }

        if (wildHPText)
        {
            wildHPText.text = $"HP: {wCurI}/{wMaxI}";
            wildHPText.color = StatNeutral;
        }

        if (playerHPBar)
        {
            playerHPBar.maxValue = playerMax;
            playerHPBar.value = playerCur;
        }

        if (wildHPBar)
        {
            wildHPBar.maxValue = wildMax;
            wildHPBar.value = wildCur;
        }
    }





    private void RefreshBenchUI()
    {
        FillOtherIndices(_scratchOthers);
        List<int> others = _scratchOthers;

if (benchImg1)
        {
            if (others.Count > 0)
            {
                benchImg1.enabled = true;
                benchImg1.sprite = teamDefs[others[0]]?.icon;
                benchImg1.color = teamHP[others[0]] > 0 ? Color.white : new Color(1, 1, 1, 0.35f);
            }
            else benchImg1.enabled = false;
        }
        if (benchBtn1) benchBtn1.interactable = others.Count > 0 && teamHP[others[0]] > 0f;

        if (benchHPText1)
        {
            if (others.Count > 0) SetBenchHP(benchHPText1, others[0]);
            else benchHPText1.gameObject.SetActive(false);
        }

        if (benchImg2)
        {
            if (others.Count > 1)
            {
                benchImg2.enabled = true;
                benchImg2.sprite = teamDefs[others[1]]?.icon;
                benchImg2.color = teamHP[others[1]] > 0 ? Color.white : new Color(1, 1, 1, 0.35f);
            }
            else benchImg2.enabled = false;
        }
        if (benchBtn2) benchBtn2.interactable = others.Count > 1 && teamHP[others[1]] > 0f;

        if (benchHPText2)
        {
            if (others.Count > 1) SetBenchHP(benchHPText2, others[1]);
            else benchHPText2.gameObject.SetActive(false);
        }
    }




    private void ClickBench(int benchSlot)
{
    if (!inBattle) return;
    if (!manualTurns) return; // swapping is a manual-turn action
    if (!IsPlayerTurn) return;
    if (isResolvingPlayerTurn) return;
    if (_narrationLock) return;

    // Lock swapping once an action is queued.
    if (pendingAction != PlayerAction.None) return;

    // Queue a Swap action (swapping costs the turn).
    pendingSwapBenchSlot = benchSlot;
    pendingAction = PlayerAction.Swap;
    GameEvents.OnBattleStateChanged?.Invoke();
}


    private void UpdateWildInfoUI()
    {
        if (!wildDef) return;

        int baseHP = Mathf.RoundToInt(BattleCalc.CalcHP(wildDef, wildLevel));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(wildDef, wildLevel);
        int baseSPD = BattleCalc.CalcSpeed(wildDef, wildLevel);

        int effHP = Mathf.RoundToInt(wildMaxHP);
        int effATK = Mathf.RoundToInt(wildAttackPerTurn);

        int effDEF = baseDEF;
        int effSPD = baseSPD;

        if (wildIdText) wildIdText.text = $"ID: {wildDef.id}";
        if (wildTypeText) wildTypeText.text = $"TYPE: {wildDef.type}";
        if (wildRarityText) wildRarityText.text = $"RARITY: {wildDef.rarity}";
        if (wildLevelText) wildLevelText.text = $"LVL: {wildLevel}";

        if (wildHPText) SetStatRowColorAndText(wildHPText, "HP", baseHP, effHP, minFinal: 1);
        if (wildATKText) SetStatRowColorAndText(wildATKText, "ATK", baseATK, effATK, minFinal: 1);
        if (wildDEFText) SetStatRowColorAndText(wildDEFText, "DEF", baseDEF, effDEF, minFinal: 0);
        if (wildSPDText) SetStatRowColorAndText(wildSPDText, "SPD", baseSPD, effSPD, minFinal: 1);
    }




    private void UpdatePlayerInfoUI()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length) return;

        var def = teamDefs[activeIndex];
        if (!def) return;

        int lvl = (teamLevels != null && activeIndex < teamLevels.Length) ? teamLevels[activeIndex] : 1;

        // ─────────────────────────────────────────────────────────────────────────
        // Baseline TOTALS (SpeciesBase + LevelGrowth + TrainingBonus + flatAtkBonus*)
        // *flatAtkBonus is treated as permanent progression ATK bonus and included in total ATK.
        // Legacy guard: if old saves mirrored training into flatAtkBonus, we avoid double counting.
        // ─────────────────────────────────────────────────────────────────────────
        GetProgressionTotalsForIndex(
            activeIndex,
            out int baseTotalHP,
            out int baseTotalATK,
            out int baseTotalDEF,
            out int baseTotalSPD,
            out _ 
        );

        int tempHPFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        int tempATKFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        int tempDEFFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;
        int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;
        // Turn-based boosters (BattleBoosterController) also contribute flat bonuses.
        var boosterCtrl = BattleBoosterController.I;
        if (boosterCtrl != null)
        {
            tempATKFlat += Mathf.Max(0, boosterCtrl.GetAttackBonus());
            tempSPDFlat += Mathf.Max(0, boosterCtrl.GetSpeedBonus());
        }


        var ctx = TitleContext.Empty;
        ctx.ownedId = (teamIds != null && activeIndex < teamIds.Length) ? teamIds[activeIndex] : "";

        float maxNoConds = GetActiveMaxHP_NoConditionals(teamMaxHP[activeIndex], activeIndex);
        maxNoConds = Mathf.Max(1f, maxNoConds);

        float currentHP = (teamHP != null && activeIndex < teamHP.Length) ? teamHP[activeIndex] : maxNoConds;
        ctx.selfHp01 = Mathf.Clamp01(currentHP / maxNoConds);


        ctx.alliesAlive = GetAlliesAliveNotIncludingActive();
        ctx.winStreak = GetWinStreakSafe();

        var cmods = GetConditionalModsForActive();

        // Header rows
        if (playerIdText) playerIdText.text = $"ID: {def.id}";
        if (playerTypeText) playerTypeText.text = $"TYPE: {def.type}";
        if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
        if (playerLevelText) playerLevelText.text = $"LVL: {lvl}";

        // ─────────────────────────────────────────────────────────────────────────
        // HP
        // Base for display = baseline totals + temp HP
        // Titles first, then conditionals (for coloring and delta)
        // ─────────────────────────────────────────────────────────────────────────
        int hpBaseForDisplay = Mathf.RoundToInt(maxNoConds);
        float hpFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "HP", ctx, hpBaseForDisplay);
        int hpTitleFinal = Mathf.Max(1, Mathf.RoundToInt(hpFinalF));

        if (playerHPText)
        {
            SetPlayerStatRowWithConditionals(
                playerHPText, "HP",
                hpBaseForDisplay, hpTitleFinal,
                condFlat: 0, condPct: cmods.hpPct,
                minFinal: 1
            );
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ATK
        // Base for display = baseline totals (already includes training + flatAtkBonus w/ legacy guard) + temp ATK
        // Titles first, then conditionals
        // ─────────────────────────────────────────────────────────────────────────
        // IMPORTANT: For coloring, compare FINAL against BASELINE totals (no temp/booster).
        // Otherwise, temporary boosts (like in-battle boosters) appear "neutral".
        int atkBaselineForColor = Mathf.Max(1, baseTotalATK);

        // Display/value resolution uses baseline + temp/boosters, then titles, then conditionals.
        int atkPreTitle = Mathf.Max(1, baseTotalATK + tempATKFlat);
        float atkFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Attack", ctx, atkPreTitle);
        int atkAfterTitles = Mathf.Max(1, Mathf.RoundToInt(atkFinalF));

        // Conditionals are applied on top of the post-title value.
        int atkCondFlat = Mathf.Max(0, cmods.atkFlat);
        float atkCondPct = Mathf.Max(0f, cmods.atkPct);
        int atkCombinedFinal = Mathf.Max(1, Mathf.RoundToInt((atkAfterTitles + atkCondFlat) * (1f + atkCondPct)));

        if (playerATKText)
            SetStatRowColorAndText(playerATKText, "ATK", atkBaselineForColor, atkCombinedFinal, minFinal: 1);

        // ─────────────────────────────────────────────────────────────────────────
        // DEF
        // Base for display = baseline totals + temp DEF
        // Titles first, then conditionals
        // ─────────────────────────────────────────────────────────────────────────
        int defBaselineForColor = Mathf.Max(0, baseTotalDEF);

        int defPreTitle = Mathf.Max(0, baseTotalDEF + tempDEFFlat);
        float defFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Defense", ctx, defPreTitle);
        int defAfterTitles = Mathf.Max(0, Mathf.RoundToInt(defFinalF));

        int defCondFlat = Mathf.Max(0, cmods.defFlat);
        float defCondPct = Mathf.Max(0f, cmods.defPct);
        int defCombinedFinal = Mathf.Max(0, Mathf.RoundToInt((defAfterTitles + defCondFlat) * (1f + defCondPct)));

        if (playerDEFText)
            SetStatRowColorAndText(playerDEFText, "DEF", defBaselineForColor, defCombinedFinal, minFinal: 0);

        // ─────────────────────────────────────────────────────────────────────────
        // SPD
        // Base for display = baseline totals + temp SPD
        // Titles first, then conditionals
        // ─────────────────────────────────────────────────────────────────────────
        int spdBaselineForColor = Mathf.Max(1, baseTotalSPD);

        int spdPreTitle = Mathf.Max(1, baseTotalSPD + tempSPDFlat);
        float spdFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Speed", ctx, spdPreTitle);
        int spdAfterTitles = Mathf.Max(1, Mathf.RoundToInt(spdFinalF));

        int spdCondFlat = Mathf.Max(0, cmods.spdFlat);
        float spdCondPct = Mathf.Max(0f, cmods.spdPct);
        int spdCombinedFinal = Mathf.Max(1, Mathf.RoundToInt((spdAfterTitles + spdCondFlat) * (1f + spdCondPct)));

        if (playerSPDText)
            SetStatRowColorAndText(playerSPDText, "SPD", spdBaselineForColor, spdCombinedFinal, minFinal: 1);

        bool resistOn = BattleTempBuffs.I && BattleTempBuffs.I.IsTypeResistActive();
        if (resistOn && playerRarityText) playerRarityText.text += " [Resist]";
    }





    private void ApplyActiveToUI()
    {
        var def = teamDefs[activeIndex];
        var lvl = teamLevels[activeIndex];

        // Shiny-aware display (team entries carry isShiny/shinyTier)
        bool isShiny = false;
        var data = SaveManager.Data;
        if (data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count)
        {
            var om = data.team[activeIndex];
            isShiny = om != null && (om.isShiny || om.shinyTier > 0);
        }

        if (playerIcon)
        {
            if (def)
            {
                var s = MonsterNameFormatter.GetIcon(def, isShiny, backIcon: true);
                playerIcon.sprite = s ? s : (def.backIcon ? def.backIcon : def.icon);
            }
            else
            {
                playerIcon.sprite = null;
            }
        }

        if (playerNameText) playerNameText.text = def ? MonsterNameFormatter.Format(def, isShiny) : "";
        if (playerLevelText) playerLevelText.text = $"Lv {lvl}";
        UpdatePlayerInfoUI();
        UpdateHPTextUI();
    }




    public void SetBattleSpeed(float s)
    {
        battleSpeed = Mathf.Clamp(s, 0.25f, 5f);
        if (SaveManager.Data != null && SaveManager.Data.settings != null)
        {
            SaveManager.Data.settings.battleSpeed = battleSpeed;
            SaveManager.Save();
        }
    }




    public void CycleBattleSpeed()
    {
        if (battleSpeed < 1.5f) SetBattleSpeed(2f);
        else if (battleSpeed < 2.5f) SetBattleSpeed(3f);
        else SetBattleSpeed(1f);
    }




    private void SetBenchHP(TextMeshProUGUI label, int teamIdx)
    {
        if (!label) return;
        if (teamIdx < 0 || teamIdx >= teamCount) { label.gameObject.SetActive(false); return; }

        float cur = Mathf.Max(0f, teamHP[teamIdx]);
        float max = Mathf.Max(1f, GetFinalMaxHPForIndex(teamIdx));
        int icur = Mathf.CeilToInt(cur);
        int imax = Mathf.CeilToInt(max);

        label.gameObject.SetActive(true);
        label.text = $"{icur}/{imax}";
        label.alpha = cur > 0f ? 1f : 0.35f;
    }


    private void SetStatRowColorAndText(TextMeshProUGUI label, string statName, int baseVal, int finalVal, int minFinal = 1)
    {
        if (!label) return;

        finalVal = Mathf.Max(minFinal, finalVal);
        baseVal = Mathf.Max(minFinal, baseVal);

        int delta = finalVal - baseVal;

        if (delta > 0) label.color = StatBuff;
        else if (delta < 0) label.color = StatNerf;
        else label.color = StatNeutral;

        if (delta == 0)
            label.text = $"{statName}: {finalVal}";
        else
            label.text = $"{statName}: {finalVal} ({(delta > 0 ? "+" : "")}{delta})";
    }




    private void SetPlayerStatRowWithConditionals(
        TextMeshProUGUI label,
        string statName,
        int baseVal,
        int titleFinalVal,
        int condFlat,
        float condPct,
        int minFinal = 1)
    {
        int condDelta = Mathf.RoundToInt(condFlat + (baseVal * condPct));
        int combinedFinal = titleFinalVal + condDelta;
        combinedFinal = Mathf.Max(minFinal, combinedFinal);

        SetStatRowColorAndText(label, statName, baseVal, combinedFinal, minFinal);
    }


    private void HandleBattleFinishedUIRefresh(BattleResult _)
    {
        if (playerPanel != null && playerPanel.activeInHierarchy)
        {
            ApplyActiveToUI();
            ClampAndPushActiveHP();
            RefreshBenchUI();
        }
    }


    private void ResetStatusIcons()
    {
        if (!feedback) return;

        Emit(BattleEvent.GuardChanged(BattleSide.Player, false));
        if (!HasBattleEventConsumers && feedback) feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Player, false);
        Emit(BattleEvent.GuardChanged(BattleSide.Wild, false));
        if (!HasBattleEventConsumers && feedback) feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Wild, false);

        Emit(BattleEvent.ChargeChanged(BattleSide.Player, false));
        if (!HasBattleEventConsumers && feedback) feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Player, false);
        Emit(BattleEvent.ChargeChanged(BattleSide.Wild, false));
        if (!HasBattleEventConsumers && feedback) feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Wild, false);
    }

    private void RefreshStatusIconsFromState()
    {
        if (!feedback) return;

        // Guard status (this round only)
        Emit(BattleEvent.GuardChanged(BattleSide.Player, defendActiveThisRound));
        if (!HasBattleEventConsumers && feedback) feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Player, defendActiveThisRound);
        Emit(BattleEvent.GuardChanged(BattleSide.Wild, wildDefendActiveThisRound));
        if (!HasBattleEventConsumers && feedback) feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Wild, wildDefendActiveThisRound);

        // Charge status (persists until spent)
        bool playerCharged =
            chargedNextAttack != null &&
            activeIndex >= 0 &&
            activeIndex < chargedNextAttack.Length &&
            chargedNextAttack[activeIndex];

        Emit(BattleEvent.ChargeChanged(BattleSide.Player, playerCharged));
        if (!HasBattleEventConsumers && feedback) feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Player, playerCharged);
        Emit(BattleEvent.ChargeChanged(BattleSide.Wild, wildChargedNextAttack));
        if (!HasBattleEventConsumers && feedback) feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Wild, wildChargedNextAttack);
    }


    private void HandleBattleStatsChanged()
    {
        if (!inBattle) return;

        UpdatePlayerInfoUI();
        UpdateWildInfoUI();
    }


    private void SetPostBattleWinnerVisible(bool victory, bool escaped)
    {
        if (escaped)
        {
            if (playerPanel) playerPanel.SetActive(true);
            if (wildPanel) wildPanel.SetActive(false);
            return;
        }

        if (victory)
        {
            if (playerPanel) playerPanel.SetActive(true);
            if (wildPanel) wildPanel.SetActive(false);
        }
        else
        {
            if (playerPanel) playerPanel.SetActive(false);
            if (wildPanel) wildPanel.SetActive(true);
        }
    }

}
