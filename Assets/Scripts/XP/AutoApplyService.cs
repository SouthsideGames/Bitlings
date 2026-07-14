using System.Collections.Generic;
using UnityEngine;

public partial class AutoApplyService : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private LevelCostCurveSO levelCostCurve;

    [Tooltip("Optional. If left empty, will use MonsterLibraryLocator.GetById.")]
    [SerializeField] private MonsterLibrarySO monsterLibrary;

    [Tooltip("Optional. If left empty, will be auto-loaded from Resources.")]
    [SerializeField] private TokenEconomySO tokenEconomy;

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
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[AutoApplyService] SaveManager.Data is null.");
            #endif
            return;
        }

        if (levelCostCurve == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[AutoApplyService] levelCostCurve is not assigned.");
            #endif
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

        try
        {
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

                    // Level + award points
                    m.level = Mathf.Max(1, m.level + 1);
                    m.unspentStatPoints += Mathf.Max(0, pointsPerLevel);

                    // Auto-distribute new stat points based on personality
                    var statDelta = BuildPersonalityStatDelta(m, pointsPerLevel);
                    if (statDelta.hp != 0 || statDelta.atk != 0 || statDelta.def != 0 || statDelta.spd != 0)
                    {
                        MonsterStatApplier.Apply(m, statDelta);
                        m.unspentStatPoints = Mathf.Max(0, m.unspentStatPoints - pointsPerLevel);
                    }

                    // Defensive: keep premium fields consistent (mirrors XPManager behavior)
                    NormalizePremiumFields(m);

                    // Clamp HP to new max (mirrors XPManager.TryManualLevelUp)
                    ClampHpToNewMax(m);

                    changed = true;

                    // Signature: (string, int)
                    GameEvents.MonsterLeveled?.Invoke(m.monsterId, m.level);
                }
            }
        }
        finally
        {
            ResourceBank.EndBatch();
        }

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

    private void NormalizePremiumFields(OwnedMonsterData om)
    {
        if (om == null) return;
        if (om.premiumTier > 0 && !om.isPremium) om.isPremium = true;
        if (om.isPremium && om.premiumTier <= 0) om.premiumTier = 1;
        if (!om.isPremium && om.premiumTier < 0) om.premiumTier = 0;
    }

    private TrainingBonus BuildPersonalityStatDelta(OwnedMonsterData m, int points)
    {
        var econ = GetTokenEconomy();
        if (econ == null) return new TrainingBonus();

        var def = monsterLibrary != null
            ? monsterLibrary.GetById(m.monsterId)
            : MonsterLibraryLocator.GetById(m.monsterId);

        var group = (def?.Personality != null)
            ? def.Personality.group
            : MonsterPersonalitySO.PersonalityGroup.None;

        int allocHp = 0, allocAtk = 0, allocDef = 0, allocSpd = 0;

        for (int i = 0; i < points; i++)
        {
            switch (group)
            {
                case MonsterPersonalitySO.PersonalityGroup.Offensive:
                    // Pure attack focus
                    allocAtk++;
                    break;
                case MonsterPersonalitySO.PersonalityGroup.Defensive:
                    // Pure defense focus
                    allocDef++;
                    break;
                case MonsterPersonalitySO.PersonalityGroup.Evasive:
                    // Pure speed focus
                    allocSpd++;
                    break;
                case MonsterPersonalitySO.PersonalityGroup.Support:
                    // HP and defense
                    if (i % 2 == 0) allocHp++; else allocDef++;
                    break;
                case MonsterPersonalitySO.PersonalityGroup.Tactical:
                    // Even spread across atk, def, spd
                    if (i % 3 == 0) allocAtk++;
                    else if (i % 3 == 1) allocDef++;
                    else allocSpd++;
                    break;
                case MonsterPersonalitySO.PersonalityGroup.Reactive:
                    // Speed and attack
                    if (i % 2 == 0) allocSpd++; else allocAtk++;
                    break;
                default:
                    // None / Chaotic: balanced HP, ATK, DEF
                    if (i % 3 == 0) allocHp++;
                    else if (i % 3 == 1) allocAtk++;
                    else allocDef++;
                    break;
            }
        }

        return new TrainingBonus
        {
            hp  = allocHp  * econ.hpPerCore,
            atk = allocAtk * econ.atkPerCore,
            def = allocDef * econ.defPerCore,
            spd = allocSpd * econ.spdPerCore
        };
    }

    private TokenEconomySO GetTokenEconomy()
    {
        if (tokenEconomy) return tokenEconomy;

        // Direct load by known path (Assets/Resources/TokenEconomy.asset) — the
        // empty-path LoadAll scanned the entire Resources tree on the main thread.
        tokenEconomy = Resources.Load<TokenEconomySO>("TokenEconomy");
        if (tokenEconomy) return tokenEconomy;

        var all = Resources.LoadAll<TokenEconomySO>("");
        if (all != null && all.Length > 0) { tokenEconomy = all[0]; return tokenEconomy; }
        return null;
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
            SaveManager.SetMonsterHP(om, totalMaxHP, stampLastHpUnix: false, save: false, fireEvents: false);
    }
}
