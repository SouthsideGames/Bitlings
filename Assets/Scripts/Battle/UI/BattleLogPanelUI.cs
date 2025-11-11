using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using System.Collections.Generic;
using System.Collections;


public class BattleLogPanelUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform content;  
    [SerializeField] private LogRowUI rowPrefab;      

    [Header("Behavior")]
    [Tooltip("If true, clears the panel whenever a new battle begins.")]
    [SerializeField] private bool clearOnBattleStart = false;

    [Tooltip("If true, clears the panel whenever a new encounter begins.")]
    [SerializeField] private bool clearOnEncounterStart = false;

    private readonly List<LogRowUI> _rows = new List<LogRowUI>(256);

    private bool AutoScroll => SettingsManager.I == null || SettingsManager.I.S.autoScrollBattleLog;

    void OnEnable()
    {

        RebuildFromHistory();

        BattleLogger.OnLogAppended    += HandleAdded;
        BattleLogger.OnBattleBegan    += HandleBattleBegin;
        BattleLogger.OnEncounterBegan += HandleEncounterBegin;
    }

    void OnDisable()
    {
        BattleLogger.OnLogAppended    -= HandleAdded;
        BattleLogger.OnBattleBegan    -= HandleBattleBegin;
        BattleLogger.OnEncounterBegan -= HandleEncounterBegin;
    }

    void HandleBattleBegin(string label)
    {
        if (clearOnBattleStart) ClearRows();
        AddSystem($"— Battle started: {label} —");
    }

    void HandleEncounterBegin(string label)
    {
        if (clearOnEncounterStart) ClearRows();
        AddSystem($"— Encounter: {label} —");
    }

    void HandleAdded(LogEntry e)
    {
        AddRow(e);
        if (AutoScroll) ScrollToBottom();
    }

    void RebuildFromHistory()
    {
        ClearRows();
        var list = BattleLogger.Entries;
        for (int i = 0; i < list.Count; i++)
            AddRow(list[i]);
        ScrollToBottom();
    }

    void AddSystem(string text)
    {
        HandleAdded(new LogEntry { text = text, scope = LogScope.System, unix = SaveManager.NowUnix() });
    }

    void AddRow(LogEntry e)
    {
        if (!rowPrefab || !content) return;

        var row = Instantiate(rowPrefab, content);
        row.Set(e);
        _rows.Add(row);

        // Force immediate layout so Content height updates now
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        // If you auto-scroll, do it next frame so the new content size is final
        if (AutoScroll && isActiveAndEnabled)
            StartCoroutine(CoScrollBottomNextFrame());
    }

    IEnumerator CoScrollBottomNextFrame()
    {
        // wait one frame to let the layout system finish
        yield return null;
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f; // bottom
        Canvas.ForceUpdateCanvases();
    }

    void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i]) Destroy(_rows[i].gameObject);
        _rows.Clear();
    }

   void ScrollToBottom()
    {
        if (!scrollRect) return;
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
    }

}
