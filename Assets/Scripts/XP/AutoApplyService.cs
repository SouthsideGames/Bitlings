using System.Collections.Generic;
using UnityEngine;

public partial class AutoApplyService : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private LevelCostCurveSO levelCostCurve;

    [Tooltip("Optional. If left empty, will use MonsterLibraryLocator.GetById.")]
    [SerializeField] private MonsterLibrarySO monsterLibrary;

    [Tooltip("How many unspent stat points are gained per level (matches StatBucketPanelUI default).")]
    [SerializeField, Min(1)] private int pointsPerLevel = 3;

    [Tooltip("Maximum monsters that can be auto-applied per press.")]
    [SerializeField] private int autoApplyCap = 3;

    [Tooltip("Safety cap to prevent runaway loops if data is bad.")]
    [SerializeField] private int safetyLevelOps = 2000;

    void OnEnable()
    {
        GameEvents.AutoApplyRequested += HandleAutoApplyRequested;
    }

    void OnDisable()
    {
        GameEvents.AutoApplyRequested -= HandleAutoApplyRequested;
    }

    private void HandleAutoApplyRequested()
    {
        ApplyAllAutoSelectedEvenSplit();
    }

    /// <summary>
    /// Uses ALL available Growth Cores and applies them across up to autoApplyCap monsters
    /// that have autoApply enabled. Cores are split evenly between selected monsters:
    /// budgetPerMonster = totalCores / selectedCount, with remainder distributed 1-by-1.
    ///
    /// If autoApplyTargetLevel <= 0, it is treated as "no cap" (level as far as budget allows).
    ///
    /// IMPORTANT: This path also awards unspentStatPoints per level (pointsPerLevel),
    /// matching normal leveling behavior.
    /// </summary>
    public void ApplyAllAutoSelectedEvenSplit()
    {
        var data = SaveManager.Data;
        if (data == null)
        {
            Debug.LogWarning("[AutoApplyService] SaveManager.Data is null.");
            return;
        }

        if (levelCostCurve == null)
        {
            Debug.LogWarning("[AutoApplyService] levelCostCurve is not assigned.");
            return;
        }

        int totalCores = ResourceBank.Get(ResourceType.GrowthCore);
        if (totalCores <= 0)
        {
            GameEvents.RaiseToast("No Growth Cores to apply.");
            return;
        }

        // 1) Gather selected autos (cap at autoApplyCap)
        var all = data.GetAllOwnedMonsters(includeTeam: true);
        if (all == null || all.Count == 0)
        {
            GameEvents.RaiseToast("No monsters found.");
            return;
        }

        List<OwnedMonsterData> selected = new List<OwnedMonsterData>(autoApplyCap);
        for (int i = 0; i < all.Count; i++)
        {
            var m = all[i];
            if (m == null) continue;
            if (!m.autoApply) continue;

            selected.Add(m);
            if (selected.Count >= autoApplyCap) break;
        }

        if (selected.Count == 0)
        {
            GameEvents.RaiseToast("No Auto Apply monsters selected.");
            return;
        }

        // 2) Split budgets (even split)
        int count = selected.Count;
        int baseBudget = totalCores / count;
        int remainder = totalCores % count;

        if (baseBudget <= 0 && remainder <= 0)
        {
            GameEvents.RaiseToast("Not enough Growth Cores.");
            return;
        }

        int[] budgets = new int[count];
        for (int i = 0; i < count; i++)
            budgets[i] = baseBudget + (i < remainder ? 1 : 0);

        // 3) Spend + level within budget per monster (batched)
        ResourceBank.BeginBatch();

        bool changed = false;
        int ops = 0;

        for (int i = 0; i < count; i++)
        {
            var m = selected[i];
            int budget = budgets[i];

            // targetLevel: 0/negative means no cap
            int targetLevel = (m.autoApplyTargetLevel <= 0) ? int.MaxValue : m.autoApplyTargetLevel;

            while (budget > 0 && m.level < targetLevel)
            {
                if (ops++ > safetyLevelOps) break;

                int cost = levelCostCurve.CoresToNextLevel(m.level);
                if (cost <= 0) break;

                if (budget < cost) break;

                // Spend from the bank (batched, no spam)
                if (!ResourceBank.TrySpend(ResourceType.GrowthCore, cost))
                    break;

                budget -= cost;

                // Level + award points (THIS is what you were missing)
                m.level = Mathf.Max(1, m.level + 1);
                m.unspentStatPoints += Mathf.Max(0, pointsPerLevel);

                // Defensive: keep shiny fields consistent (mirrors XPManager behavior)
                NormalizeShinyFields(m);

                // Clamp HP to new max (mirrors XPManager.TryManualLevelUp)
                ClampHpToNewMax(m);

                changed = true;

                // Signature: (string, int)
                GameEvents.MonsterLeveled?.Invoke(m.monsterId, m.level);
            }
        }

        ResourceBank.EndBatch();

        if (changed)
        {
            SaveManager.Save();
            GameEvents.OnOwnedMonstersChanged?.Invoke();
            GameEvents.OnTeamChanged?.Invoke();
        }
        else
        {
            GameEvents.RaiseToast("No eligible Auto Apply upgrades (need more Growth Cores).");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers (mirrors XPManager behavior)
    // ─────────────────────────────────────────────────────────────

    private void NormalizeShinyFields(OwnedMonsterData om)
    {
        if (om == null) return;
        if (om.shinyTier > 0 && !om.isShiny) om.isShiny = true;
        if (om.isShiny && om.shinyTier <= 0) om.shinyTier = 1;
        if (!om.isShiny && om.shinyTier < 0) om.shinyTier = 0;
    }

    private void ClampHpToNewMax(OwnedMonsterData om)
    {
        if (om == null || string.IsNullOrEmpty(om.monsterId)) return;

        MonsterDataSO def = null;
        if (monsterLibrary != null)
            def = monsterLibrary.GetById(om.monsterId);
        else
            def = MonsterLibraryLocator.GetById(om.monsterId);

        if (!def) return;

        int totalMaxHP = HealingService.CalcMaxHP(def, om.level, includeTraining: true, includeTitles: false);

        if (om.currentHP > totalMaxHP)
            SaveManager.SetMonsterHP(om, om.currentHP, stampLastHpUnix: false, save: false, fireEvents: false);
    }
}
