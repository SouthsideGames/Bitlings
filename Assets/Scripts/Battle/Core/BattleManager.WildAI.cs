using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public partial class BattleManager : MonoBehaviour
{

    
    private bool ShouldShowWildTelegraphText()
    {
        // Manual: always show text. Auto: show only for first N turns.
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
            // Map to feedback action enum
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

        // Even without text, give the player a moment to register the icon.
        if (wildIntentTelegraphPause > 0f)
            yield return CoWaitUnscaled(wildIntentTelegraphPause);
    }




    private EnemyAction ChooseEnemyAction()
    {
        if (!wildDef || wildMaxHP <= 0.01f)
            return EnemyAction.Attack;

        float hpRatio = Mathf.Clamp01(wildHP / Mathf.Max(1f, wildMaxHP));
        BattleAction action = BattleAction.Attack;

        if (wildDef.Personality != null)
        {
            var ctx = new PersonalityContext
            {
                selfHpRatio = hpRatio,
                hasSuperEffectiveMove = false,
                isBadlyMatched = false,
                turnNumber = Mathf.Max(1, _turnIndex + 1)
            };

            action = wildDef.Personality.ChooseAction(in ctx, _rng.EnemyRng);
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
            case BattleAction.Attack: return EnemyAction.Attack;
            case BattleAction.Defend: return EnemyAction.Defend;
            case BattleAction.Focus: return EnemyAction.Focus;
            case BattleAction.Run: return EnemyAction.Run;
            default: return Fallback();
        }
    }




    private float ComputeEnemyRunChance()
    {
        if (!wildDef || wildMaxHP <= 0.01f)
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
        if (wildPendingGuardShield <= 0.01f) return;

        string name = wildDef ? wildDef.displayName : "Foe";
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
            float next = defendFirstUseSuccess * Mathf.Pow(defendRepeatMultiplier, wildDefendConsecutiveUses);
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




    private void ApplyWildDefendStance()
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
            BattleLogger.Log($"{name} is defending.", LogScope.Battle);
            BattleLogger.Log($"{name} will reduce the next hit and convert it into a shield for the following round.", LogScope.Battle);
        }
        else
        {
            BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
        }
    }

}
