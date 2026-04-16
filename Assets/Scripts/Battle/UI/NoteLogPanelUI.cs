using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Collections;

public class NoteLogPanelUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform content;
    [SerializeField] private NoteLogRowUI rowPrefab;

    [Header("Behavior")]
    [Tooltip("If true, clears the panel whenever a new battle begins.")]
    [SerializeField] private bool clearOnBattleStart = false;

    [Tooltip("If true, clears the panel whenever a new rift begins.")]
    [SerializeField] private bool clearOnRiftStart = false;

    private readonly List<NoteLogRowUI> _rows = new List<NoteLogRowUI>(256);
    private static int _openPanelCount;
    private bool _countedOpen;
    private Coroutine _scrollCo;

    public static bool IsAnyOpen => _openPanelCount > 0;

    private bool AutoScroll => SettingsManager.I == null || SettingsManager.I.settingsState.autoScrollBattleLog;

    void OnEnable()
    {
        MarkOpen(true);
        RebuildFromHistory();

        BattleLogger.OnLogAppended    += HandleAdded;
        BattleLogger.OnBattleBegan    += HandleBattleBegin;
        BattleLogger.OnRiftBegan += HandleRiftBegin;
        BattleLogger.OnLogCleared     += HandleLogCleared;
    }

    void OnDisable()
    {
        MarkOpen(false);
        BattleLogger.OnLogAppended    -= HandleAdded;
        BattleLogger.OnBattleBegan    -= HandleBattleBegin;
        BattleLogger.OnRiftBegan -= HandleRiftBegin;
        BattleLogger.OnLogCleared     -= HandleLogCleared;
    }

    void OnDestroy()
    {
        MarkOpen(false);
    }

    private void MarkOpen(bool open)
    {
        if (open)
        {
            if (_countedOpen) return;
            _countedOpen = true;
            _openPanelCount++;
            return;
        }

        if (!_countedOpen) return;
        _countedOpen = false;
        _openPanelCount = Mathf.Max(0, _openPanelCount - 1);
    }

    void HandleBattleBegin(string label)
    {
        if (clearOnBattleStart) ClearRows();
        AddSystem($"— Battle started: {label} —");
    }

    void HandleRiftBegin(string label)
    {
        if (clearOnRiftStart) ClearRows();
        AddSystem($"— Rift: {label} —");
    }

    void HandleAdded(LogEntry e)
    {
        AddRow(e);
        if (AutoScroll) ScheduleScrollToBottom();
    }

    void HandleLogCleared()
    {
        ClearRows();
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
        BattleLogger.Log(text, LogScope.System);
    }

    void AddRow(LogEntry e)
    {
        if (!rowPrefab || !content) return;

        var row = Instantiate(rowPrefab, content);
        row.Set(e);
        _rows.Add(row);
    }

    void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i])
                Destroy(_rows[i].gameObject);
        }
        _rows.Clear();
    }

    void ScheduleScrollToBottom()
    {
        if (_scrollCo != null || !isActiveAndEnabled) return;
        _scrollCo = StartCoroutine(CoScrollToBottomDeferred());
    }

    IEnumerator CoScrollToBottomDeferred()
    {
        yield return null;
        if (scrollRect != null && content != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
        _scrollCo = null;
    }

    void ScrollToBottom()
    {
        ScheduleScrollToBottom();
    }

    // ─────────────────────────────────────────────────────────
    // Hook for Post-Game Summary "Continue" button
    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// Call this from your post-battle summary when the player hits Continue.
    /// Pass isAutoBattle = true when you're about to chain into auto battles.
    /// </summary>
    public void PrepForNextBattle(bool isAutoBattle)
    {
        if (isAutoBattle)
        {
            // Auto-battle: clear & disable logging to avoid spam / overhead.
            BattleLogger.ClearAll(false);
            BattleLogger.SetEnabled(false);
        }
        else
        {
            // Manual: clear & leave logging enabled for the next fight.
            BattleLogger.SetEnabled(true);
            BattleLogger.ClearAll(false);
        }
    }
}
