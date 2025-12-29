using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoApplyService : MonoBehaviour
{
    public static AutoApplyService I { get; private set; }

    [Header("Refs")]
    [SerializeField] private PlayerManager playerManager;     
    [SerializeField] private BucketLibrarySO bucketLibrary;    
    [SerializeField] private TokenEconomySO tokenEconomy;      
    [SerializeField] private LevelCostCurveSO levelCostCurve;  

    [Header("Options")]
    [SerializeField] private int autoApplyCap = 3;

    [Header("Debounce")]
    [Tooltip("Small delay to coalesce multiple triggers (resource gain + team changed, etc.).")]
    [SerializeField, Min(0f)] private float debounceSeconds = 0.10f;

    bool _dirty;
    bool _scheduled;
    float _nextEarliestRun;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        // Fallbacks (mirrors your existing script)
        if (playerManager == null)
            playerManager = SaveManager.Data;

        if (tokenEconomy == null)
            tokenEconomy = Resources.Load<TokenEconomySO>("TokenEconomy");

        if (bucketLibrary == null)
            bucketLibrary = Resources.Load<BucketLibrarySO>("BucketLibrary");
    }

    void OnEnable()
    {
        GameEvents.OnTeamChanged += HandleTeamChanged;
        GameEvents.MonsterLeveled += HandleMonsterLeveled;

        GameEvents.ResourceAdded += HandleResourceAdded;
        GameEvents.OnResourcesChanged += HandleResourcesChanged;

        RequestAutoApply();
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged -= HandleTeamChanged;
        GameEvents.MonsterLeveled -= HandleMonsterLeveled;

        GameEvents.ResourceAdded -= HandleResourceAdded;
        GameEvents.OnResourcesChanged -= HandleResourcesChanged;
    }

    void HandleTeamChanged() => RequestAutoApply();

    void HandleMonsterLeveled(string ownedKey, int newLevel)
    {
        // A level-up often changes affordability/targets; re-check.
        RequestAutoApply();
    }

    void HandleResourceAdded(ResourceType type, int amount)
    {
        // Only care about GrowthCore positive gains (auto-apply becomes possible).
        if (type == ResourceType.GrowthCore && amount > 0)
            RequestAutoApply();
    }

    void HandleResourcesChanged()
    {
        // Broad fallback. We debounce so this won't be noisy.
        RequestAutoApply();
    }

    /// <summary>
    /// Public hook for UI/scripts to request a run after toggling autoApply or target level.
    /// </summary>
    public void RequestAutoApply()
    {
        _dirty = true;

        float now = Time.unscaledTime;
        _nextEarliestRun = Mathf.Max(_nextEarliestRun, now + debounceSeconds);

        if (!_scheduled)
        {
            _scheduled = true;
            StartCoroutine(RunWhenReady());
        }
    }

    IEnumerator RunWhenReady()
    {
        // Always wait at least one frame so multiple events in a single frame coalesce.
        yield return null;

        while (true)
        {
            if (!_dirty)
            {
                _scheduled = false;
                yield break;
            }

            float now = Time.unscaledTime;
            if (now < _nextEarliestRun)
            {
                yield return null;
                continue;
            }

            _dirty = false;
            TickAutoApply();

            // If TickAutoApply indirectly caused another RequestAutoApply, loop again.
            yield return null;
        }
    }

    void TickAutoApply()
    {
        var data = playerManager ?? SaveManager.Data;
        if (data == null || levelCostCurve == null)
            return;

        var monsters = GetAllOwnedMonsters();
        if (monsters == null || monsters.Count == 0)
            return;

        int cores = GetGrowthCores();
        if (cores <= 0)
            return;

        int processed = 0;
        bool changed = false;

        ResourceBank.BeginBatch();

        try
        {
            foreach (var m in monsters)
            {
                if (m == null || !m.autoApply) continue;
                if (processed >= autoApplyCap) break;

                // guards
                if (m.autoApplyTargetLevel <= 0) continue;
                if (m.level >= m.autoApplyTargetLevel) continue;

                int need = Mathf.Max(1, levelCostCurve.CoresToNextLevel(Mathf.Max(1, m.level)));

                // Re-check current cores (can drop as we spend in this loop).
                cores = GetGrowthCores();
                if (cores < need) continue;

                var bucket = bucketLibrary
                    ? bucketLibrary.GetById(m.lastBucketId, bucketLibrary.DefaultBucket())
                    : null;

                if (bucket == null || tokenEconomy == null) continue;

                // Spend cores
                if (!TrySpendGrowthCores(need)) continue;

                // Apply training delta + level up
                var delta = LevelUpCalculator.DistributeByWeights(need, bucket, tokenEconomy);
                MonsterStatApplier.Apply(m, delta);
                m.level = Mathf.Max(1, m.level + 1);

                // Clamp HP to new max
                var def = MonsterLibraryLocator.GetById(m.monsterId);
                if (def != null)
                {
                    int newMaxHP = Mathf.RoundToInt(BattleCalc.CalcHP(def, m.level));
                    m.currentHP = Mathf.Clamp(m.currentHP, 0, newMaxHP);
                }

                processed++;
                changed = true;
            }
        }
        finally
        {
            ResourceBank.EndBatch();
        }

        if (changed)
        {
            SaveManager.Save();
            GameEvents.OnTeamChanged?.Invoke();
        }
    }

    int GetGrowthCores()
    {
        if (ResourceManager.I != null)
            return ResourceManager.I.Get(ResourceType.GrowthCore);

        return ResourceBank.Get(ResourceType.GrowthCore);
    }

    bool TrySpendGrowthCores(int amount)
    {
        if (amount <= 0) return true;
        return ResourceBank.TrySpend(ResourceType.GrowthCore, amount);
    }

    List<OwnedMonsterData> GetAllOwnedMonsters()
    {
        var data = playerManager ?? SaveManager.Data;
        if (data == null) return null;

        return data.GetAllOwnedMonsters(includeTeam: true);
    }
}
