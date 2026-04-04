using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Promotions/Promotion Table", fileName = "PromotionTable")]
public sealed class PromotionTableSO : ScriptableObject
{
    [Serializable]
    public sealed class RankEntry
    {
        [Min(1)] public int rank = 1;

        [Tooltip("Total cumulative XP required to reach this rank (Rank 1 usually = 0).")]
        [Min(0)] public int totalXpToReach = 0;

        [Tooltip("Optional display name for the rank.")]
        public string displayName;

        [TextArea(1, 4)]
        [Tooltip("Optional short reward summary shown on the dossier rank list.")]
        public string rewardSummary;
    }

    [Header("Config")]
    [SerializeField, Min(1)] private int maxRank = 20;

    [Header("Rank Entries")]
    [Tooltip("If empty, PromotionManager uses a fallback XP curve.")]
    [SerializeField] private List<RankEntry> ranks = new();

    private Dictionary<int, RankEntry> _map;

    public int MaxRank => Mathf.Max(1, maxRank);

    public RankEntry Get(int rank)
    {
        EnsureMap();
        _map.TryGetValue(rank, out var e);
        return e;
    }

    public int GetTotalXpToReach(int rank)
    {
        var e = Get(rank);
        if (e != null) return Mathf.Max(0, e.totalXpToReach);
        return -1;
    }

    private void EnsureMap()
    {
        if (_map != null) return;
        _map = new Dictionary<int, RankEntry>();

        for (int i = 0; i < ranks.Count; i++)
        {
            var e = ranks[i];
            if (e == null) continue;
            if (e.rank <= 0) continue;
            _map[e.rank] = e;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _map = null;
        if (maxRank < 1) maxRank = 1;
    }
#endif
}
