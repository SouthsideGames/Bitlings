using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class IronRunTimelineUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private ScrollRect              scrollRect;
    [SerializeField] private RectTransform           contentParent;
    [SerializeField] private IronRunTimelineNodeUI   nodePrefab;
    [SerializeField] private Image                   connectorPrefab;
    [SerializeField] private TextMeshProUGUI         floorLabelPrefab;
    [SerializeField] private TextMeshProUGUI         personalBestLabel;

    [Header("Connector Colors")]
    [SerializeField] private Color connectorVictoryColor = new Color(0.24f, 0.87f, 0.45f, 0.6f);
    [SerializeField] private Color connectorDefeatColor  = new Color(1.00f, 0.33f, 0.33f, 0.6f);
    [SerializeField] private Color connectorEscapedColor = new Color(0.70f, 0.70f, 0.70f, 0.4f);

    private readonly List<IronRunTimelineNodeUI> _nodes = new List<IronRunTimelineNodeUI>();

    public void Bind(List<IronBattleLogEntry> log, bool forfeited, ExecutiveTrialRunState.ExecutiveTrialMode mode)
    {
        // Clear previous children
        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            Destroy(contentParent.GetChild(i).gameObject);
        }
        _nodes.Clear();

        if (log == null || log.Count == 0)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        for (int i = 0; i < log.Count; i++)
        {
            // Add connector before each node except the first
            if (i > 0)
            {
                var connector = Instantiate(connectorPrefab, contentParent);
                
                // Color based on previous entry
                var prevEntry = log[i - 1];
                if (prevEntry.victory)
                    connector.color = connectorVictoryColor;
                else if (prevEntry.wildEscaped || prevEntry.playerEscaped)
                    connector.color = connectorEscapedColor;
                else
                    connector.color = connectorDefeatColor;
            }

            // Add floor label every 3 entries
            if (i > 0 && i % 3 == 0)
            {
                var label = Instantiate(floorLabelPrefab, contentParent);
                label.text = $"Floor {log[i].winsBeforeBattle + 1}";
            }

            // Add node
            var node = Instantiate(nodePrefab, contentParent);
            node.Bind(log[i], isFinalNode: i == log.Count - 1);
            _nodes.Add(node);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentParent);

        // Staggered entry animations
        for (int i = 0; i < _nodes.Count; i++)
        {
            _nodes[i].PlayEntryAnimation(i * 0.06f);
        }

        StartCoroutine(ScrollToEnd());

        // Personal best check
        var stats = ExecutiveTrialStats.Load();
        int best = mode == ExecutiveTrialRunState.ExecutiveTrialMode.Hardcore
            ? stats.bestHardcoreWins : stats.bestStandardWins;
        int winsThisRun = 0;
        foreach (var e in log)
        {
            if (e.victory) winsThisRun++;
        }
        if (personalBestLabel != null)
            personalBestLabel.gameObject.SetActive(winsThisRun > 0 && winsThisRun >= best);
    }

    private IEnumerator ScrollToEnd()
    {
        yield return new WaitForSecondsRealtime(0.4f);
        Canvas.ForceUpdateCanvases();
        if (scrollRect != null)
            scrollRect.horizontalNormalizedPosition = 1f;
    }
}
