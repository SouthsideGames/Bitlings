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
    Manual = 21,
    Info = 22,
    Log = 23,
    PostBattleSummary = 24,
    Achievement = 25,
    CheatCodes = 26,
    Expedition = 27,
    PackDetails = 28,
    Recycle = 29,
    Story = 30,
}

[Serializable]
public class PanelEntry
{
    public PanelId id;
    public GameObject root;
}

public class UIManager : MonoBehaviour
{
    public static UIManager I { get; private set; }

    [Header("Panels")]
    [SerializeField] private List<PanelEntry> panels = new();

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
            if (!_map.ContainsKey(p.id)) _map.Add(p.id, p);
            SetImmediate(p.id, p.root.activeSelf, fireEvent: false);
        }
    }

    void Start()
    {
        Debug.Log($"[UIManager] Has Intro? {_map.ContainsKey(PanelId.Intro)}");
        CloseAll();
        Show(PanelId.Intro);
    }

    // PUBLIC API
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
        {
            SetImmediate(id, false, fireEvent: false);
        }
        _open.Clear();
    }
    
    public void CloseAllExcept(PanelId keep)
    {
        var list = new List<PanelId>(_open);
        foreach (var id in list) if (id != keep) SetActive(id, false);
    }

    public GameObject GetRoot(PanelId id)
    {
        return _map.TryGetValue(id, out var e) ? e.root : null;
    }

    // INTERNALS --------------------------------------------------------------

    private void SetImmediate(PanelId id, bool on, bool fireEvent)
    {
        if (!_map.TryGetValue(id, out var p) || p.root == null) return;
        p.root.SetActive(on);

        if (on) _open.Add(id);
        else _open.Remove(id);

        if (fireEvent) OnPanelChanged?.Invoke(id, on);
    }

    private void SetActive(PanelId id, bool on, bool fireEvent = true)
    {
        Debug.Log($"[UIManager] SetActive {id} -> {on}");

        if (!_map.TryGetValue(id, out var p) || p.root == null)
        {
            Debug.LogWarning($"[UIManager] No panel root for {id}");
            return;
        }

        if (on)
        {
            if (_open.Add(id))
            {
                AnimateOpen(p.root);
                if (fireEvent) OnPanelChanged?.Invoke(id, true);
            }
        }
        else
        {
            if (_open.Remove(id))
            {
                AnimateClose(p.root, () =>
                {
                    p.root.SetActive(false);
                    if (fireEvent) OnPanelChanged?.Invoke(id, false);
                });
            }
        }
    }

    // ANIMATIONS -------------------------------------------------------------

    private void AnimateOpen(GameObject root)
    {
        root.SetActive(true);

        CanvasGroup cg = root.GetComponent<CanvasGroup>();
        if (cg == null) cg = root.AddComponent<CanvasGroup>();

        RectTransform rt = root.GetComponent<RectTransform>();

        // Start hidden
        cg.alpha = 0f;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -30f);

        // Fade in + slide up
        LeanTween.alphaCanvas(cg, 1f, 0.25f).setEaseOutCubic();
        LeanTween.moveY(rt, 0f, 0.25f).setEaseOutCubic();
    }

    private void AnimateClose(GameObject root, Action onComplete)
    {
        CanvasGroup cg = root.GetComponent<CanvasGroup>();
        if (cg == null) cg = root.AddComponent<CanvasGroup>();

        RectTransform rt = root.GetComponent<RectTransform>();

        // Fade out + slide down
        LeanTween.alphaCanvas(cg, 0f, 0.20f).setEaseInCubic();
        LeanTween.moveY(rt, -30f, 0.20f).setEaseInCubic()
            .setOnComplete(() => onComplete?.Invoke());
    }
}
