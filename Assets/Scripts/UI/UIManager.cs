using UnityEngine;
using System;
using System.Collections.Generic;

public enum PanelId
{
    None = 0,
    Intro = 1,
    Encounter = 2,
    Growth = 3,
    Home = 4,
    Resources = 5,
    Upgrades = 6,
    MonsterBox = 7,
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
    HowToPlay = 21,
    Info = 22,
    Log = 23,
    PostBattleSummary = 24,
    Achievement = 25,
    CheatCodes = 26,
    DebugTools = 27,
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
