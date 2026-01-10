using UnityEngine;
using System;
using System.Collections.Generic;

public enum PanelId
{
    None = 0,
    Intro = 1,
    Encounter = 2,
    Gym = 3,
    Home = 4,
    Resources = 5,
    Upgrades = 6,
    Codex = 7,
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

    [Header("Panels")]
    [SerializeField] private List<PanelEntry> panels = new();

    [Header("Animation")]
    [SerializeField] private float openFadeDuration = 0.18f;
    [SerializeField] private float closeFadeDuration = 0.16f;
    [SerializeField] private float slideOffsetY = -30f;

    public event Action<PanelId, bool> OnPanelChanged;

    private readonly Dictionary<PanelId, PanelEntry> _map = new();
    private readonly HashSet<PanelId> _open = new();

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

            // Ensure the current scene state is tracked and consistent
            SetImmediate(p.id, p.root.activeSelf, fireEvent: false);

            // If panel is active in scene and uses fade, ensure it's fully interactive/visible
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

    public bool Hide(PanelId id)
    {
        if (!_map.TryGetValue(id, out var p) || p.root == null) return false;
        SetActive(id, false);
        return true;
    }

    public void CloseAll()
    {
        var list = new List<PanelId>(_open);
        foreach (var id in list)
            SetImmediate(id, false, fireEvent: false);

        _open.Clear();
    }

    public void CloseAllExcept(PanelId keep)
    {
        var list = new List<PanelId>(_open);
        foreach (var id in list)
            if (id != keep) SetActive(id, false);
    }

    public GameObject GetRoot(PanelId id) => _map.TryGetValue(id, out var e) ? e.root : null;

    private void SetImmediate(PanelId id, bool on, bool fireEvent)
    {
        if (!_map.TryGetValue(id, out var p) || p.root == null) return;

        // Cancel any running tweens to avoid "stuck half-faded" panels
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

            // Ensure slide position is normalized (open position)
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
            // Before deactivating, ensure it won't intercept input if a CanvasGroup exists
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
        if (!_map.TryGetValue(id, out var p) || p.root == null)
        {
            Debug.LogWarning($"[UIManager] No panel root for {id}");
            return;
        }

        if (on)
        {
            if (_open.Add(id))
            {
                AnimateOpen(p);
                if (fireEvent) OnPanelChanged?.Invoke(id, true);
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

    // ─────────────────────────────────────────────────────────────────────
    // Animation
    // ─────────────────────────────────────────────────────────────────────

    private void AnimateOpen(PanelEntry p)
    {
        var root = p.root;

        // Cancel any existing tweens (important when toggling quickly)
        CancelTweens(root);

        root.SetActive(true);

        RectTransform rt = root.GetComponent<RectTransform>();

        // Optional slide-in
        if (p.useSlide && rt)
        {
            var pos = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(pos.x, slideOffsetY);
            LeanTween.moveY(rt, 0f, openFadeDuration).setEaseOutCubic();
        }

        // Fade only if enabled
        if (p.useFade)
        {
            CanvasGroup cg = EnsureCanvasGroup(root);

            // Enable interaction immediately on open
            cg.interactable = true;
            cg.blocksRaycasts = true;

            cg.alpha = 0f;
            LeanTween.alphaCanvas(cg, 1f, openFadeDuration).setEaseOutCubic();
        }
    }

    private void AnimateClose(PanelEntry p, Action onComplete)
    {
        var root = p.root;

        // Cancel any existing tweens (important when toggling quickly)
        CancelTweens(root);

        RectTransform rt = root.GetComponent<RectTransform>();

        float dur = closeFadeDuration;
        bool anyTween = false;

        if (p.useFade)
        {
            anyTween = true;

            CanvasGroup cg = EnsureCanvasGroup(root);

            // Disable interaction immediately on close (prevents ghost clicks during fade)
            cg.interactable = false;
            cg.blocksRaycasts = false;

            LeanTween.alphaCanvas(cg, 0f, dur).setEaseInCubic();
        }

        if (p.useSlide && rt)
        {
            anyTween = true;
            LeanTween.moveY(rt, slideOffsetY, dur).setEaseInCubic();
        }

        if (anyTween)
            LeanTween.delayedCall(root, dur, () => onComplete?.Invoke());
        else
            onComplete?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static CanvasGroup EnsureCanvasGroup(GameObject root)
    {
        var cg = root.GetComponent<CanvasGroup>();
        if (cg == null) cg = root.AddComponent<CanvasGroup>();
        return cg;
    }

    private static void CancelTweens(GameObject root)
    {
        if (!root) return;

        // Cancel all tweens associated with this GameObject
        LeanTween.cancel(root);

        // Also cancel tweens on RectTransform if present
        var rt = root.GetComponent<RectTransform>();
        if (rt) LeanTween.cancel(rt.gameObject);
    }
}
