using UnityEngine;
using System;
using System.Collections.Generic;

public enum PanelId
{
    None = 0,
    Intro,
    Encounter,
    Training,
    Home,
    Resources,
    Upgrades,
    Monsters,
    JobAssign,
    Harbor,
    CryoLab,
    Sanctum,
    WyrmDen,
    ShadowMarket,
    Settings,
    RewardPopup,
    MonsterDetail,
    Evolution,
    TitleDetail,
    StarterPicker,
    Forge,
    HowToPlay,
    Info,
    Log,
    PostBattleSummary,
    Achievement,
    DebugTools,
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

    [Header("Registry (fill in Inspector)")]
    [SerializeField] private List<PanelEntry> panels = new List<PanelEntry>();

    // events
    public event Action<PanelId, bool> OnPanelChanged; 

    // internals
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
            // ensure consistent initial state from scene
            SetActive(p.id, p.root.activeSelf, fireEvent: false);
        }
    }

    void Start()
    {
        CloseAll();

        Show(PanelId.Intro);
    }

    // ------- Public API -------

    public void Show(PanelId id)
    {
        SetActive(id, true);
    }

    public bool Hide(PanelId id)
    {
        if (!_map.TryGetValue(id, out var panel) || panel.root == null)
        {
            Debug.LogWarning($"[UIManager] Hide failed: {id} not registered");
            return false;
        }
        panel.root.SetActive(false);
        _open.Remove(id);
        OnPanelChanged?.Invoke(id, false);
        return true;
    }

    public void Toggle(PanelId id)
    {
        bool isOpen = IsOpen(id);
        SetActive(id, !isOpen);
    }

    public bool IsOpen(PanelId id) => _open.Contains(id);

    public void CloseAll()
    {
        var toClose = new List<PanelId>(_open);
        foreach (var id in toClose) SetActive(id, false);
    }

    public void CloseAllExcept(PanelId keep)
    {
        var toClose = new List<PanelId>(_open);
        foreach (var id in toClose) if (id != keep) SetActive(id, false);
    }

    public GameObject GetRoot(PanelId id)
    {
        return _map.TryGetValue(id, out var e) ? e.root : null;
    }

    // ------- Internals -------

    private void SetActive(PanelId id, bool on, bool fireEvent = true)
    {
        if (!_map.TryGetValue(id, out var e) || e.root == null) return;

        if (on)
        {
            if (_open.Add(id))
            {
                e.root.SetActive(true);
                if (fireEvent) OnPanelChanged?.Invoke(id, true);
            }
        }
        else
        {
            if (_open.Remove(id))
            {
                e.root.SetActive(false);
                if (fireEvent) OnPanelChanged?.Invoke(id, false);
            }
        }
    }
}
