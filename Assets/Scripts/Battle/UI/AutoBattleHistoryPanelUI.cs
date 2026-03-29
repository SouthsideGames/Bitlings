using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AutoBattleHistoryPaneUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Transform listRoot;
    [SerializeField] private AutoBattleHistoryRowUI rowPrefab;
    [SerializeField] private TextMeshProUGUI contentLabel;
    [SerializeField] private TextMeshProUGUI emptyLabel;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private ScrollContentAutoSizer autoSizer;

    [Header("Behavior")]
    [SerializeField] private FeatureId requiredFeature = FeatureId.IdleBattle_LogArchive;
    [SerializeField] private int maxEntriesToShow = 25;
    [SerializeField] private int maxLinesPerEntry = 8;
    [SerializeField] private int expandedRowsCount = 1;

    private readonly List<AutoBattleHistoryRowUI> _rows = new();

    void Awake()
    {
        if (closeButton) closeButton.onClick.AddListener(Close);
        if (refreshButton) refreshButton.onClick.AddListener(Rebuild);

        if (root) root.SetActive(false);
    }

    public void Open()
    {
        if (!IsUnlocked())
            return;

        if (!root) return;
        root.SetActive(true);
        Rebuild();
    }

    public void Close()
    {
        if (!root) return;
        root.SetActive(false);
    }

    public void Toggle()
    {
        if (!root) return;
        if (root.activeSelf) Close();
        else Open();
    }

    public void Rebuild()
    {
        var data = SaveManager.Data;
        if (data == null || data.autoBattleLogArchive == null || data.autoBattleLogArchive.Count == 0)
        {
            ShowEmpty("No auto-battle history yet.");
            ClearRows();
            return;
        }

        bool canRenderRows = listRoot != null && rowPrefab != null;
        if (canRenderRows)
            RebuildRows(data.autoBattleLogArchive);
        else
            RebuildTextFallback(data.autoBattleLogArchive);

        SetEmptyVisible(false);
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

    private void RebuildTextFallback(List<AutoBattleLogEntry> list)
    {
        if (contentLabel == null)
            return;

        int shown = 0;
        int start = Mathf.Max(0, list.Count - Mathf.Max(1, maxEntriesToShow));

        var sb = new StringBuilder(4096);

        for (int i = list.Count - 1; i >= start; i--)
        {
            var e = list[i];
            AppendEntry(sb, e);
            shown++;

            if (shown < maxEntriesToShow && i > start)
                sb.AppendLine();
        }

        contentLabel.text = sb.ToString();

        if (emptyLabel != null)
            emptyLabel.gameObject.SetActive(false);
    }

    private void ShowEmpty(string message)
    {
        if (emptyLabel != null)
        {
            emptyLabel.text = message;
            emptyLabel.gameObject.SetActive(true);
        }

        if (contentLabel != null)
            contentLabel.text = message;
    }

    private void SetEmptyVisible(bool visible)
    {
        if (emptyLabel != null)
            emptyLabel.gameObject.SetActive(visible);
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

    private void AppendEntry(StringBuilder sb, AutoBattleLogEntry e)
    {
        string time = FormatUnix(e.unix);
        string outcome = e.victory ? "Victory" : (e.escaped ? "Escaped" : "Defeat");
        string name = string.IsNullOrEmpty(e.opponentName) ? "Unknown" : e.opponentName;

        sb.Append("[").Append(time).Append("] ")
          .Append(outcome)
          .Append(" vs ")
          .Append(name)
          .Append(" (Lv ")
          .Append(Mathf.Max(1, e.opponentLevel))
          .AppendLine(")");

        if (e.lines == null || e.lines.Count == 0)
            return;

        int lineCount = Mathf.Min(Mathf.Max(1, maxLinesPerEntry), e.lines.Count);
        for (int i = 0; i < lineCount; i++)
        {
            var line = e.lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            sb.Append("  - ").AppendLine(line);
        }

        if (lineCount < e.lines.Count)
            sb.AppendLine("  - ...");
    }

    private static string FormatUnix(long unix)
    {
        if (unix <= 0)
            return "Unknown time";

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            return "Unknown time";
        }
    }

    private bool IsUnlocked()
    {
        if (requiredFeature == FeatureId.None)
            return true;

        return FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(requiredFeature);
    }
}
