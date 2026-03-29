using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AutoBattleHistoryPanelUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private AutoBattleHistoryRowUI rowPrefab;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private ScrollContentAutoSizer autoSizer;

    [Header("Behavior")]
    [SerializeField] private PanelId panelId = PanelId.AutoBattleHistory;
    [SerializeField] private FeatureId requiredFeature = FeatureId.IdleBattle_LogArchive;
    [SerializeField] private int maxEntriesToShow = 25;
    [SerializeField] private int maxLinesPerEntry = 8;
    [SerializeField] private int expandedRowsCount = 1;

    private readonly List<AutoBattleHistoryRowUI> _rows = new();

    void Awake()
    {
        if (closeButton) closeButton.onClick.AddListener(Close);
        if (refreshButton) refreshButton.onClick.AddListener(Rebuild);

        gameObject.SetActive(false);
    }

    void OnEnable()
    {
        Rebuild();
    }

    public void Open()
    {
        if (!IsUnlocked())
            return;

        if (UIManager.I != null)
            UIManager.I.Show(panelId);
        else
            gameObject.SetActive(true);
    }

    public void Close()
    {
        if (UIManager.I != null)
            UIManager.I.Hide(panelId);
        else
            gameObject.SetActive(false);
    }

    public void Toggle()
    {
        if (UIManager.I != null && UIManager.I.IsOpen(panelId))
            Close();
        else
            Open();
    }

    public void Rebuild()
    {
        var data = SaveManager.Data;
        if (data == null || data.autoBattleLogArchive == null || data.autoBattleLogArchive.Count == 0)
        {
            ClearRows();
            return;
        }

        if (listRoot != null && rowPrefab != null)
            RebuildRows(data.autoBattleLogArchive);

        ScrollToTop();
    }

    private void RebuildRows(List<AutoBattleLogEntry> list)
    {
        ClearRows();

        int start = Mathf.Max(0, list.Count - Mathf.Max(1, maxEntriesToShow));
        int shown = 0;

        for (int i = list.Count - 1; i >= start; i--)
        {
            bool expanded = shown < Mathf.Max(0, expandedRowsCount);
            var row = Instantiate(rowPrefab, listRoot);
            row.Set(list[i], expanded, maxLinesPerEntry);
            _rows.Add(row);
            shown++;
        }

        RefreshLayout();
    }

    private void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
                Destroy(_rows[i].gameObject);
        }

        _rows.Clear();
    }

    private void RefreshLayout()
    {
        if (autoSizer != null)
            autoSizer.Refresh(force: true);
    }

    private void ScrollToTop()
    {
        if (scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 1f;
        Canvas.ForceUpdateCanvases();
    }

    private bool IsUnlocked()
    {
        if (requiredFeature == FeatureId.None)
            return true;

        return FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(requiredFeature);
    }
}
