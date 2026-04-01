using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class IdleBattleHistoryRowUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI summaryLabel;
    [SerializeField] private TextMeshProUGUI detailsLabel;
    [SerializeField] private GameObject detailsRoot;
    [SerializeField] private ScrollRect detailsScrollRect;
    [SerializeField] private Button toggleButton;
    [SerializeField] private TextMeshProUGUI toggleLabel;

    [Header("Behavior")]
    [SerializeField] private int maxLinesWhenCollapsed = 5;

    [Header("Summary Colors")]
    [SerializeField] private string victoryHex = "#4FD27A";
    [SerializeField] private string defeatHex = "#FF7A7A";

    private AutoBattleLogEntry _entry;
    private bool _expanded;

    void Awake()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleExpanded);
    }

    public void Set(AutoBattleLogEntry entry, bool expandedByDefault, int collapsedLineLimit)
    {
        _entry = entry;
        _expanded = expandedByDefault;
        maxLinesWhenCollapsed = Mathf.Max(1, collapsedLineLimit);

        if (summaryLabel != null)
            summaryLabel.text = BuildSummary(entry);

        RebuildDetails();
        ApplyExpandedState();
    }

    public void ToggleExpanded()
    {
        _expanded = !_expanded;
        RebuildDetails();
        ApplyExpandedState();

        var parent = transform.parent as RectTransform;
        if (parent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);

        AudioManager.I.PlayClick();
    }

    private void ApplyExpandedState()
    {
        if (detailsRoot != null)
            detailsRoot.SetActive(_expanded);

        if (detailsScrollRect != null)
        {
            detailsScrollRect.vertical = _expanded;
            detailsScrollRect.verticalNormalizedPosition = 1f;
        }

        if (toggleLabel != null)
            toggleLabel.text = _expanded ? "Show Less" : "Show More";
    }

    private void RebuildDetails()
    {
        if (detailsLabel == null)
            return;

        detailsLabel.text = BuildDetails(_entry, _expanded ? int.MaxValue : Mathf.Max(1, maxLinesWhenCollapsed));
    }

    private string BuildSummary(AutoBattleLogEntry e)
    {
        string time = FormatUnix(e.unix);
        string outcome = e.victory ? "Victory" : "Defeat";
        string reason = GetDefeatReason(e);
        string name = string.IsNullOrEmpty(e.opponentName) ? "Unknown" : e.opponentName;
        int level = Mathf.Max(1, e.opponentLevel);
        string outcomeColor = e.victory ? victoryHex : defeatHex;
        string styledOutcome = $"<b><color={outcomeColor}>{outcome}</color></b>";

        string reasonSuffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" - {reason}";

        return $"[{time}] {styledOutcome} vs {name} (Lv {level}){reasonSuffix}";
    }

    private static string BuildDetails(AutoBattleLogEntry e, int maxLines)
    {
        string reason = GetDefeatReason(e);
        bool hasReason = !string.IsNullOrWhiteSpace(reason);

        if (e.lines == null || e.lines.Count == 0)
            return hasReason ? $"- Reason: {reason}" : "No battle lines recorded.";

        var sb = new StringBuilder(512);

        if (hasReason)
            sb.Append("- Reason: ").AppendLine(reason);

        int limit = Mathf.Min(Mathf.Max(1, maxLines), e.lines.Count);

        for (int i = 0; i < limit; i++)
        {
            string line = e.lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            sb.Append("- ").AppendLine(line);
        }

        if (limit < e.lines.Count)
            sb.AppendLine("- ...");

        return sb.Length > 0 ? sb.ToString() : "No battle lines recorded.";
    }

    private static string GetDefeatReason(AutoBattleLogEntry e)
    {
        if (e == null || e.victory)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(e.defeatReason))
            return e.defeatReason;

        if (e.escaped)
            return "On this turn wild monster fled.";

        return string.Empty;
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
}
