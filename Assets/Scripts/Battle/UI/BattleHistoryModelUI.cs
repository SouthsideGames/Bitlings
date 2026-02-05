using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class BattleHistoryModelUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform content;
    [SerializeField] private TextMeshProUGUI linePrefab;
    [SerializeField] private Button closeButton;

    [Header("Behavior")]
    [SerializeField] private int maxVisibleLines = 200;

    readonly List<TextMeshProUGUI> _spawned = new();

    void Awake()
    {
        if (closeButton) closeButton.onClick.AddListener(Close);
        if (root) root.SetActive(false);
    }

    void OnEnable()  { BattleLogger.OnLineLogged += HandleLine; }
    void OnDisable() { BattleLogger.OnLineLogged -= HandleLine; }

    public void Open()
    {
        if (SettingsManager.I != null && !SettingsManager.I.GetBattleHistoryEnabled())
            return;

        if (!root) return;
        root.SetActive(true);
        Rebuild();
        AutoScrollIfEnabled();
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

    void Rebuild()
    {
        Clear();

        var lines = BattleLogger.GetLinesSnapshot();
        if (lines == null) return;

        int start = Mathf.Max(0, lines.Count - maxVisibleLines);
        for (int i = start; i < lines.Count; i++)
            AddLine(lines[i]);
    }

    void HandleLine(string line)
    {
        if (!root || !root.activeSelf) return;

        AddLine(line);

        // Trim
        while (_spawned.Count > maxVisibleLines)
        {
            var first = _spawned[0];
            _spawned.RemoveAt(0);
            if (first) Destroy(first.gameObject);
        }

        AutoScrollIfEnabled();
    }

    void AddLine(string line)
    {
        if (!content || !linePrefab) return;
        var t = Instantiate(linePrefab, content);
        t.text = line ?? "";
        _spawned.Add(t);
    }

    void AutoScrollIfEnabled()
    {
        bool autoScroll = SettingsManager.I == null || SettingsManager.I.GetAutoScrollBattleLog();
        if (!autoScroll || scrollRect == null) return;

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    void Clear()
    {
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i]) Destroy(_spawned[i].gameObject);
        _spawned.Clear();
    }
}
