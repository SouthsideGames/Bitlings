using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class IdleBattleRewardPanelUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private Button collectButton;

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

    [Header("Totals")]
    [SerializeField] private TMP_Text totalCreditsText;

    private MonsterLibrarySO monsterLibrary;
    private Action _onCollected;

    private struct CreditGroup
    {
        public int count;
        public int credits;
        public bool anyNew;
    }

    void Awake()
    {
        if (panel)
        {
            panel.alpha = 0;
            panel.interactable = false;
            panel.blocksRaycasts = false;
        }

        if (collectButton)
            collectButton.onClick.AddListener(OnCollect);

        gameObject.SetActive(false);

        monsterLibrary = MonsterLibraryLocator.Lib;
    }

    public void Open(IdleBattleSummary summary, Action onCollected, string titleOverride = null)
    {
        _onCollected = onCollected;

        if (titleText)
            titleText.text = string.IsNullOrEmpty(titleOverride) ? "While You Were Away…" : titleOverride;

        if (totalBattlesText)
            totalBattlesText.text = $"Bitlings Battled: {(summary != null ? summary.totalEncounters : 0)}";

        if (totalCreditsText)
            totalCreditsText.text = $"Credits: {(summary != null ? summary.totalcredits : 0):N0}";

        ClearList(encounteredListContent);
        ClearList(capturedListContent);

        PopulateEncounteredList(summary != null ? summary.mergedLog : null);

        bool captureUnlocked = IsCaptureFeatureUnlocked();
        if (captureUnlocked)
            PopulateCapturedList(summary != null ? summary.capturedLog : null);

        ApplyCapturedSectionVisibility(summary, captureUnlocked);

        if (panel)
        {
            panel.alpha = 1;
            panel.interactable = true;
            panel.blocksRaycasts = true;
        }

        gameObject.SetActive(true);
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

    private void PopulateEncounteredList(List<IdleEncounterLogEntry> log)
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
            groups[e.monsterId] = g;
        }

        foreach (var kvp in groups)
        {
            string monsterId = kvp.Key;
            var g = kvp.Value;

            var def = FindMonster(monsterId);
            var icon = def ? def.icon : null;
            var name = def ? def.displayName : monsterId;

            var item = Instantiate(encounteredItemPrefab, encounteredListContent);
            item.Set(icon, name, g.count, g.credits);
        }

        if (encounteredScrollRect) encounteredScrollRect.verticalNormalizedPosition = 1f;
    }

    private void PopulateCapturedList(List<IdleEncounterLogEntry> log)
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
            groups[e.monsterId] = g;
        }

        foreach (var kvp in groups)
        {
            string monsterId = kvp.Key;
            var g = kvp.Value;

            var def = FindMonster(monsterId);
            var icon = def ? def.icon : null;
            var name = def ? def.displayName : monsterId;

            var item = Instantiate(capturedItemPrefab, capturedListContent);
            item.Set(icon, name, g.count, g.anyNew, g.credits);
        }

        if (capturedScrollRect) capturedScrollRect.verticalNormalizedPosition = 1f;
    }

    private bool IsMonsterAlreadyOwnedOrUnlocked(string monsterId)
    {
        try
        {
            var saveMgrType = Type.GetType("SaveManager");
            object saveMgr = null;

            if (saveMgrType != null)
            {
                var instProp = saveMgrType.GetProperty("I", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instProp != null) saveMgr = instProp.GetValue(null, null);
            }

            if (saveMgr != null)
            {
                if (TryInvokeBool(saveMgr, "IsMonsterUnlocked", monsterId, out bool b1)) return b1;
                if (TryInvokeBool(saveMgr, "IsUnlocked", monsterId, out bool b2)) return b2;
                if (TryInvokeBool(saveMgr, "HasMonsterUnlocked", monsterId, out bool b3)) return b3;
                if (TryInvokeBool(saveMgr, "HasUnlockedMonster", monsterId, out bool b4)) return b4;

                if (TryInvokeBool(saveMgr, "HasOwnedMonster", monsterId, out bool b5)) return b5;
                if (TryInvokeBool(saveMgr, "HasMonster", monsterId, out bool b6)) return b6;
                if (TryInvokeBool(saveMgr, "OwnsMonster", monsterId, out bool b7)) return b7;
            }
        }
        catch
        {
            // ignore
        }

        return true; // Conservative fallback: don't show NEW if unsure
    }

    private bool TryInvokeBool(object target, string methodName, string arg, out bool result)
    {
        result = false;
        if (target == null) return false;

        var t = target.GetType();
        var mi = t.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (mi == null) return false;

        var ps = mi.GetParameters();
        if (ps == null || ps.Length != 1) return false;
        if (ps[0].ParameterType != typeof(string)) return false;
        if (mi.ReturnType != typeof(bool)) return false;

        result = (bool)mi.Invoke(target, new object[] { arg });
        return true;
    }

    private void OnCollect()
    {
        _onCollected?.Invoke();
        Close();
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
