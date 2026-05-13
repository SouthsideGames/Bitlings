using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public enum PanelId
{
    None = 0,
    Intro = 1,
    Rift = 2,
    Gym = 3,
    Home = 4,
    Resources = 5,
    Upgrades = 6,
    Directory = 7,
    JobAssign = 8,
    Harbor = 9,
    CryoLab = 10,
    Sanctum = 11,
    WyrmDen = 12,
    ShadowMarket = 13,
    Settings = 14,
    RewardPopup = 15,
    MonsterDetail = 16,
    Evolution = 17,
    TitleDetail = 18,
    StarterPicker = 19,
    Forge = 20,
    Info = 22,
    Log = 23,
    PostBattleSummary = 24,
    Achievement = 25,
    CheatCodes = 26,
    Expedition = 27,
    PackDetails = 28,
    Recycle = 29,
    Story = 30,
    PlayerDossier = 31,
    ImagePreview = 32,
    IdleBattleRewards = 33,

    // Executive Trial: ONLY the main container should be managed by UIManager
    ExecutiveTrialRift = 34,

    // Exchange
    DuplicateResolution = 35,
    Exchange = 36,
    ExchangeSpeciesDetail = 37,
    StatBreakdown = 38,
    AutoBattleHistory = 39,

    // Arena
    ArenaMain = 40,
    ArenaTournamentDetail = 41,
    ArenaMatchDetail = 42,
    ArenaBracket = 43,
}

[Serializable]
public class PanelEntry
{
    public PanelId id;
    public GameObject root;

    [Tooltip("If false, UIManager will NOT add/use a CanvasGroup or fade this panel.")]
    public bool useFade = true;

    [Tooltip("Optional slide animation even when fade is disabled.")]
    public bool useSlide = true;
}

public class UIManager : MonoBehaviour
{
    public static UIManager I { get; private set; }

    private const string TutorialExecutiveTrialUnlockedKey = "tut_executivetrialunlocked_v1";
    private const string TutorialAutoRiftKey = "tut_autorift_v1";
    private const string TutorialArenaUnlockedKey = "tut_arena_v1";
    private const string TutorialAutoEncounterKey = "tut_autoencounter_v1";

    [Header("Panels")]
    [SerializeField] private List<PanelEntry> panels = new();

    [Header("Navigation")]
    [Tooltip("If enabled, opening a MAIN panel will automatically close any other MAIN panels (ex: Home won't stay open under Dictionary). Overlays/popups are not affected.")]
    [SerializeField] private bool singleMainPanelMode = true;

    [Tooltip("Panels treated as overlays/popups. They are allowed to stack and will NOT be auto-closed when opening a main panel.")]
    [SerializeField] private List<PanelId> overlayPanels = new()
    {
        PanelId.JobAssign,
        PanelId.RewardPopup,
        PanelId.MonsterDetail,
        PanelId.Evolution,
        PanelId.TitleDetail,
        PanelId.Info,
        PanelId.Log,
        PanelId.PostBattleSummary,
        PanelId.Achievement,
        PanelId.CheatCodes,
        PanelId.PackDetails,
        PanelId.ImagePreview,
        PanelId.IdleBattleRewards,
        PanelId.DuplicateResolution,
        PanelId.ExchangeSpeciesDetail,
        PanelId.StatBreakdown,
        PanelId.AutoBattleHistory,

        // NOTE:
        // Iron overlays are intentionally NOT included here.
        // They are controlled by ExecutiveTrialRiftPanelUI (or equivalent),
        // not by UIManager.
    };

    [Header("Animation")]
    [SerializeField] private float openFadeDuration = 0.18f;
    [SerializeField] private float closeFadeDuration = 0.16f;
    [SerializeField] private float slideOffsetY = -30f;

    public event Action<PanelId, bool> OnPanelChanged;

    private readonly Dictionary<PanelId, PanelEntry> _map = new();
    private readonly HashSet<PanelId> _open = new();
    private readonly List<PanelId> _tempPanelList = new();

