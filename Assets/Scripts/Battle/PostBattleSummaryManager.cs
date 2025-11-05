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
        public List<string> levelUpLines;

        public int coinsBase;
        public int coinsTitleBonus;

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
        List<string> levelUpSummaries = null,
        int coinsBase = 0,
        int coinsTitleBonus = 0,
        int growthCoresBase = 0,
        int growthCoresTitleBonus = 0,
        List<string> growthCoresDetailLines = null
    )
    {
        _battleInProgress = false;
        _autoBattling = isAuto;

        _pending.Enqueue(new Queued
        {
            result = result,
            growthCoresGained = growthCoresGained,
            leveled = monstersLeveledUp,
            captured = captured,
            capturedId = capturedMonsterId,
            capturedLvl = capturedLevel,
            levelUpLines = levelUpSummaries,
            coinsBase = coinsBase,
            coinsTitleBonus = coinsTitleBonus,
            growthCoresBase = growthCoresBase,
            growthCoresTitleBonus = growthCoresTitleBonus,
            growthCoresDetailLines = growthCoresDetailLines
        });

        TryShowNext();
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

        UIManager.I?.Show(PanelId.PostBattleSummary);
        _panelOpen = true;

        postBattleSummaryPanelUI.OnClosed = () =>
        {
            _panelOpen = false;
            LeanTween.delayedCall(gameObject, 0.05f, TryShowNext);
        };

        postBattleSummaryPanelUI.Set(
            q.result,
            q.growthCoresGained,
            q.leveled,
            q.captured,
            q.capturedId,
            q.capturedLvl,
            q.levelUpLines,
            q.coinsBase,
            q.coinsTitleBonus,
            q.growthCoresBase,
            q.growthCoresTitleBonus,
            q.growthCoresDetailLines
        );

        postBattleSummaryPanelUI.Show();
    }
}
