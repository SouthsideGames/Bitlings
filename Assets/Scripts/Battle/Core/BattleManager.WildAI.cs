using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// BattleManager.WildAI
// Wild action selection, telegraphing, and AI decision helpers.
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public partial class BattleManager : MonoBehaviour
{
    private bool ShouldShowWildTelegraphText()
    {
        if (!AutoResolveActive)
            return true;

        return _turnIndex < autoTelegraphTextFirstTurns;
    }

    private float GetWildIntentIconDuration()
    {
        if (AutoResolveActive)
            return wildIntentIconDurationAuto;

        return wildIntentIconDurationManual;
    }

    private string GetWildDisplayName(string fallback = "Foe")
    {
        if (!wildDef) return fallback;
        bool premiumWild = RiftManager.I != null && RiftManager.I.CurrentWildIsPremium;
        return MonsterNameFormatter.Format(wildDef, premiumWild);
    }

    private string GetWildTelegraphLine(EnemyAction action)
    {
        switch (action)
        {
            case EnemyAction.Attack: return telegraphAttack;
            case EnemyAction.Defend: return telegraphDefend;
            case EnemyAction.Focus:  return telegraphFocus;
            case EnemyAction.Run:    return telegraphRun;
            default: return string.Empty;
        }
    }

    private IEnumerator Co_TelegraphWildIntent(EnemyAction action)
    {
        if (!showWildIntentIcons)
            yield break;

        if (feedback != null)
        {
            var fbAction = BattleFeedbackManager.BattleFeedbackAction.Attack;

            switch (action)
            {
                case EnemyAction.Attack: fbAction = BattleFeedbackManager.BattleFeedbackAction.Attack; break;
                case EnemyAction.Defend: fbAction = BattleFeedbackManager.BattleFeedbackAction.Defend; break;
                case EnemyAction.Focus:  fbAction = BattleFeedbackManager.BattleFeedbackAction.Focus;  break;
                case EnemyAction.Run:    fbAction = BattleFeedbackManager.BattleFeedbackAction.Run;    break;
            }

            Emit(BattleEvent.IntentTelegraph(BattleSide.Wild, fbAction.ToString()));
            if (!HasBattleEventConsumers && feedback)
                feedback.ShowWildIntent(fbAction, GetWildIntentIconDuration(), false);
        }

        // Manual: always narration. Auto: narration only for first N turns.
        if (ShouldShowWildTelegraphText())
        {
            string line = GetWildTelegraphLine(action);
            if (!string.IsNullOrEmpty(line))
                yield return Say(line);
        }

        if (wildIntentTelegraphPause > 0f)
            yield return CoWaitUnscaled(wildIntentTelegraphPause);
    }

    private EnemyAction ChooseEnemyAction()
    {
        if (!wildDef || wildMaxHP <= 0.01f) // TODO: confirm this 0.01f is intentional
            return EnemyAction.Attack;

        if (IsWildFrozen())
            return EnemyAction.None;

        float hpRatio = Mathf.Clamp01(wildHP / Mathf.Max(1f, wildMaxHP));
        BattleAction action = BattleAction.Attack;

        if (wildDef.Personality != null)
        {
            var playerTypeDef = (teamDefs != null && activeIndex >= 0 && activeIndex < teamDefs.Length)
                ? teamDefs[activeIndex] : null;
            float typeMatchupMult = (playerTypeDef != null)
                ? BattleTypeChart.GetMultiplier(wildDef.type, playerTypeDef.type)
                : 1f;

            var ctx = new PersonalityContext
            {
                selfHpRatio = hpRatio,
                enemyHpRatio = (teamHP != null && activeIndex >= 0 && activeIndex < teamHP.Length)
                    ? Mathf.Clamp01(teamHP[activeIndex] / Mathf.Max(1f, (teamMaxHP != null && activeIndex < teamMaxHP.Length) ? teamMaxHP[activeIndex] : 1f))
                    : 1f,
                hasSuperEffectiveMove = typeMatchupMult > 1f,
                isBadlyMatched = typeMatchupMult < 1f,
                turnNumber = Mathf.Max(1, _turnIndex + 1)
            };

            if (IsHardModePersonalityActive())
            {
                ctx.superEffectiveAttackBonus = Mathf.Max(0, hardModeSuperEffectiveBonus);
                ctx.eachTurnAttackBonus       = Mathf.Max(0, hardModeAttackPressureBonus);
            }

            action = wildDef.Personality.ChooseAction(in ctx, _rng.EnemyRng);

            if (ctx.enemyHpRatio < 0.35f && action == BattleAction.Defend && Rng01() < 0.55f)
            {
                action = BattleAction.Attack;
                BattleLogger.Log($"[AI] Wild senses a wounded opponent and presses the advantage (enemyHpRatio={ctx.enemyHpRatio:F2}): overrides Defend â†’ Attack.", LogScope.Battle);
            }

            if (_turnIndex > wildAIDefendDecayAfterTurn && action == BattleAction.Defend && Rng01() < 0.6f)
            {
                action = BattleAction.Attack;
                BattleLogger.Log("[AI] Wild grows desperate in a long fight and abandons Defend for Attack.", LogScope.Battle);
            }
        }

        EnemyAction Fallback()
        {
            if (hpRatio < 0.25f && Rng01() < 0.40f)
                return EnemyAction.Run;
            if (hpRatio < 0.50f && Rng01() < 0.30f)
                return EnemyAction.Defend;
            if (Rng01() < 0.15f)
                return EnemyAction.Focus;
            return EnemyAction.Attack;
        }

        switch (action)
        {
            case BattleAction.Attack: action = BattleAction.Attack; break;
            case BattleAction.Defend: action = BattleAction.Defend; break;
            case BattleAction.Focus:  action = BattleAction.Focus;  break;
            case BattleAction.Run:    action = BattleAction.Run;    break;
            default: action = BattleAction.Attack; break;
        }

        if (IsWildSundered())
        {
            if (action == BattleAction.Defend || action == BattleAction.Run)
                action = BattleAction.Attack;
        }

        if (IsWildWyrmFury())
        {
            if (action == BattleAction.Focus)
                action = BattleAction.Attack;
        }

        // If the wild already has a charge loaded, always cash it in.
        // Prevents Focus-when-charged bug and ensures the damage bonus is never wasted.
        if (wildChargedNextAttack)
            action = BattleAction.Attack;

        switch (action)
        {
            case BattleAction.Attack: return EnemyAction.Attack;
            case BattleAction.Defend: return EnemyAction.Defend;
            case BattleAction.Focus:  return EnemyAction.Focus;
            case BattleAction.Run:    return EnemyAction.Run;
            default: return Fallback();
        }
    }

    private PlayerAction ChoosePlayerFailsafeAction()
    {
        MonsterDataSO activeDef = GetTeamDefSafe(activeIndex);
        if (activeDef?.Personality == null)
            return PlayerAction.Attack;

        float maxHp = GetFinalMaxHPForIndex(activeIndex);
        float hpRatio = (maxHp > 0.01f && teamHP != null && activeIndex >= 0 && activeIndex < teamHP.Length) // TODO: confirm this 0.01f is intentional
            ? Mathf.Clamp01(teamHP[activeIndex] / maxHp)
            : 1f;

        float typeMatchupMult = wildDef != null
            ? BattleTypeChart.GetMultiplier(activeDef.type, wildDef.type)
            : 1f;

        var ctx = new PersonalityContext
        {
            selfHpRatio           = hpRatio,
            hasSuperEffectiveMove = typeMatchupMult > 1f,
            isBadlyMatched        = typeMatchupMult < 1f,
            turnNumber            = Mathf.Max(1, _turnIndex + 1)
        };

        BattleAction action = activeDef.Personality.ChooseAction(in ctx, _rng.EnemyRng);

        if (GetPlayerTeamRallyBonusPctSafe() > 0f && action != BattleAction.Attack)
        {
            action = BattleAction.Attack;
            BattleLogger.Log("[AI] Failsafe override: Rally is active, auto-queueing Attack.", LogScope.Battle);
        }

        // If the player already has a charge loaded, attack to cash it in.
        if (chargedNextAttack != null && activeIndex >= 0 && activeIndex < chargedNextAttack.Length && chargedNextAttack[activeIndex])
            action = BattleAction.Attack;

        switch (action)
        {
            case BattleAction.Attack: return PlayerAction.Attack;
            case BattleAction.Defend: return PlayerAction.Defend;
            case BattleAction.Focus:  return PlayerAction.Focus;
            case BattleAction.Run:    return PlayerAction.Run;
            default:                  return PlayerAction.Attack;
        }
    }

    private float ComputeEnemyRunChance()
    {
        if (!wildDef || wildMaxHP <= 0.01f) // TODO: confirm this 0.01f is intentional
            return 0f;

        float hpLost01 = 1f - Mathf.Clamp01(wildHP / wildMaxHP);
        float baseChance = 0.05f;
        float hpBonus = hpLost01 * 0.70f;

        string groupName = null;
        if (wildDef.Personality != null)
        {
            try { groupName = wildDef.Personality.group.ToString(); }
            catch { groupName = null; }
        }

        if (groupName == "Evasive")
            hpBonus *= 1.3f;

        float chance = baseChance + hpBonus;
        return Mathf.Clamp01(chance);
    }

    private void ApplyPendingGuardShieldForWild()
    {
        if (wildPendingGuardShield <= 0.01f) return; // TODO: confirm this 0.01f is intentional

        string name = GetWildDisplayName("Foe");
        float gain = wildPendingGuardShield;
        wildShieldHP += gain;
        wildPendingGuardShield = 0f;

        BattleLogger.Log($"{name} gains a guard shield of {Mathf.RoundToInt(gain)}!", LogScope.Battle);
    }

    private string GetWildPersonalityLabel()
    {
        if (!wildDef || wildDef.Personality == null) return null;
        return wildDef.Personality.group.ToString();
    }

    private string GetBasicMoveName(MonsterDataSO def)
    {
        if (!def) return "Attack";
        return !string.IsNullOrEmpty(def.basicAttackName) ? def.basicAttackName : "Attack";
    }

    private bool RollEnemyDefendSuccess()
    {
        float chance = Mathf.Clamp01(wildDefendCurrentSuccess);
        bool ok = Rng01() <= chance;

        if (ok)
        {
            wildDefendConsecutiveUses++;
            float next = defendFirstUseSuccess * Mathf.Pow(wildDefendRepeatMultiplier, wildDefendConsecutiveUses);
            wildDefendCurrentSuccess = Mathf.Max(defendMinSuccess, next);
        }
        else
        {
            wildDefendConsecutiveUses = 0;
            wildDefendCurrentSuccess = defendFirstUseSuccess;
        }

        return ok;
    }

    private void ResetEnemyDefendStreak()
    {
        wildDefendConsecutiveUses = 0;
        wildDefendCurrentSuccess = defendFirstUseSuccess;
    }

    private bool RollEnemyFocusSuccess()
    {
        float chance = Mathf.Clamp01(wildFocusCurrentSuccess);
        bool ok = Rng01() <= chance;

        if (ok)
        {
            wildFocusConsecutiveUses++;
            float next = focusFirstUseSuccess * Mathf.Pow(focusRepeatMultiplier, wildFocusConsecutiveUses);
            wildFocusCurrentSuccess = Mathf.Max(focusMinSuccess, next);
        }
        else
        {
            wildFocusConsecutiveUses = 0;
            wildFocusCurrentSuccess = focusFirstUseSuccess;
        }

        return ok;
    }

    private void ResetEnemyFocusStreak()
    {
        wildFocusConsecutiveUses = 0;
        wildFocusCurrentSuccess = focusFirstUseSuccess;
    }

    private void ApplyWildDefendStance()
    {
        ResetEnemyFocusStreak();

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
            BattleLogger.Log($"{name} is defending.", LogScope.Battle);
            BattleLogger.Log($"{name} will reduce the next hit and convert it into a shield for the following round.", LogScope.Battle);
        }
        else
        {
            BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
            GrantFailedDefendCritBonus(BattleSide.Wild);
        }
    }

}