    private Coroutine _idleRewardCo;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        _map.Clear();
        foreach (var p in panels)
        {
            if (p == null || p.root == null) continue;

            if (!_map.ContainsKey(p.id))
                _map.Add(p.id, p);

            SetImmediate(p.id, p.root.activeSelf, fireEvent: false);

            if (p.root.activeSelf && p.useFade)
            {
                var cg = EnsureCanvasGroup(p.root);
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }
    }

    void Start()
    {
        CloseAll();
        Show(PanelId.Intro);
    }

    public void Show(PanelId id) => SetActive(id, true);
    public void Toggle(PanelId id) => SetActive(id, !_open.Contains(id));
    public bool IsOpen(PanelId id) => _open.Contains(id);

    /// <summary>
    /// Shows a modal dialog asking the player to choose between their cloud save and local save
    /// when both have diverged. Calls exactly one of the two callbacks then dismisses.
    /// </summary>
    /// <param name="cloudWins">Number of Arena championship wins recorded in cloud save.</param>
    /// <param name="localWins">Number of Arena championship wins recorded in local save.</param>
    /// <param name="onKeepCloud">Invoked if the player chooses the cloud save.</param>
    /// <param name="onKeepLocal">Invoked if the player chooses the local save.</param>
    public void ShowSaveConflictDialog( // FIXED: new dialog - surfaces cloud/local divergence to player instead of silent overwrite
        int cloudWins,
        int localWins,
        System.Action onKeepCloud,
        System.Action onKeepLocal)
    {
        string message = "Your saves have diverged.\n\n" +
                         $"Cloud save: {cloudWins} Arena win{(cloudWins == 1 ? "" : "s")}\n" +
                         $"This device: {localWins} Arena win{(localWins == 1 ? "" : "s")}\n\n" +
                         "Which save would you like to keep?";

        // WARNING: could not resolve - manual wiring needed
        // UIManager does not currently expose a two-button confirm API such as:
        // ShowConfirmDialog(message, "Keep Cloud", "Keep This Device", onKeepCloud, onKeepLocal);
        // Reuse the project's existing two-button modal once available.
        if (ConfirmToastUI.I != null)
            ConfirmToastUI.I.Show(message);
    }

    public bool Hide(PanelId id)
    {
        if (!_map.TryGetValue(id, out var p) || p.root == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevLog.Log($"[UIManager] Hide ignored (panel not registered): {id}");
#endif
            return false;
        }

        SetActive(id, false);
        return true;
    }

    public void CloseAll()
    {
        _tempPanelList.Clear();
        _tempPanelList.AddRange(_open);
        foreach (var id in _tempPanelList)
            SetImmediate(id, false, fireEvent: false);

        _open.Clear();
    }

    public void CloseAllExcept(PanelId keep)
    {
        _tempPanelList.Clear();
        _tempPanelList.AddRange(_open);
        foreach (var id in _tempPanelList)
            if (id != keep) SetActive(id, false);
    }

    public GameObject GetRoot(PanelId id) => _map.TryGetValue(id, out var e) ? e.root : null;

    void TryOpenIdleBattleRewardsNextFrame()
    {
        if (_idleRewardCo != null) StopCoroutine(_idleRewardCo);
        _idleRewardCo = StartCoroutine(Co_TryOpenIdleBattleRewardsNextFrame());
    }

    void TryOpenExecutiveTrialUnlockedTutorial()
    {
        var data = SaveManager.Data;
        if (data == null) return;
        if (!data.HasExecutiveTrialUnlocked) return;

        int maxRank = PromotionManager.I != null ? PromotionManager.I.GetMaxRank() : 25;
        int rank = Mathf.Max(1, data.promotionRank);
        if (rank < maxRank) return;

        if (SaveManager.IsTutorialComplete(TutorialExecutiveTrialUnlockedKey)) return;

        // Avoid opening over the idle rewards popup; it will be requested again
        // the next time Home is opened if still incomplete.
        if (IsOpen(PanelId.IdleBattleRewards)) return;

        TutorialOverlayPanel.RequestOpen(TutorialExecutiveTrialUnlockedKey);
    }

