using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-350)]
public class PostBattleSummaryManager : MonoBehaviour
{
    public static PostBattleSummaryManager I { get; private set; }

    [SerializeField] private PostBattleSummaryPanelUI postBattleSummaryPanelUI;

    readonly Queue<Queued> _pending = new Queue<Queued>();
    bool _panelOpen;
    bool _autoBattling;
    bool _battleInProgress;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    struct Queued
    {
        public BattleResult result;
        public int growthCoresGained;
        public int leveled;
        public bool captured;
        public string capturedId;
        public int capturedLvl;
        public bool capturedShiny;
        public bool wildWasShiny;
        public List<string> levelUpLines;

        public int creditsBase;
        public int creditsTitleBonus;

        public int growthCoresBase;
        public int growthCoresTitleBonus;
        public List<string> growthCoresDetailLines;
    }

    public void NotifyBattleStart() => _battleInProgress = true;

    public void NotifyBattleEnd(
        BattleResult result,
        bool isAuto,
        int growthCoresGained = 0,
        int monstersLeveledUp = 0,
        bool captured = false,
        string capturedMonsterId = null,
        int capturedLevel = 0,
        bool capturedShiny = false,
        bool wildWasShiny = false,
        List<string> levelUpSummaries = null,
        int creditsBase = 0,
        int creditsTitleBonus = 0,
        int growthCoresBase = 0,
        int growthCoresTitleBonus = 0,
        List<string> growthCoresDetailLines = null
    )
    {
        _battleInProgress = false;

        // OPTION 1 (Idle rewards panel):
        // When running auto battles, we do NOT show per-battle summaries.
        // We aggregate results for an IdleBattleRewardPanel summary instead.
        if (isAuto)
        {
            IdleBattleForegroundLogger.LogBattle(result);
            return;
        }

        _pending.Enqueue(new Queued
        {
            result = result,
            growthCoresGained = growthCoresGained,
            leveled = monstersLeveledUp,
            captured = captured,
            capturedId = capturedMonsterId,
            capturedLvl = captured ? Mathf.Max(1, capturedLevel) : 0,
            capturedShiny = capturedShiny,
            wildWasShiny = wildWasShiny,
            levelUpLines = levelUpSummaries,

            creditsBase = creditsBase,
            creditsTitleBonus = creditsTitleBonus,

            growthCoresBase = growthCoresBase,
            growthCoresTitleBonus = growthCoresTitleBonus,
            growthCoresDetailLines = growthCoresDetailLines
        });

        TryShowNext();
    }

    // Backward-compatible overload (preserves prior call sites)
    public void NotifyBattleEnd(
        BattleResult result,
        bool isAuto,
        int growthCoresGained = 0,
        int monstersLeveledUp = 0,
        bool captured = false,
        string capturedMonsterId = null,
        int capturedLevel = 0,
        List<string> levelUpSummaries = null,
        int creditsBase = 0,
        int creditsTitleBonus = 0,
        int growthCoresBase = 0,
        int growthCoresTitleBonus = 0,
        List<string> growthCoresDetailLines = null
    )
    {
        NotifyBattleEnd(
            result,
            isAuto,
            growthCoresGained,
            monstersLeveledUp,
            captured,
            capturedMonsterId,
            capturedLevel,
            capturedShiny: false,
            wildWasShiny: false,
            levelUpSummaries: levelUpSummaries,
            creditsBase: creditsBase,
            creditsTitleBonus: creditsTitleBonus,
            growthCoresBase: growthCoresBase,
            growthCoresTitleBonus: growthCoresTitleBonus,
            growthCoresDetailLines: growthCoresDetailLines
        );
    }

    public bool TryUpdateLatestQueuedCapture(bool captured, string capturedMonsterId, int capturedLevel)
    {
        return TryUpdateLatestQueuedCapture(captured, capturedMonsterId, capturedLevel, capturedShiny: false);
    }

    public bool TryUpdateLatestQueuedCapture(bool captured, string capturedMonsterId, int capturedLevel, bool capturedShiny)
    {
        if (_panelOpen) return false;
        if (_pending.Count == 0) return false;

        int count = _pending.Count;
        var list = new List<Queued>(count);
        while (_pending.Count > 0)
            list.Add(_pending.Dequeue());

        var last = list[list.Count - 1];
        last.captured = captured;
        last.capturedId = capturedMonsterId;
        last.capturedLvl = captured ? Mathf.Max(1, capturedLevel) : 0;
        last.capturedShiny = captured && capturedShiny;
        list[list.Count - 1] = last;

        for (int i = 0; i < list.Count; i++)
            _pending.Enqueue(list[i]);

        return true;
    }

    public void SetAutoBattling(bool on)
    {
        _autoBattling = on;
        if (!on) TryShowNext();
    }

    public void NotifyEnergyDepleted()
    {
        _autoBattling = false;
        TryShowNext();
    }

    public void TryFlush() => TryShowNext();
    public void FlushNowIfPossible() => TryShowNext();

    void TryShowNext()
    {
        if (_panelOpen || _battleInProgress || _autoBattling) return;
        if (_pending.Count == 0) return;

        if (!postBattleSummaryPanelUI)
        {
            Debug.LogWarning("[PostBattleSummaryManager] Missing PostBattleSummaryPanelUI reference.");
            return;
        }

        var q = _pending.Dequeue();
        ShowSummaryImmediately(q);
    }

    void ShowSummaryImmediately(Queued q)
    {
        UIManager.I?.Show(PanelId.PostBattleSummary);
        _panelOpen = true;

        postBattleSummaryPanelUI.OnClosed = () =>
        {
            _panelOpen = false;
            StartCoroutine(Co_DelayedTryShowNext());
        };

        postBattleSummaryPanelUI.Set(
            q.result,
            q.growthCoresGained,
            q.leveled,
            q.captured,
            q.capturedId,
            q.capturedLvl,
            q.capturedShiny,
            q.levelUpLines,
            q.creditsBase,
            q.creditsTitleBonus,
            q.growthCoresBase,
            q.growthCoresTitleBonus,
            q.growthCoresDetailLines,
            q.wildWasShiny
        );

        postBattleSummaryPanelUI.Show();
    }

    IEnumerator Co_DelayedTryShowNext()
    {
        yield return null; // next frame
        TryShowNext();
    }
}
