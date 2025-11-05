using System.Collections.Generic;
using UnityEngine;

public class AutoApplyService : MonoBehaviour
{
    public static AutoApplyService I { get; private set; }

    [Header("Refs")]
    [SerializeField] private PlayerManager playerManager;      // drag in
    [SerializeField] private BucketLibrarySO bucketLibrary;    // drag in
    [SerializeField] private TokenEconomySO tokenEconomy;        // drag in (or leave null to auto-load)
    [SerializeField] private LevelCostCurveSO levelCostCurve;  // drag in

    [Header("Options")]
    [SerializeField, Min(0.05f)] private float pollSeconds = 0.5f;
    [SerializeField] private int autoApplyCap = 3;

    float _t;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        if (!tokenEconomy) tokenEconomy = TokenEconomySO.Load();
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (_t < pollSeconds) return;
        _t = 0f;
        TickAutoApply();
    }

    void TickAutoApply()
    {
        if (playerManager == null || levelCostCurve == null) return;
        var monsters = GetAllOwnedMonsters();
        if (monsters == null) return;

        // Respect global cap (only the first 3 flagged get processed)
        int processed = 0;

        foreach (var m in monsters)
        {
            if (m == null || !m.autoApply) continue;
            if (processed >= autoApplyCap) break;

            // guards
            if (m.autoApplyTargetLevel <= 0 || m.level >= m.autoApplyTargetLevel) continue;

            int cores = GetGrowthCores();
            int need = levelCostCurve.CoresToNextLevel(m.level);
            if (cores < need) continue;

            // choose bucket (last used or default)
            var bucket = bucketLibrary ? bucketLibrary.GetById(m.lastBucketId, bucketLibrary.DefaultBucket())
                                       : null;
            if (bucket == null || tokenEconomy == null) continue;

            // Spend cores -> distribute 1 level worth of "points"
            if (!TrySpendGrowthCores(need)) continue;

            var delta = LevelUpCalculator.DistributeByWeights(need, bucket, tokenEconomy);
            MonsterStatApplier.Apply(m, delta);
            m.level = Mathf.Max(1, m.level + 1);

            // toast (optional): hook your UI here
            // BattleLogger or lightweight toaster: "Auto-applied to {name} (Lv {old}->{new})"

            processed++;
        }
    }

    // --- Resource helpers (adjust if your ResourceManager API differs) ---

    int GetGrowthCores()
    {
        // If you have a getter, use it; many projects store counts in ResourceManager
        // Here we try a common pattern: ResourceManager.I?.Get(ResourceType)
        var rm = ResourceManager.I;
        if (rm == null) return 0;
        return rm.Get(ResourceType.GrowthCores); // ensure your ResourceManager exposes Get(...)
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

    // --- Owned monsters source ---

    List<OwnedMonsterData> GetAllOwnedMonsters()
    {
        // Adjust to your actual accessor; many builds keep it on PlayerManager
        return playerManager?.GetAllOwnedMonsters();
    }
}