    void TryOpenArenaUnlockedTutorial()
    {
        if (SaveManager.IsTutorialComplete(TutorialArenaUnlockedKey)) return;

        var arena = SaveManager.GetArenaSaveData();
        if (arena == null || !arena.arenaUnlocked) return;

        if (IsOpen(PanelId.IdleBattleRewards)) return;

        TutorialOverlayPanel.RequestOpen(TutorialArenaUnlockedKey);
    }

    void TryOpenAutoEncounterTutorial()
    {
        if (SaveManager.IsTutorialComplete(TutorialAutoEncounterKey)) return;

        // Only open from Home's foreground context. Home can remain open as an
        // ancestor while other main panels (e.g. Exchange) are shown.
        if (!IsHomeForegroundContext()) return;

        if (FeatureUnlockManager.I == null ||
            !FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic))
            return;

        if (IsOpen(PanelId.IdleBattleRewards)) return;

        TutorialOverlayPanel.RequestOpen(TutorialAutoEncounterKey);
    }

    private bool IsHomeForegroundContext()
    {
        if (!IsOpen(PanelId.Home)) return false;

        foreach (var id in _open)
        {
            if (id == PanelId.Home) continue;
            if (IsOverlayPanel(id)) continue;

            // Any other main panel means Home is not the active context.
            return false;
        }

        return true;
    }

    void TryOpenAutoRiftTutorial()
    {
        if (SaveManager.IsTutorialComplete(TutorialAutoRiftKey)) return;

        // Only show once idle battles have been unlocked
        if (FeatureUnlockManager.I == null ||
            !FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic))
            return;

        TutorialOverlayPanel.RequestOpen(TutorialAutoRiftKey);
    }

    IEnumerator Co_TryOpenIdleBattleRewardsNextFrame()
    {
        yield return null;
        IdleBattleManager.I?.TryOpenSummaryIfNeeded();
    }

    private void SetImmediate(PanelId id, bool on, bool fireEvent)
    {
        if (!_map.TryGetValue(id, out var p) || p.root == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevLog.Log($"[UIManager] SetImmediate ignored (panel not registered): {id}");
#endif
            return;
        }

        CancelTweens(p.root);

        if (on)
        {
            p.root.SetActive(true);

            if (p.useFade)
            {
                var cg = EnsureCanvasGroup(p.root);
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            var rt = p.root.GetComponent<RectTransform>();
            if (rt && p.useSlide)
            {
                var pos = rt.anchoredPosition;
                rt.anchoredPosition = new Vector2(pos.x, 0f);
            }

            _open.Add(id);
        }
        else
        {
            if (p.useFade)
            {
                var cg = p.root.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                    cg.alpha = 0f;
                }
            }

            p.root.SetActive(false);
            _open.Remove(id);
        }

        if (fireEvent) OnPanelChanged?.Invoke(id, on);
    }

    private void SetActive(PanelId id, bool on, bool fireEvent = true)
    {
        // IRON GUARD: prevent regular Rift from being shown during active Iron runs
        if (on && id == PanelId.Rift && ExecutiveTrialRuntime.IsActive)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[UIManager] Blocked opening regular Rift panel while Executive Trial is active.");
#endif
            return;
        }

        // IRON GUARD: if Iron panel is being shown, immediately hide regular Rift if still open
        if (on && id == PanelId.ExecutiveTrialRift && ExecutiveTrialRuntime.IsActive)
        {
            if (_open.Contains(PanelId.Rift))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DevLog.Log("[UIManager] Executive Trial panel opening; force-hiding regular Rift panel.");
#endif
                SetImmediate(PanelId.Rift, false, fireEvent: false);
            }
        }

        if (!_map.TryGetValue(id, out var p) || p.root == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[UIManager] No panel root for {id} (call ignored)");
#endif
            return;
        }

        if (on)
        {
            if (singleMainPanelMode && !IsOverlayPanel(id))
            {
                CloseAllMainPanelsExcept(id);
            }

            if (_open.Add(id))
            {
                AnimateOpen(p);
                if (fireEvent) OnPanelChanged?.Invoke(id, true);

                if (id == PanelId.Home)
                {
                    TryOpenIdleBattleRewardsNextFrame();
                    TryOpenExecutiveTrialUnlockedTutorial();
                    TryOpenArenaUnlockedTutorial();
                    TryOpenAutoEncounterTutorial();
                    ExchangeManager.I?.TryShowPendingDividendHomeToast();
                }

                if (id == PanelId.Rift)
                {
                    TryOpenAutoRiftTutorial();
                }
            }
        }
        else
        {
            if (_open.Remove(id))
            {
                AnimateClose(p, () =>
                {
                    p.root.SetActive(false);
                    if (fireEvent) OnPanelChanged?.Invoke(id, false);
                });
            }
        }
    }

    private bool IsOverlayPanel(PanelId id)
    {
        return overlayPanels != null && overlayPanels.Contains(id);
    }

    private void CloseAllMainPanelsExcept(PanelId keepMain)
    {
        var keepRoot = GetRoot(keepMain);
        _tempPanelList.Clear();
        _tempPanelList.AddRange(_open);
        foreach (var id in _tempPanelList)
        {
            if (id == keepMain) continue;
            if (IsOverlayPanel(id)) continue;

            // If the panel being opened is nested under an already-open main panel,
            // keep that ancestor open (closing it would disable/hide the child panel).
            var candidateRoot = GetRoot(id);
            if (keepRoot != null && candidateRoot != null && keepRoot.transform.IsChildOf(candidateRoot.transform))
                continue;

            SetActive(id, false);
        }
    }

    private void AnimateOpen(PanelEntry p)
    {
        var root = p.root;
        CancelTweens(root);

        root.SetActive(true);

        RectTransform rt = root.GetComponent<RectTransform>();

        if (p.useSlide && rt)
        {
            var pos = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(pos.x, slideOffsetY);
            LeanTween.moveY(rt, 0f, openFadeDuration).setEaseOutCubic().setIgnoreTimeScale(true);
        }

        if (p.useFade)
        {
            CanvasGroup cg = EnsureCanvasGroup(root);
            cg.interactable = true;
            cg.blocksRaycasts = true;

            cg.alpha = 0f;
            LeanTween.alphaCanvas(cg, 1f, openFadeDuration).setEaseOutCubic().setIgnoreTimeScale(true);
        }
    }

    private void AnimateClose(PanelEntry p, Action onComplete)
    {
        var root = p.root;
        CancelTweens(root);

        RectTransform rt = root.GetComponent<RectTransform>();

        float dur = closeFadeDuration;
        bool anyTween = false;

        if (p.useFade)
        {
            anyTween = true;

            CanvasGroup cg = EnsureCanvasGroup(root);
            cg.interactable = false;
            cg.blocksRaycasts = false;

            LeanTween.alphaCanvas(cg, 0f, dur).setEaseInCubic().setIgnoreTimeScale(true);
        }

        if (p.useSlide && rt)
        {
            anyTween = true;
            LeanTween.moveY(rt, slideOffsetY, dur).setEaseInCubic().setIgnoreTimeScale(true);
        }

        if (anyTween)
            LeanTween.delayedCall(root, dur, () => onComplete?.Invoke()).setIgnoreTimeScale(true);
        else
            onComplete?.Invoke();
    }

    private static CanvasGroup EnsureCanvasGroup(GameObject root)
    {
        var cg = root.GetComponent<CanvasGroup>();
        if (cg == null) cg = root.AddComponent<CanvasGroup>();
        return cg;
    }

    private static void CancelTweens(GameObject root)
    {
        if (!root) return;
        LeanTween.cancel(root);

        var rt = root.GetComponent<RectTransform>();
        if (rt) LeanTween.cancel(rt.gameObject);
    }
}