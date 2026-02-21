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

                // ---------- SHIELD (Player) ----------
        // Visible shield indicator should reflect BOTH:
        //   1) regular shields (Defend/job/guard shields) and
        //   2) BattleStartShieldTitle (title shield buffer)
        // IMPORTANT UX: Title shield should NOT change the HP number itself (it absorbs damage first),
        // but it SHOULD remain visible as (+X) until depleted.
        int pShield = 0;
        if (shieldHP != null && activeIndex >= 0 && activeIndex < shieldHP.Length)
            pShield += Mathf.RoundToInt(shieldHP[activeIndex]);
        if (titleShieldHP != null && activeIndex >= 0 && activeIndex < titleShieldHP.Length)
            pShield += Mathf.RoundToInt(titleShieldHP[activeIndex]);
        pShield = Mathf.Max(0, pShield);

        // ---------- SHIELD (Wild) ----------
        int wShield = 0;
        try { wShield += Mathf.RoundToInt(Mathf.Max(0f, wildShieldHP)); } catch { /* ignored */ }
        try { wShield += Mathf.RoundToInt(Mathf.Max(0f, wildTitleShieldHP)); } catch { /* ignored */ }
        wShield = Mathf.Max(0, wShield);
        if (feedback != null && feedback.HasHPTextWired)
        {
            feedback.SetHPTexts(
                playerCur: playerCur,
                playerMax: playerMax,
                wildCur: wildCur,
                wildMax: wildMax,
                playerShield: pShield,
                wildShield: wShield
            );
            return;
        }

        int pCurI = Mathf.CeilToInt(playerCur);
        int pMaxI = Mathf.CeilToInt(playerMax);
        int wCurI = Mathf.CeilToInt(wildCur);
        int wMaxI = Mathf.CeilToInt(wildMax);

        if (playerHPText)
        {
            playerHPText.text = (pShield > 0)
                ? $"HP: {pCurI}/{pMaxI}  (+{pShield} Shield)"
                : $"HP: {pCurI}/{pMaxI}";
            playerHPText.color = StatNeutral;
        }

        if (wildHPText)
        {
            wildHPText.text = (wShield > 0)
                ? $"HP: {wCurI}/{wMaxI}  (+{wShield} Shield)"
                : $"HP: {wCurI}/{wMaxI}";
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


    
    // ─────────────────────────────────────────────────────────────────────────
    // UI Baseline getters (adjusted stats without Titles) captured once per battle.
    // ─────────────────────────────────────────────────────────────────────────
    private int GetUiBaselineAtk(int idx, int fallback) => (_uiBaseAtk != null && idx >= 0 && idx < _uiBaseAtk.Length && _uiBaseAtk[idx] > 0) ? _uiBaseAtk[idx] : fallback;
    private int GetUiBaselineDef(int idx, int fallback) => (_uiBaseDef != null && idx >= 0 && idx < _uiBaseDef.Length && _uiBaseDef[idx] >= 0) ? _uiBaseDef[idx] : fallback;
    private int GetUiBaselineSpd(int idx, int fallback) => (_uiBaseSpd != null && idx >= 0 && idx < _uiBaseSpd.Length && _uiBaseSpd[idx] > 0) ? _uiBaseSpd[idx] : fallback;
    private int GetUiBaselineMaxHp(int idx, int fallback) => (_uiBaseMaxHp != null && idx >= 0 && idx < _uiBaseMaxHp.Length && _uiBaseMaxHp[idx] > 0) ? _uiBaseMaxHp[idx] : fallback;

private void UpdateWildInfoUI()
    {
        if (!wildDef) return;

        // Preferred path: central stat pipeline.
        if (Stats != null)
        {
            BattleStatBlock baseB = Stats.GetAdjustedWild();
            BattleStatBlock effB = Stats.GetEffectiveWild();

            if (wildIdText) wildIdText.text = $"ID: {wildDef.id}";
            if (wildTypeText) wildTypeText.text = $"TYPE: {wildDef.type}";
            if (wildRarityText) wildRarityText.text = $"RARITY: {wildDef.rarity}";
            if (wildLevelText) wildLevelText.text = $"LVL: {wildLevel}";

            if (wildHPText) SetStatRowColorAndText(wildHPText, "HP", baseB.maxHP, effB.maxHP, minFinal: 1);
            if (wildATKText)
            {
                SetStatRowColorAndText(wildATKText, "ATK", baseB.atk, effB.atk, minFinal: 1);

            }
            if (wildDEFText) SetStatRowColorAndText(wildDEFText, "DEF", baseB.def, effB.def, minFinal: 0);
            if (wildSPDText) SetStatRowColorAndText(wildSPDText, "SPD", baseB.spd, effB.spd, minFinal: 1);
            return;
        }

        // "Base" here means the adjusted stat the battle is tuned to (level-scaled + encounter threat).
        int baseHP  = (_uiBaseWildMaxHp > 0) ? _uiBaseWildMaxHp : Mathf.RoundToInt(Mathf.Max(1f, wildBaseMaxHP));
        int baseATK = (_uiBaseWildAtk > 0) ? _uiBaseWildAtk : Mathf.RoundToInt(Mathf.Max(1f, wildBaseAttackPerTurn));
        int baseDEF = (_uiBaseWildDef >= 0) ? _uiBaseWildDef : BattleCalc.CalcDefense(wildDef, wildLevel);
        int baseSPD = (_uiBaseWildSpd > 0) ? _uiBaseWildSpd : BattleCalc.CalcSpeed(wildDef, wildLevel);

        int effHP = Mathf.RoundToInt(Mathf.Max(1f, wildMaxHP));
        int effATK = Mathf.RoundToInt(Mathf.Max(1f, wildAttackPerTurn));

        int effDEF = baseDEF;
        int effSPD = baseSPD;

        if (wildDef && !string.IsNullOrEmpty(_wildCombatIdForTitles))
        {
            var wCtx = BuildTitleContextForWild();
            float defF = TitlesAdapter.GetStatValue(_wildCombatIdForTitles, wildDef, wildLevel, "Defense", wCtx, baseDEF);
            float spdF = TitlesAdapter.GetStatValue(_wildCombatIdForTitles, wildDef, wildLevel, "Speed", wCtx, baseSPD);
            if (!float.IsNaN(defF) && !float.IsInfinity(defF)) effDEF = Mathf.Max(0, Mathf.RoundToInt(defF));
            if (!float.IsNaN(spdF) && !float.IsInfinity(spdF)) effSPD = Mathf.Max(1, Mathf.RoundToInt(spdF));
        }

        if (wildIdText) wildIdText.text = $"ID: {wildDef.id}";
        if (wildTypeText) wildTypeText.text = $"TYPE: {wildDef.type}";
        if (wildRarityText) wildRarityText.text = $"RARITY: {wildDef.rarity}";
        if (wildLevelText) wildLevelText.text = $"LVL: {wildLevel}";

        if (wildHPText) SetStatRowColorAndText(wildHPText, "HP", baseHP, effHP, minFinal: 1);
        if (wildATKText)
        {
            SetStatRowColorAndText(wildATKText, "ATK", baseATK, effATK, minFinal: 1);
        }
        if (wildDEFText) SetStatRowColorAndText(wildDEFText, "DEF", baseDEF, effDEF, minFinal: 0);
        if (wildSPDText) SetStatRowColorAndText(wildSPDText, "SPD", baseSPD, effSPD, minFinal: 1);
    }




    private void UpdatePlayerInfoUI()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length) return;

        var def = teamDefs[activeIndex];
        if (!def) return;

        int lvl = (teamLevels != null && activeIndex < teamLevels.Length) ? teamLevels[activeIndex] : 1;

        // Preferred path: central stat pipeline.
        if (Stats != null)
        {
            BattleStatBlock baseB = Stats.GetAdjustedPlayer(activeIndex);
            BattleStatBlock effB = Stats.GetEffectivePlayer(activeIndex);

                // Debug: inspect title-applied flat values via adapter when BattleStartFlat present
                try
                {
                    var ownedId = (teamIds != null && activeIndex < teamIds.Length) ? teamIds[activeIndex] : null;
                    if (!string.IsNullOrEmpty(ownedId))
                    {
                        var mods = TitlesAdapter.GetBattleStatMods(ownedId);
                        int atkFlatFromMods = mods.atkFlat;
                        float atkFromStatValue = TitlesAdapter.GetStatValue(ownedId, teamDefs[activeIndex], lvl, "Attack", BuildTitleContextForActive(), baseB.atk);
                        int baseAtk = baseB.atk;
                        int effAtk = effB.atk;

                        if (atkFlatFromMods != 0 || Mathf.RoundToInt(atkFromStatValue) != baseAtk)
                        {
                            Debug.Log($"[Titles DEBUG] owned={ownedId} mods.atkFlat={atkFlatFromMods} TitlesAdapter.AttackValue={Mathf.RoundToInt(atkFromStatValue)} base={baseAtk} eff={effAtk}");
                        }
                    }
                }
                catch { }

            if (playerIdText) playerIdText.text = $"ID: {def.id}";
            if (playerTypeText) playerTypeText.text = $"TYPE: {def.type}";
            if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
            if (playerLevelText) playerLevelText.text = $"LVL: {lvl}";

            if (playerHPText) SetStatRowColorAndText(playerHPText, "HP", baseB.maxHP, effB.maxHP, minFinal: 1);
            if (playerATKText) SetStatRowColorAndText(playerATKText, "ATK", baseB.atk, effB.atk, minFinal: 1);
            if (playerDEFText) SetStatRowColorAndText(playerDEFText, "DEF", baseB.def, effB.def, minFinal: 0);
            if (playerSPDText) SetStatRowColorAndText(playerSPDText, "SPD", baseB.spd, effB.spd, minFinal: 1);
            return;
        }

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
        // Base for display = baseline totals (teamMaxHP already includes training) + temp HP (no title hpPct here)
        int hpBaseForDisplay = Mathf.RoundToInt(Mathf.Max(1f, teamMaxHP[activeIndex]));
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
        {
            SetStatRowColorAndText(playerATKText, "ATK", atkBaselineForColor, atkCombinedFinal, minFinal: 1);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // DEV overlay: show TurnBooster stacks + raw value to diagnose rounding / id mismatches.
            int tbOwned = TitlesAdapter.Debug_GetTurnBoosterStacks(ctx.ownedId);
            int tbDef = (def != null) ? TitlesAdapter.Debug_GetTurnBoosterStacks(def.id) : 0;
            string activeId = TitlesAdapter.Debug_GetActiveBattleMonsterId();
            int tIdx = TitlesAdapter.Debug_GetTurnIndex();
            playerATKText.text += $" <size=70%><color=#AAAAAA>(TB o:{tbOwned} d:{tbDef} active:{activeId} t:{tIdx} raw:{atkFinalF:0.##} pre:{atkPreTitle})</color></size>";
            #endif
        }

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

        // Shiny-aware display
        // IMPORTANT: the player can set a preferred variant (shiny/non-shiny) without changing the team list.
        // Battle UI should reflect that preference.
        bool isShiny = false;

        if (def != null && !string.IsNullOrEmpty(def.id))
        {
            var pref = MonsterVariantPreference.GetPreferredOwned(def.id);
            if (pref != null)
            {
                isShiny = pref.isShiny || pref.shinyTier > 0;
            }
            else
            {
                var data = SaveManager.Data;
                if (data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count)
                {
                    var om = data.team[activeIndex];
                    isShiny = om != null && (om.isShiny || om.shinyTier > 0);
                }
            }
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

        // Primary status UI (Phase 2) is cleared here so a new battle always starts clean.
        feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Player);
        feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Wild);

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

        // Primary status UI (Phase 2/3)
        if (teamStatus != null && activeIndex >= 0 && activeIndex < teamStatus.Length)
        {
            var st = teamStatus[activeIndex];
            if (st == StatusType.None)
            {
                feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Player);
            }
            else
            {
                var icon = statusLibrary != null ? statusLibrary.GetIcon(st) : null;
                bool persistent = teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length && teamStatusPersistent[activeIndex];
                int turns = persistent ? 0 : (teamStatusTurns != null && activeIndex < teamStatusTurns.Length ? Mathf.Max(0, teamStatusTurns[activeIndex]) : 0);
                feedback.SetPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Player, icon, turns, persistent);
            }
        }

        if (wildStatus == StatusType.None)
        {
            feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Wild);
        }
        else
        {
            var icon = statusLibrary != null ? statusLibrary.GetIcon(wildStatus) : null;
            int turns = wildStatusPersistent ? 0 : Mathf.Max(0, wildStatusTurns);
            feedback.SetPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Wild, icon, turns, wildStatusPersistent);
        }
    }


    private void HandleBattleStatsChanged()
    {
        if (!inBattle) return;

        // If this event was raised by RequestBattleStatRebuild, the rebuild already happened.
        // Keep the event for any other subscribers, but ignore it here to prevent recursion.
        if (_ignoreNextBattleStatsEvent)
        {
            _ignoreNextBattleStatsEvent = false;
            return;
        }

        // Route ALL stat-change notifications through the unified pipeline.
        // External systems (boosters, etc.) may raise GameEvents.BattleStatsChanged.
        RequestBattleStatRebuild(BattleStatRebuildReason.ExternalEvent);
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

    // =====================
    // Stable public UI/feedback API
    // =====================

    /// <summary>
    /// Remaining BattleStartShield HP for the active player monster from Titles (rounded to whole HP).
    /// Used by feedback/UI systems (e.g., (+Shield) text) and must remain stable.
    /// </summary>
    public int GetActivePlayerTitleShieldTotal()
    {
        try
        {
            if (titleShieldHP == null) return 0;
            if (activeIndex < 0 || activeIndex >= titleShieldHP.Length) return 0;
            return Mathf.RoundToInt(Mathf.Max(0f, titleShieldHP[activeIndex]));
        }
        catch { return 0; }
    }

    /// <summary>
    /// Remaining BattleStartShield HP for the wild opponent from Titles (rounded to whole HP).
    /// Used by feedback/UI systems (e.g., (+Shield) text) and must remain stable.
    /// </summary>
    public int GetWildTitleShieldTotal()
    {
        try { return Mathf.RoundToInt(Mathf.Max(0f, wildTitleShieldHP)); }
        catch { return 0; }
    }

}