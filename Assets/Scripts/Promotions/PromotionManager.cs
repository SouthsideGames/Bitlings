using UnityEngine;

public sealed class PromotionManager : MonoBehaviour
{
    public static PromotionManager I { get; private set; }

    [Header("Data")]
    [SerializeField] private PromotionTableSO promotionTable;

    [Header("XP Award (Battle End)")]
    [Tooltip("Base Promotion XP gained on victory.")]
    [SerializeField] private int baseXpWin = 10;

    [Tooltip("Base Promotion XP gained on defeat/escape.")]
    [SerializeField] private int baseXpLoss = 2;

    [Tooltip("Extra XP per wild level (scaled and rounded).")]
    [SerializeField] private float xpPerWildLevel = 0.15f;

    [Tooltip("Clamp per-battle XP gain to avoid runaway.")]
    [SerializeField] private int maxXpPerBattle = 50;

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
    }

    private void OnEnable()
    {
        GameEvents.BattleFinished += OnBattleFinished;
    }

    private void OnDisable()
    {
        GameEvents.BattleFinished -= OnBattleFinished;
    }

    private void OnBattleFinished(BattleResult result)
    {
        // NOTE: BattleResult is a struct (value type) in this project, so it cannot be null.
        if (SaveManager.Data == null) return;

        // Only award promotion XP for real encounters (ignore null wildDef). If you want
        // training battles to grant XP later, remove this guard.
        if (result.wildDef == null) return;

        int gain = ComputeXpGain(result);
        if (gain <= 0) return;

        // ✅ UX change: no toast popups.
        AddPromotionXp(gain);
    }

    public int ComputeXpGain(BattleResult result)
    {
        int baseGain = result.victory ? baseXpWin : baseXpLoss;
        int lvl = Mathf.Max(1, result.wildLevel);
        int scaled = Mathf.RoundToInt(lvl * Mathf.Max(0f, xpPerWildLevel));
        int gain = Mathf.Max(0, baseGain + scaled);
        if (maxXpPerBattle > 0) gain = Mathf.Min(gain, maxXpPerBattle);
        return gain;
    }

    public void AddPromotionXp(int amount)
    {
        if (SaveManager.Data == null) return;
        if (amount <= 0) return;

        var pm = SaveManager.Data;

        int maxRank = GetMaxRank();
        int oldRank = Mathf.Clamp(pm.promotionRank, 1, maxRank);
        int oldXp = Mathf.Max(0, pm.promotionXP);

        // Hard cap: once at max rank, ignore further promotion XP gains.
        if (oldRank >= maxRank)
        {
            pm.promotionRank = maxRank;
            int maxFloor = Mathf.Max(0, GetTotalXpToReach(maxRank));
            pm.promotionXP = Mathf.Min(oldXp, maxFloor);
            SaveManager.Save();

            int xpThisRankMax = GetXpIntoCurrentRank(pm.promotionRank, pm.promotionXP);
            GameEvents.PromotionProgressChanged?.Invoke(pm.promotionRank, pm.promotionXP, xpThisRankMax, 0);
            return;
        }

        pm.promotionRank = oldRank;
        pm.promotionXP = oldXp + amount;

        bool rankedUp = TryProcessRankUps(out int newRank);

        // Persist
        SaveManager.Save();

        // Events
        int xpThisRank = GetXpIntoCurrentRank(pm.promotionRank, pm.promotionXP);
        int xpToNext = GetXpToNext(pm.promotionRank, pm.promotionXP);
        GameEvents.PromotionProgressChanged?.Invoke(pm.promotionRank, pm.promotionXP, xpThisRank, xpToNext);

        if (rankedUp)
        {
            GrantRankRewards(oldRank + 1, newRank);
            GameEvents.PromotionRankChanged?.Invoke(oldRank, newRank);
        }
    }

    private bool TryProcessRankUps(out int newRank)
    {
        newRank = SaveManager.Data != null ? SaveManager.Data.promotionRank : 1;
        if (SaveManager.Data == null) return false;

        var pm = SaveManager.Data;
        int rank = Mathf.Max(1, pm.promotionRank);
        int xp = Mathf.Max(0, pm.promotionXP);

        int maxRank = GetMaxRank();

        bool rankedUp = false;

        // If using table and it's sparse, fallback curve fills the gaps.
        while (rank < maxRank)
        {
            int nextRank = rank + 1;
            int req = GetTotalXpToReach(nextRank);
            if (req < 0) break;

            if (xp >= req)
            {
                rank = nextRank;
                rankedUp = true;
            }
            else
            {
                break;
            }
        }

        pm.promotionRank = rank;
        newRank = rank;
        return rankedUp;
    }

    public int GetMaxRank()
    {
        int maxRank = (promotionTable != null) ? promotionTable.MaxRank : 25;
        return Mathf.Max(1, maxRank);
    }

    // ─────────────────────────────────────────────────────────────
    // Threshold helpers
    // ─────────────────────────────────────────────────────────────

    public int GetTotalXpToReach(int rank)
    {
        rank = Mathf.Clamp(rank, 1, GetMaxRank());

        // Prefer table if it has an explicit number.
        if (promotionTable != null)
        {
            int v = promotionTable.GetTotalXpToReach(rank);
            if (v >= 0) return v;
        }

        if (rank == 1) return 0;

        int total = 0;
        for (int r = 2; r <= rank; r++)
        {
            int reqForThisStep = 50 + 20 * (r - 2);
            total += Mathf.Max(1, reqForThisStep);
        }
        return total;
    }

    public int GetXpIntoCurrentRank(int currentRank, int totalXp)
    {
        currentRank = Mathf.Max(1, currentRank);
        totalXp = Mathf.Max(0, totalXp);

        int curFloor = GetTotalXpToReach(currentRank);
        return Mathf.Max(0, totalXp - Mathf.Max(0, curFloor));
    }

    public int GetXpToNext(int currentRank, int totalXp)
    {
        currentRank = Mathf.Max(1, currentRank);
        totalXp = Mathf.Max(0, totalXp);

        int maxRank = (promotionTable != null) ? promotionTable.MaxRank : 25;
        maxRank = Mathf.Max(1, maxRank);

        if (currentRank >= maxRank) return 0;

        int nextReq = GetTotalXpToReach(currentRank + 1);
        int curFloor = GetTotalXpToReach(currentRank);

        int xpThisRank = Mathf.Max(0, totalXp - curFloor);
        int xpNeededThisRank = Mathf.Max(1, nextReq - curFloor);
        int remaining = Mathf.Max(0, xpNeededThisRank - xpThisRank);
        return remaining;
    }

    private void GrantRankRewards(int fromRank, int toRank)
    {
        if (promotionTable == null) return;

        for (int r = fromRank; r <= toRank; r++)
        {
            var entry = promotionTable.Get(r);
            if (entry == null || entry.rewards == null || entry.rewards.Count == 0) continue;

            ResourceManager.I.AddMany(entry.rewards);
        }
    }

    public string GetRankDisplayName(int rank)
    {
        rank = Mathf.Max(1, rank);

        if (promotionTable != null)
        {
            var e = promotionTable.Get(rank);
            if (e != null && !string.IsNullOrEmpty(e.displayName))
                return e.displayName; // ✅ name only
        }

        // Fallback titles (first option per rank from design).
        switch (rank)
        {
            case 1: return "Intern";
            case 2: return "Clerk";
            case 3: return "Technician";
            case 4: return "Coordinator";
            case 5: return "Supervisor";
            case 6: return "Auditor";
            case 7: return "Recruiter";
            case 8: return "Compliance Officer";
            case 9: return "Operations Lead";
            case 10: return "Manager";
            case 11: return "Project Lead";
            case 12: return "Department Head";
            case 13: return "Program Lead";
            case 14: return "Regional Manager";
            case 15: return "Director";
            case 16: return "Executive Manager";
            case 17: return "Division Head";
            case 18: return "Senior Director";
            case 19: return "Executive Director";
            case 20: return "Commissioner";
            case 21: return "Chief Commissioner";
            case 22: return "BRN Overseer";
            case 23: return "Executive Overseer";
            case 24: return "BRN Director General";
            case 25: return "BRN Supreme Director";
            default: return $"Rank {rank}";
        }
    }
}