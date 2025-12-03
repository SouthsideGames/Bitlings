using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-350)]
public class PostBattleSummaryManager : MonoBehaviour
{
    public static PostBattleSummaryManager I { get; private set; }

    [SerializeField] private PostBattleSummaryPanelUI postBattleSummaryPanelUI;

    [Header("Fade Overlay (Battle → Summary)")]
    [SerializeField] private CanvasGroup fadeOverlay;              // full-screen black image
    [SerializeField, Min(0f)] private float fadeToBlackDuration = 0.30f;
    [SerializeField, Min(0f)] private float delayBeforeFade = 0.10f;
    [SerializeField, Min(0f)] private float delayBeforeSummaryAfterFade = 0.05f;
    [SerializeField, Range(0f, 1f)] private float fadeOverlayTargetAlpha = 0.8f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.25f;

    readonly Queue<Queued> _pending = new Queue<Queued>();
    bool _panelOpen;
    bool _autoBattling;
    bool _battleInProgress;
    bool _isFading;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (fadeOverlay)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.blocksRaycasts = false;
            fadeOverlay.interactable = false;
            fadeOverlay.gameObject.SetActive(false);
        }
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
        // Don’t show while:
        // • Panel already open
        // • Battle still running
        // • Auto mode is on
        // • In the middle of fading
        if (_panelOpen || _battleInProgress || _autoBattling || _isFading) return;
        if (_pending.Count == 0) return;

        if (!postBattleSummaryPanelUI)
        {
            Debug.LogWarning("[PostBattleSummaryManager] Missing PostBattleSummaryPanelUI reference.");
            return;
        }

        var q = _pending.Dequeue();

        // If we have a fade overlay, use the Pokémon-style:
        // Wild faints → short delay → fade to black → summary
        if (fadeOverlay && fadeToBlackDuration > 0f)
        {
            StartCoroutine(Co_FadeAndShow(q));
        }
        else
        {
            ShowSummaryImmediately(q);
        }
    }

    System.Collections.IEnumerator Co_FadeAndShow(Queued q)
    {
        _isFading = true;

        // Small pause after KO so the player can see the faint
        if (delayBeforeFade > 0f)
            yield return new WaitForSecondsRealtime(delayBeforeFade);

        // Activate overlay & fade to black
        fadeOverlay.gameObject.SetActive(true);
        fadeOverlay.blocksRaycasts = true;
        fadeOverlay.interactable = false;
        fadeOverlay.alpha = 0f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, fadeToBlackDuration);
            float a = Mathf.Lerp(0f, fadeOverlayTargetAlpha, Mathf.Clamp01(t));
            fadeOverlay.alpha = a;
            yield return null;
        }
        fadeOverlay.alpha = fadeOverlayTargetAlpha;

        if (delayBeforeSummaryAfterFade > 0f)
            yield return new WaitForSecondsRealtime(delayBeforeSummaryAfterFade);

        ShowSummaryImmediately(q);
    }

    void ShowSummaryImmediately(Queued q)
    {
        UIManager.I?.Show(PanelId.PostBattleSummary);
        _panelOpen = true;

        postBattleSummaryPanelUI.OnClosed = () =>
        {
            _panelOpen = false;

            // Fade back from black to battle UI / world
            if (fadeOverlay)
            {
                StartCoroutine(Co_FadeOutOverlayThenShowNext());
            }
            else
            {
                _isFading = false;
                LeanTween.delayedCall(gameObject, 0.05f, TryShowNext);
            }
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

    System.Collections.IEnumerator Co_FadeOutOverlayThenShowNext()
    {
        if (!fadeOverlay)
        {
            _isFading = false;
            LeanTween.delayedCall(gameObject, 0.05f, TryShowNext);
            yield break;
        }

        float startA = fadeOverlay.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, fadeOutDuration);
            float a = Mathf.Lerp(startA, 0f, Mathf.Clamp01(t));
            fadeOverlay.alpha = a;
            yield return null;
        }

        fadeOverlay.alpha = 0f;
        fadeOverlay.gameObject.SetActive(false);
        fadeOverlay.blocksRaycasts = false;

        _isFading = false;

        // Small delay to prevent immediate re-open spam
        LeanTween.delayedCall(gameObject, 0.05f, TryShowNext);
    }
}
