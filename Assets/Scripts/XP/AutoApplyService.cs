using System.Collections.Generic;
using UnityEngine;

public class AutoApplyService : MonoBehaviour
{
    public static AutoApplyService I { get; private set; }

    [Header("Refs")]
    [SerializeField] private PlayerManager playerManager;      // drag in
    [SerializeField] private BucketLibrarySO bucketLibrary;    // drag in
    [SerializeField] private TokenEconomySO tokenEconomy;      // drag in (or leave null to auto-load)
    [SerializeField] private LevelCostCurveSO levelCostCurve;  // drag in

    [Header("Options")]
    [SerializeField, Min(0.05f)] private float pollSeconds = 0.5f;
    [SerializeField] private int autoApplyCap = 3;

    float _timer;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        // Fallbacks
        if (playerManager == null)
            playerManager = SaveManager.Data;

        if (tokenEconomy == null)
            tokenEconomy = Resources.Load<TokenEconomySO>("TokenEconomy");

        if (bucketLibrary == null)
            bucketLibrary = Resources.Load<BucketLibrarySO>("BucketLibrary");
    }

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer >= pollSeconds)
        {
            _timer = 0f;
            TickAutoApply();
        }
    }

    void TickAutoApply()
    {
        // Need data + curve
        var data = playerManager ?? SaveManager.Data;
        if (data == null || levelCostCurve == null)
            return;

        var monsters = GetAllOwnedMonsters();
        if (monsters == null || monsters.Count == 0)
            return;

        int processed = 0;

        foreach (var m in monsters)
        {
            if (m == null || !m.autoApply) continue;
            if (processed >= autoApplyCap) break;

            // guards
            if (m.autoApplyTargetLevel <= 0 || m.level >= m.autoApplyTargetLevel) continue;

            int cores = GetGrowthCores();
            int need  = levelCostCurve.CoresToNextLevel(m.level);
            if (cores < need) continue;

            // choose bucket (last used or default)
            var bucket = bucketLibrary
                ? bucketLibrary.GetById(m.lastBucketId, bucketLibrary.DefaultBucket())
                : null;

            if (bucket == null || tokenEconomy == null) continue;

            // Spend cores -> distribute 1 level worth of "points"
            if (!TrySpendGrowthCores(need)) continue;

            // Distribute stats + level up
            var delta = LevelUpCalculator.DistributeByWeights(need, bucket, tokenEconomy);
            MonsterStatApplier.Apply(m, delta);
            m.level = Mathf.Max(1, m.level + 1);

            var def = MonsterLibraryLocator.GetById(m.monsterId);
            if (def != null)
            {
                int newMaxHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, m.level));
                m.currentHP   = Mathf.Clamp(m.currentHP, 0, newMaxHP);
            }

            processed++;
        }

        // If we actually changed anything, SAVE it.
        if (processed > 0)
        {
            SaveManager.Save();
            GameEvents.OnTeamChanged?.Invoke();
        }
    }

    // --- Resource helpers (adjust if your ResourceManager API differs) ---

    int GetGrowthCores()
    {
        var rm = ResourceManager.I;
        if (rm == null) return 0;
        return rm.Get(ResourceType.GrowthCores);
    }

    bool TrySpendGrowthCores(int amount)
    {
        var rm = ResourceManager.I;
        if (rm == null || amount <= 0) return false;
        int have = rm.Get(ResourceType.GrowthCores);
        if (have < amount) return false;
        rm.Add(ResourceType.GrowthCores, -amount);
        return true;
    }

    // --- Owned monsters source (real objects, no copies) ---

    List<OwnedMonsterData> GetAllOwnedMonsters()
    {
        var data = playerManager ?? SaveManager.Data;
        if (data == null) return null;

        return data.GetAllOwnedMonsters(includeTeam: true);
    }
}
