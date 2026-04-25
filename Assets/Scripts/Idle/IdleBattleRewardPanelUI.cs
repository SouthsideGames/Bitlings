using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class IdleBattleRewardPanelUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private Button historyButton;

    [Header("Header Text")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text totalBattlesText;

    [Header("Encountered List (Fought)")]
    [SerializeField] private ScrollRect encounteredScrollRect;
    [SerializeField] private Transform encounteredListContent;
    [SerializeField] private RewardBitlingItem encounteredItemPrefab;

    [Header("Captured List (Captured)")]
    [SerializeField] private ScrollRect capturedScrollRect;
    [SerializeField] private Transform capturedListContent;
    [SerializeField] private RewardBitlingItem capturedItemPrefab;
    [SerializeField] private GameObject capturedSectionRoot;

    [Header("Captured Feature Gate")]
    [SerializeField] private FeatureId captureFeatureId = FeatureId.IdleBattle_OfflineCapture;

    [Header("History Feature Gate")]
    [SerializeField] private FeatureId historyFeatureId =  FeatureId.IdleBattle_LogArchive;

    [Header("Totals")]
    [SerializeField] private TMP_Text totalCreditsText;

    private MonsterLibrarySO monsterLibrary;
    private Action _onCollected;
    private bool _pendingCollection;

    private struct CreditGroup
    {
        public int count;
        public int credits;
        public bool anyNew;
        public bool anyPremium;
    }

    void Awake()
    {
        if (panel)
        {
            panel.alpha = 0;
            panel.interactable = false;
            panel.blocksRaycasts = false;
        }

        if (historyButton)
            historyButton.onClick.AddListener(OnHistoryClicked);


        gameObject.SetActive(false);

        monsterLibrary = MonsterLibraryLocator.Lib;
    }

    void OnEnable()
    {
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        UpdateHistoryButtonVisibility();
    }

    void OnDisable()
    {
        ConsumePendingCollection();

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

    void OnDestroy()
    {
        ConsumePendingCollection();

        if (historyButton)
            historyButton.onClick.RemoveListener(OnHistoryClicked);
    }

    public void Open(IdleBattleSummary summary, Action onCollected, string titleOverride = null)
    {
        _onCollected = onCollected;
        _pendingCollection = onCollected != null;

        if (titleText)
            titleText.text = string.IsNullOrEmpty(titleOverride) ? "While You Were Away…" : titleOverride;

        if (totalBattlesText)
            totalBattlesText.text = $"Bitlings Battled: {(summary != null ? summary.totalRifts : 0)}";

        if (totalCreditsText)
            totalCreditsText.text = $"Credits: {(summary != null ? summary.totalcredits : 0):N0}";

        ClearList(encounteredListContent);
        ClearList(capturedListContent);

        PopulateEncounteredList(summary != null ? summary.mergedLog : null);

        bool captureUnlocked = IsCaptureFeatureUnlocked();
        if (captureUnlocked)
            PopulateCapturedList(summary != null ? summary.capturedLog : null);

        ApplyCapturedSectionVisibility(summary, captureUnlocked);
        UpdateHistoryButtonVisibility();

        if (panel)
        {
            panel.alpha = 1;
            panel.interactable = true;
            panel.blocksRaycasts = true;
        }

        gameObject.SetActive(true);
    }

    private bool IsHistoryFeatureUnlocked()
    {
        if (historyFeatureId == FeatureId.None) return true;

        var fum = FeatureUnlockManager.I;
        if (fum == null) return false;

        return fum.IsUnlocked(historyFeatureId);
    }

    private void UpdateHistoryButtonVisibility()
    {
        if (!historyButton)
            return;

        bool unlocked = IsHistoryFeatureUnlocked();
        historyButton.gameObject.SetActive(unlocked);
        historyButton.interactable = unlocked;
    }

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == historyFeatureId)
            UpdateHistoryButtonVisibility();
    }

    private void OnHistoryClicked()
    {
        var ui = UIManager.I;
        if (ui == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevLog.Log("[IdleBattleRewardPanelUI] History click ignored: UIManager.I is null.");
#endif
            return;
        }

        ui.Show(PanelId.AutoBattleHistory);
        ui.Hide(PanelId.IdleBattleRewards);
    }


    private bool IsCaptureFeatureUnlocked()
    {
        if (captureFeatureId == FeatureId.None) return true;

        var fum = FeatureUnlockManager.I;
        if (fum == null) return false;

        return fum.IsUnlocked(captureFeatureId);
    }

    private void ApplyCapturedSectionVisibility(IdleBattleSummary summary, bool captureUnlocked)
    {
        if (capturedSectionRoot == null)
            return;

        bool hasCaptures = summary != null && summary.capturedLog != null && summary.capturedLog.Count > 0;
        capturedSectionRoot.SetActive(captureUnlocked && hasCaptures);
    }

    private void PopulateEncounteredList(List<IdleRiftLogEntry> log)
    {
        if (log == null || log.Count == 0 || encounteredItemPrefab == null || encounteredListContent == null)
            return;

        var groups = new Dictionary<string, CreditGroup>(64);

        for (int i = 0; i < log.Count; i++)
        {
            var e = log[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;

            var g = groups.TryGetValue(e.monsterId, out var existing) ? existing : default;
            g.count += e.count;
            g.credits += Mathf.Max(0, e.credits);
            g.anyPremium = g.anyPremium || e.premiumSeen;
            groups[e.monsterId] = g;
        }

        foreach (var kvp in groups)
        {
            string monsterId = kvp.Key;
            var g = kvp.Value;

            var def = FindMonster(monsterId);
            var icon = def ? MonsterNameFormatter.GetIcon(def, g.anyPremium, backIcon: false) : null;
            var name = def ? MonsterNameFormatter.Format(def, g.anyPremium) : monsterId;

            var item = Instantiate(encounteredItemPrefab, encounteredListContent);
            item.Set(icon, name, g.count, g.credits);
        }

        if (encounteredScrollRect) encounteredScrollRect.verticalNormalizedPosition = 1f;
    }

    private void PopulateCapturedList(List<IdleRiftLogEntry> log)
    {
        if (log == null || log.Count == 0 || capturedItemPrefab == null || capturedListContent == null)
            return;

        var groups = new Dictionary<string, CreditGroup>(32);

        for (int i = 0; i < log.Count; i++)
        {
            var e = log[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;

            bool isNew = !IsMonsterAlreadyOwnedOrUnlocked(e.monsterId);

            var g = groups.TryGetValue(e.monsterId, out var existing) ? existing : default;
            g.count += e.count;
            g.credits += Mathf.Max(0, e.credits);
            g.anyNew = g.anyNew || isNew;
            g.anyPremium = g.anyPremium || e.premiumSeen;
            groups[e.monsterId] = g;
        }

        foreach (var kvp in groups)
        {
            string monsterId = kvp.Key;
            var g = kvp.Value;

            var def = FindMonster(monsterId);
            var icon = def ? MonsterNameFormatter.GetIcon(def, g.anyPremium, backIcon: false) : null;
            var name = def ? MonsterNameFormatter.Format(def, g.anyPremium) : monsterId;

            var item = Instantiate(capturedItemPrefab, capturedListContent);
            item.Set(icon, name, g.count, g.anyNew, g.credits);
        }

        if (capturedScrollRect) capturedScrollRect.verticalNormalizedPosition = 1f;
    }

    private bool IsMonsterAlreadyOwnedOrUnlocked(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return true;
        var data = SaveManager.Data;
        if (data == null) return true;
        if (data.ownedIds != null) return data.ownedIds.Contains(monsterId);
        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
                if (data.owned[i] != null && data.owned[i].monsterId == monsterId) return true;
        }
        return false;
    }

    private void OnCollect()
    {
        ConsumePendingCollection();
        Close();
    }

    private void ConsumePendingCollection()
    {
        if (!_pendingCollection)
            return;

        _pendingCollection = false;

        var onCollected = _onCollected;
        _onCollected = null;
        onCollected?.Invoke();
    }

    private void Close()
    {
        if (panel)
        {
            panel.alpha = 0;
            panel.interactable = false;
            panel.blocksRaycasts = false;
        }

        gameObject.SetActive(false);

        ClearList(encounteredListContent);
        ClearList(capturedListContent);
    }

    private void ClearList(Transform content)
    {
        if (!content) return;

        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            if (child.GetComponent<RewardBitlingItem>() != null)
                Destroy(child.gameObject);
        }
    }

    private MonsterDataSO FindMonster(string monsterId)
    {
        if (!monsterLibrary || string.IsNullOrEmpty(monsterId) || monsterLibrary.monsters == null)
            return null;

        for (int i = 0; i < monsterLibrary.monsters.Length; i++)
        {
            var m = monsterLibrary.monsters[i];
            if (m != null && m.id == monsterId)
                return m;
        }

        return null;
    }
}
