using UnityEngine;

/// <summary>
/// Phase 5: Promotion XP + Rank progression (1–20).
///
/// - XP is awarded at battle end.
/// - Rank-ups are automatic once XP threshold is met.
/// - Uses PromotionTableSO if assigned; otherwise uses a fallback XP curve.
///
/// UI hooks:
/// - GameEvents.PromotionProgressChanged
/// - GameEvents.PromotionRankChanged
/// - GameEvents.ToastRequested ("XP gained" nudges player to Player Dossier > Ranks)
/// </summary>
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

        AddPromotionXp(gain, showToast: true);
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

    public void AddPromotionXp(int amount, bool showToast)
    {
        if (SaveManager.Data == null) return;
        if (amount <= 0) return;

        var pm = SaveManager.Data;

        int oldRank = Mathf.Max(1, pm.promotionRank);
        int oldXp = Mathf.Max(0, pm.promotionXP);

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
            GameEvents.PromotionRankChanged?.Invoke(oldRank, newRank);

        if (showToast)
        {
            if (rankedUp)
                GameEvents.RaiseToast($"Promotion XP +{amount}. Rank up! Check Player Dossier → Ranks.");
            else
                GameEvents.RaiseToast($"Promotion XP +{amount}. Check Player Dossier → Ranks.");
        }
    }

    private bool TryProcessRankUps(out int newRank)
    {
        newRank = SaveManager.Data != null ? SaveManager.Data.promotionRank : 1;
        if (SaveManager.Data == null) return false;

        var pm = SaveManager.Data;
        int rank = Mathf.Max(1, pm.promotionRank);
        int xp = Mathf.Max(0, pm.promotionXP);

        int maxRank = (promotionTable != null) ? promotionTable.MaxRank : 20;
        maxRank = Mathf.Max(1, maxRank);

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

    // ─────────────────────────────────────────────────────────────
    // Threshold helpers
    // ─────────────────────────────────────────────────────────────

    public int GetTotalXpToReach(int rank)
    {
        rank = Mathf.Max(1, rank);

        // Prefer table if it has an explicit number.
        if (promotionTable != null)
        {
            int v = promotionTable.GetTotalXpToReach(rank);
            if (v >= 0) return v;
        }

        // Fallback curve (Rank 1–20):
        // Rank 2 requires 50 XP, then each subsequent rank step requires +20 more than the prior step.
        // (Step requirements: 50, 70, 90, ... up to 410 for Rank 20.)
        // Total XP to reach rank N is sum of requirements for ranks 2..N.
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

        int maxRank = (promotionTable != null) ? promotionTable.MaxRank : 20;
        maxRank = Mathf.Max(1, maxRank);

        if (currentRank >= maxRank) return 0;

        int nextReq = GetTotalXpToReach(currentRank + 1);
        int curFloor = GetTotalXpToReach(currentRank);

        int xpThisRank = Mathf.Max(0, totalXp - curFloor);
        int xpNeededThisRank = Mathf.Max(1, nextReq - curFloor);
        int remaining = Mathf.Max(0, xpNeededThisRank - xpThisRank);
        return remaining;
    }
}
