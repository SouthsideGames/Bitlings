using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-350)]
public class PostBattleSummaryManager : MonoBehaviour
{
    public static PostBattleSummaryManager I { get; private set; }

    [SerializeField] private PostBattleSummaryPanelUI postBattleSummaryPanelUI;

    readonly Queue<Queued> _pending = new Queue<Queued>();
    const int MaxPendingQueue = 50;
    bool _panelOpen;
    bool _autoBattling;
    bool _battleInProgress;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public int Debug_PendingCount => _pending.Count;
    public bool Debug_AutoHold => _autoBattling;
    public bool Debug_BattleInProgress => _battleInProgress;
    public bool Debug_PanelOpen => _panelOpen;
#endif

    public void ClearQueuedSummaries()
    {
        _pending.Clear();
    }

    private bool IsForegroundAutoActive()
    {
        var em = RiftManager.I;
        return em != null && em.IsAutoMode;
    }


    struct Queued
    {
        public BattleResult result;
        public int growthCoresGained;
        public int leveled;
        public bool captured;
        public string capturedId;
        public int capturedLvl;
        public bool capturedPremium;
        public bool wildWasPremium;
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
        bool capturedPremium = false,
        bool wildWasPremium = false,
        List<string> levelUpSummaries = null,
        int creditsBase = 0,
        int creditsTitleBonus = 0,
        int growthCoresBase = 0,
        int growthCoresTitleBonus = 0,
        List<string> growthCoresDetailLines = null
    )
    {
        _battleInProgress = false;

        // Auto-battle (the player is watching) should NOT queue post-battle summaries.
        // Rewards are already visible during the fights, and we don't want a backlog
        // of queued popups when auto mode ends.
        if (isAuto)
            return;

        // IMPORTANT:
        // If we’re already “holding” summaries (using SetAutoBattling(true) as a suspend),
        // do NOT let this call clear that hold.
        _autoBattling = _autoBattling || isAuto;

        _pending.Enqueue(new Queued
        {
            result = result,
            growthCoresGained = growthCoresGained,
            leveled = monstersLeveledUp,
            captured = captured,
            capturedId = capturedMonsterId,
            capturedLvl = captured ? Mathf.Max(1, capturedLevel) : 0,
            capturedPremium = capturedPremium,
            wildWasPremium = wildWasPremium,
            levelUpLines = levelUpSummaries,

            creditsBase = creditsBase,
            creditsTitleBonus = creditsTitleBonus,

            growthCoresBase = growthCoresBase,
            growthCoresTitleBonus = growthCoresTitleBonus,
            growthCoresDetailLines = growthCoresDetailLines
        });

        // Prevent unbounded queue growth if panel gets stuck open
        while (_pending.Count > MaxPendingQueue) _pending.Dequeue();

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
            capturedPremium: false,
            wildWasPremium: false,
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
        return TryUpdateLatestQueuedCapture(captured, capturedMonsterId, capturedLevel, capturedPremium: false);
    }

    public bool TryUpdateLatestQueuedCapture(bool captured, string capturedMonsterId, int capturedLevel, bool capturedPremium)
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
        last.capturedPremium = captured && capturedPremium;
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

        
        // Contract guard: never show summaries while foreground auto is active.
        if (IsForegroundAutoActive()) return;

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

            // Small defer so UIManager Hide/Show doesn’t collide in the same frame.
            StartCoroutine(Co_DelayedTryShowNext());
        };

        postBattleSummaryPanelUI.Set(
            q.result,
            q.growthCoresGained,
            q.leveled,
            q.captured,
            q.capturedId,
            q.capturedLvl,
            q.capturedPremium,
            q.levelUpLines,
            q.creditsBase,
            q.creditsTitleBonus,
            q.growthCoresBase,
            q.growthCoresTitleBonus,
            q.growthCoresDetailLines,
            q.wildWasPremium
        );

        postBattleSummaryPanelUI.Show();
    }

    IEnumerator Co_DelayedTryShowNext()
    {
        yield return null; // next frame
        TryShowNext();
    }
}
