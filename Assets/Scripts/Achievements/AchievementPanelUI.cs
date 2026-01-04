using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class AchievementPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private AchievementItemUI itemPrefab;

    [Header("Top Summary")]
    [SerializeField] private Slider completionSlider;
    [SerializeField] private TextMeshProUGUI completionLabel;

    [Header("Optional")]
    [SerializeField] private bool stableRandomTieBreak = true;

    private readonly List<AchievementItemUI> _items = new List<AchievementItemUI>();

    private void OnEnable()
    {
        SaveManager.LoadOrCreate();
        if (AchievementManager.I == null) return;

        // When player opens panel, clear "New!" badges for unlocked achievements.
        AchievementManager.I.MarkAllUnlockedAsSeen();

        Rebuild();
    }

    public void Rebuild()
    {
        if (AchievementManager.I == null) return;

        var entries = AchievementManager.I.GetAllEntries();
        if (entries == null) return;

        ClearItems();

        // Sort: incomplete closest-to-completion at top, completed bottom.
        var incomplete = new List<AchievementEntrySO>();
        var complete = new List<AchievementEntrySO>();

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;

            var p = AchievementManager.I.GetProgress(e.id);
            bool unlocked = p != null && p.unlocked;

            if (unlocked) complete.Add(e);
            else incomplete.Add(e);
        }

        incomplete.Sort((a, b) =>
        {
            float ra = ProgressRatio(a);
            float rb = ProgressRatio(b);

            int cmp = rb.CompareTo(ra); // descending
            if (cmp != 0) return cmp;

            // tie-break
            if (!stableRandomTieBreak) return UnityEngine.Random.Range(-1, 2);

            int ha = StableHash(SaveManager.Data.playerId + "|" + a.id);
            int hb = StableHash(SaveManager.Data.playerId + "|" + b.id);
            return ha.CompareTo(hb);
        });

        // Completed at bottom; can keep by unlocked time or stable ID order.
        complete.Sort((a, b) =>
        {
            var pa = AchievementManager.I.GetProgress(a.id);
            var pb = AchievementManager.I.GetProgress(b.id);
            long ta = pa != null ? pa.unlockedUnix : 0;
            long tb = pb != null ? pb.unlockedUnix : 0;
            // older first
            int cmp = ta.CompareTo(tb);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.id, b.id);
        });

        int unlockedCount = 0;
        int totalCount = 0;

        void Spawn(AchievementEntrySO e)
        {
            totalCount++;
            var p = AchievementManager.I.GetProgress(e.id);
            if (p != null && p.unlocked) unlockedCount++;

            var it = Instantiate(itemPrefab, contentRoot);
            it.Bind(e, p);
            _items.Add(it);
        }

        for (int i = 0; i < incomplete.Count; i++) Spawn(incomplete[i]);
        for (int i = 0; i < complete.Count; i++) Spawn(complete[i]);

        UpdateCompletion(unlockedCount, totalCount);
    }

    private void UpdateCompletion(int unlocked, int total)
    {
        if (completionSlider)
        {
            completionSlider.minValue = 0;
            completionSlider.maxValue = Mathf.Max(1, total);
            completionSlider.value = unlocked;

            // Optional animate
            LeanTween.cancel(completionSlider.gameObject);
            float from = completionSlider.value;
            completionSlider.value = from;
            LeanTween.value(completionSlider.gameObject, from, unlocked, 0.25f)
                .setOnUpdate(v => completionSlider.value = v);
        }

        if (completionLabel)
            completionLabel.text = $"{unlocked}/{Mathf.Max(1, total)} Unlocked";
    }

    private float ProgressRatio(AchievementEntrySO e)
    {
        if (e == null) return 0f;
        var p = AchievementManager.I.GetProgress(e.id);
        int v = p != null ? p.value : 0;
        int g = Mathf.Max(1, e.goal);
        return Mathf.Clamp01(v / (float)g);
    }

    private void ClearItems()
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i]) Destroy(_items[i].gameObject);

        _items.Clear();
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            for (int i = 0; i < s.Length; i++)
                h = h * 31 + s[i];
            return h;
        }
    }
}
