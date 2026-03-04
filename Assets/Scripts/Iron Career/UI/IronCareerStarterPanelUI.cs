using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class IronCareerStarterPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager iron;
    [SerializeField] private MonsterLibrarySO monsterLibrary;

    [Header("Slots (3)")]
    [SerializeField] private IronCareerStarterSlotUI slot1;
    [SerializeField] private IronCareerStarterSlotUI slot2;
    [SerializeField] private IronCareerStarterSlotUI slot3;

    [Tooltip("Buttons over each slot so the player can pick ONE starter.")]
    [SerializeField] private Button slot1Button;
    [SerializeField] private Button slot2Button;
    [SerializeField] private Button slot3Button;

    [Header("Mode UI")]
    [SerializeField] private Toggle standardToggle;
    [SerializeField] private Toggle hardcoreToggle;
    [SerializeField] private TMP_Text modeDescText;

    [Header("Buttons")]
    [SerializeField] private Button rerollButton;
    [SerializeField] private Button startButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button rulesButton;

    [Header("Weekly Reroll UI")]
    [SerializeField] private TMP_Text rerollCountText;
    [SerializeField] private int dailyRerollsMax = 1;

    [Header("Reroll Suspense")]
    [Tooltip("Total spin duration before final results lock in.")]
    [SerializeField] private float spinDuration = 0.9f;

    [Tooltip("How fast the images cycle (seconds). Smaller = faster.")]
    [SerializeField] private float spinInterval = 0.06f;

    [Tooltip("Extra delay between locking slot 1→2→3 (adds drama).")]
    [SerializeField] private float lockStepDelay = 0.10f;

    private IronCareerEncounterPanelUI _ironUI;

    private IronCareerMetaData _metaData;

    private readonly List<MonsterDataSO> _offer = new List<MonsterDataSO>(3);
    private List<MonsterDataSO> _pool; 
    private Dictionary<string, MonsterDataSO> _byId; 
    private Coroutine _spinCo;
    private bool _spinning;

    private bool _toggleGuard;
    private int _rerollsRemaining;

    // Selection
    private int _selectedIndex = -1;

    private void Awake()
    {
        if (!iron) iron = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);

        _ironUI = IronCareerEncounterPanelUI.I;
        if (!_ironUI) _ironUI = FindFirstObjectByType<IronCareerEncounterPanelUI>(FindObjectsInactive.Include);

        if (rerollButton) rerollButton.onClick.AddListener(OnRerollClicked);
        if (startButton) startButton.onClick.AddListener(StartRun);
        if (backButton) backButton.onClick.AddListener(BackToHome);
        if (rulesButton) rulesButton.onClick.AddListener(OpenRules);

        if (standardToggle) standardToggle.onValueChanged.AddListener(OnStandardToggleChanged);
        if (hardcoreToggle) hardcoreToggle.onValueChanged.AddListener(OnHardcoreToggleChanged);

        if (slot1Button) slot1Button.onClick.AddListener(() => SelectIndex(0));
        if (slot2Button) slot2Button.onClick.AddListener(() => SelectIndex(1));
        if (slot3Button) slot3Button.onClick.AddListener(() => SelectIndex(2));
    }

    private void OnEnable()
    {
        if (!_ironUI)
        {
            _ironUI = IronCareerEncounterPanelUI.I;
            if (!_ironUI) _ironUI = FindFirstObjectByType<IronCareerEncounterPanelUI>(FindObjectsInactive.Include);
        }

        LoadOrResetDailyRerollsAndOffer();

        _toggleGuard = true;

        bool std = standardToggle && standardToggle.isOn;
        bool hc = hardcoreToggle && hardcoreToggle.isOn;

        if (!std && !hc)
        {
            if (standardToggle) standardToggle.isOn = true;
        }
        else if (std && hc)
        {
            // If both somehow on, prefer Standard.
            if (hardcoreToggle) hardcoreToggle.isOn = false;
        }

        _toggleGuard = false;

        RefreshModeDesc();

        EnsurePool();
        EnsureLookup();

        if (!TryLoadOfferFromMeta())
        {
            StartSpinReroll(consumesDaily: false);
        }
        else
        {
            _spinning = false;
            if (_spinCo != null) { StopCoroutine(_spinCo); _spinCo = null; }

            ClearSelection(); 
            SetButtonsInteractable(true);
            RefreshRerollUI();
            RefreshStartButton();
        }
    }

    private void OnDisable()
    {
        if (_spinCo != null)
        {
            StopCoroutine(_spinCo);
            _spinCo = null;
        }
        _spinning = false;
    }

    // ─────────────────────────────────────────────────────────────
    // Toggle exclusivity
    // ─────────────────────────────────────────────────────────────

    private void OnStandardToggleChanged(bool on)
    {
        if (_toggleGuard) return;
        if (!on) return;

        _toggleGuard = true;
        if (hardcoreToggle) hardcoreToggle.isOn = false;
        _toggleGuard = false;

        RefreshModeDesc();
    }

    private void OnHardcoreToggleChanged(bool on)
    {
        if (_toggleGuard) return;
        if (!on) return;

        _toggleGuard = true;
        if (standardToggle) standardToggle.isOn = false;
        _toggleGuard = false;

        RefreshModeDesc();
    }

    // ─────────────────────────────────────────────────────────────
    // Weekly meta (rerolls + saved weekly offer)
    // ─────────────────────────────────────────────────────────────

    private void LoadOrResetDailyRerollsAndOffer()
    {
        _metaData = IronCareerMetaSave.Load();

        string weekKey = BuildIsoWeekKey(DateTime.Now);

        if (_metaData.lastRerollDate != weekKey)
        {
            _metaData.lastRerollDate = weekKey;
            _metaData.rerollsRemaining = Mathf.Max(0, dailyRerollsMax);
            _metaData.starterOfferIds = null;

            IronCareerMetaSave.Save(_metaData);
        }

        _rerollsRemaining = Mathf.Clamp(
            _metaData.rerollsRemaining,
            0,
            Mathf.Max(0, dailyRerollsMax)
        );

        RefreshRerollUI();
    }

    private void ConsumeReroll()
    {
        if (_rerollsRemaining <= 0) return;

        _rerollsRemaining--;

        if (_metaData != null)
        {
            _metaData.rerollsRemaining = _rerollsRemaining;
            IronCareerMetaSave.Save(_metaData);
        }

        RefreshRerollUI();
    }

    private void RefreshRerollUI()
    {
        if (rerollCountText)
            rerollCountText.text = $"{_rerollsRemaining}/{Mathf.Max(0, dailyRerollsMax)}";

        if (rerollButton)
            rerollButton.interactable = !_spinning && _rerollsRemaining > 0;
    }

    private void OnRerollClicked()
    {
        StartSpinReroll(consumesDaily: true);
    }

    // ─────────────────────────────────────────────────────────────
    // Offer persistence (prevent open/close spam)
    // ─────────────────────────────────────────────────────────────

    private void EnsureLookup()
    {
        if (_byId != null) return;

        _byId = new Dictionary<string, MonsterDataSO>(256);

        if (!monsterLibrary || monsterLibrary.monsters == null) return;

        foreach (var m in monsterLibrary.monsters)
        {
            if (!m) continue;
            if (string.IsNullOrEmpty(m.id)) continue;

            if (!_byId.ContainsKey(m.id))
                _byId.Add(m.id, m);
        }
    }

    private static bool IsValidStarter(MonsterDataSO m)
    {
        if (!m) return false;
        if (!m.canBeStarter) return false;
        if (m.uncatchable) return false;
        if (m.isBoss) return false;
        if (string.IsNullOrEmpty(m.id)) return false;
        if (m.starterWeight <= 0) return false;
        return true;
    }

    private static string BuildIsoWeekKey(DateTime dt)
    {
        int isoDay = ((int)dt.DayOfWeek + 6) % 7 + 1; // Monday=1..Sunday=7
        DateTime thursday = dt.Date.AddDays(4 - isoDay);
        DateTime week1 = new DateTime(thursday.Year, 1, 4);
        int week1IsoDay = ((int)week1.DayOfWeek + 6) % 7 + 1;
        DateTime week1Thursday = week1.AddDays(4 - week1IsoDay);
        int week = 1 + (int)((thursday - week1Thursday).TotalDays / 7d);
        return $"{thursday.Year}-W{Mathf.Clamp(week, 1, 53):00}";
    }

    private bool TryLoadOfferFromMeta()
    {
        if (_metaData == null) return false;
        if (_metaData.starterOfferIds == null) return false;
        if (_metaData.starterOfferIds.Length != 3) return false;

        EnsureLookup();
        if (_byId == null || _byId.Count == 0) return false;

        _offer.Clear();

        for (int i = 0; i < 3; i++)
        {
            string id = _metaData.starterOfferIds[i];
            if (string.IsNullOrEmpty(id)) return false;

            if (!_byId.TryGetValue(id, out var def)) return false;
            if (!IsValidStarter(def)) return false;

            _offer.Add(def);
        }

        // Must be unique
        if (_offer[0].id == _offer[1].id) return false;
        if (_offer[0].id == _offer[2].id) return false;
        if (_offer[1].id == _offer[2].id) return false;

        // Bind immediately (no spin)
        if (slot1) slot1.Bind(_offer[0]);
        if (slot2) slot2.Bind(_offer[1]);
        if (slot3) slot3.Bind(_offer[2]);

        ClearSelection();
        RefreshStartButton();
        return true;
    }

    private void SaveOfferToMeta()
    {
        if (_metaData == null) return;
        if (_offer.Count != 3) return;

        _metaData.starterOfferIds = new string[3]
        {
            _offer[0].id,
            _offer[1].id,
            _offer[2].id
        };

        IronCareerMetaSave.Save(_metaData);
    }

    // ─────────────────────────────────────────────────────────────
    // Reroll suspense
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spins through candidates for suspense, then locks in 3 unique weighted starters.
    /// If consumesDaily=true, decrements the daily reroll count first (and aborts if 0).
    /// </summary>
    public void StartSpinReroll(bool consumesDaily)
    {
        if (_spinning) return;

        EnsurePool();
        EnsureLookup();

        if (_pool == null || _pool.Count == 0)
        {
            HandleEmptyPool();
            return;
        }

        if (consumesDaily)
        {
            if (_rerollsRemaining <= 0)
            {
                RefreshRerollUI();
                return;
            }
            ConsumeReroll();
        }

        // When a new offer is being generated, clear selection (player must choose again).
        ClearSelection();
        RefreshStartButton();

        if (_spinCo != null) StopCoroutine(_spinCo);
        _spinCo = StartCoroutine(Co_SpinThenLock());
    }

    private IEnumerator Co_SpinThenLock()
    {
        _spinning = true;
        SetButtonsInteractable(false);

        float t = 0f;

        // Rapid cycling phase
        while (t < spinDuration)
        {
            slot1?.Bind(_pool[UnityEngine.Random.Range(0, _pool.Count)]);
            slot2?.Bind(_pool[UnityEngine.Random.Range(0, _pool.Count)]);
            slot3?.Bind(_pool[UnityEngine.Random.Range(0, _pool.Count)]);

            yield return new WaitForSeconds(spinInterval);
            t += spinInterval;
        }

        // Lock phase (unique, weighted)
        _offer.Clear();
        var used = new HashSet<string>();

        var final1 = PickWeightedUnique(_pool, used);
        if (final1) { _offer.Add(final1); used.Add(final1.id); }
        slot1?.Bind(final1);
        yield return new WaitForSeconds(lockStepDelay);

        var final2 = PickWeightedUnique(_pool, used);
        if (final2) { _offer.Add(final2); used.Add(final2.id); }
        slot2?.Bind(final2);
        yield return new WaitForSeconds(lockStepDelay);

        var final3 = PickWeightedUnique(_pool, used);
        if (final3) { _offer.Add(final3); used.Add(final3.id); }
        slot3?.Bind(final3);

        // Persist offer so open/close can't change it.
        SaveOfferToMeta();

        _spinning = false;
        _spinCo = null;

        // Player must choose one after the spin.
        ClearSelection();
        RefreshStartButton();

        SetButtonsInteractable(true);
        RefreshRerollUI();
    }

    private void HandleEmptyPool()
    {
        Debug.LogError("[IronCareerStarterPanelUI] No valid starters found in MonsterLibrary. Starter selection disabled.");

        _offer.Clear();
        ClearSelection();
        RefreshStartButton();
        RefreshRerollUI();

        if (rerollButton) rerollButton.interactable = false;
        if (startButton) startButton.interactable = false;
        if (slot1Button) slot1Button.interactable = false;
        if (slot2Button) slot2Button.interactable = false;
        if (slot3Button) slot3Button.interactable = false;

        // Keep back/rules enabled so the player can exit gracefully.
        if (modeDescText) modeDescText.text = "No starters available. Please return to Home.";
    }

    private void SetButtonsInteractable(bool on)
    {
        if (rerollButton) rerollButton.interactable = on && (_rerollsRemaining > 0) && !_spinning;
        if (startButton) startButton.interactable = on && CanStart();
        if (backButton) backButton.interactable = on;
        if (rulesButton) rulesButton.interactable = on;

        if (standardToggle) standardToggle.interactable = on;
        if (hardcoreToggle) hardcoreToggle.interactable = on;

        if (slot1Button) slot1Button.interactable = on && !_spinning;
        if (slot2Button) slot2Button.interactable = on && !_spinning;
        if (slot3Button) slot3Button.interactable = on && !_spinning;
    }

    private void EnsurePool()
    {
        if (_pool != null && _pool.Count > 0) return;

        _pool = new List<MonsterDataSO>(64);

        if (!monsterLibrary || monsterLibrary.monsters == null) return;

        foreach (var m in monsterLibrary.monsters)
        {
            if (!IsValidStarter(m)) continue;
            _pool.Add(m);
        }
    }

    private static MonsterDataSO PickWeightedUnique(List<MonsterDataSO> pool, HashSet<string> usedIds)
    {
        int total = 0;

        for (int i = 0; i < pool.Count; i++)
        {
            var m = pool[i];
            if (!m) continue;
            if (usedIds != null && usedIds.Contains(m.id)) continue;
            total += Mathf.Max(0, m.starterWeight);
        }

        if (total <= 0) return null;

        int roll = UnityEngine.Random.Range(0, total);
        for (int i = 0; i < pool.Count; i++)
        {
            var m = pool[i];
            if (!m) continue;
            if (usedIds != null && usedIds.Contains(m.id)) continue;

            int w = Mathf.Max(0, m.starterWeight);
            if (w <= 0) continue;

            roll -= w;
            if (roll < 0) return m;
        }

        // fallback
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            var m = pool[i];
            if (!m) continue;
            if (usedIds != null && usedIds.Contains(m.id)) continue;
            return m;
        }

        return null;
    }

    private void RefreshModeDesc()
    {
        if (!modeDescText) return;

        bool hardcore = hardcoreToggle && hardcoreToggle.isOn;
        modeDescText.text = hardcore
            ? "Hardcore: Forced Hire (you must take the new monster)."
            : "Standard: Optional Hire (you may skip).";
    }

    // ─────────────────────────────────────────────────────────────
    // Selection UI
    // ─────────────────────────────────────────────────────────────

    private void SelectIndex(int index)
    {
        if (_spinning) return;
        if (_offer == null || _offer.Count != 3) return;
        if (index < 0 || index > 2) return;

        _selectedIndex = index;
        RefreshStartButton();
    }

    private void ClearSelection()
    {
        _selectedIndex = -1;

    }

    private bool CanStart()
    {
        return iron != null && !_spinning && _offer != null && _offer.Count == 3 && _selectedIndex >= 0 && _selectedIndex <= 2;
    }

    private void RefreshStartButton()
    {
        if (startButton) startButton.interactable = CanStart();
    }

    // ─────────────────────────────────────────────────────────────
    // Panel actions (Start / Back / Rules)
    // ─────────────────────────────────────────────────────────────

    private void StartRun()
    {
        bool canStart = CanStart();
        string selectedName = (_offer != null && _selectedIndex >= 0 && _selectedIndex < _offer.Count && _offer[_selectedIndex] != null)
            ? _offer[_selectedIndex].name
            : "NULL";
        Debug.Log($"[IronCareerStarterPanelUI] StartRun clicked: canStart={canStart} spinning={_spinning} selectedIndex={_selectedIndex} offerCount={(_offer != null ? _offer.Count : -1)} selected={selectedName} standardOn={(standardToggle && standardToggle.isOn)} hardcoreOn={(hardcoreToggle && hardcoreToggle.isOn)}");

        if (!canStart) return;

        bool hardcore = hardcoreToggle && hardcoreToggle.isOn;

        var mode = hardcore
            ? IronCareerRunState.IronCareerMode.Hardcore
            : IronCareerRunState.IronCareerMode.Standard;

        MonsterDataSO starter = _offer[_selectedIndex];
        if (!starter) return;

        AudioManager.I?.PickIronCareerBattleMusicOnStartPress(playImmediately: true);

        // Pass ONLY the selected starter.
        iron.StartNewRunFromUI(mode, new List<MonsterDataSO> { starter });
    }

    private void BackToHome()
    {
        // UIManager only manages top-level containers.
        UIManager.I?.Hide(PanelId.IronCareerEncounter);
        UIManager.I?.Show(PanelId.Home);
    }

    public void OpenRules()
    {
        if (!_ironUI)
        {
            _ironUI = IronCareerEncounterPanelUI.I;
            if (!_ironUI) _ironUI = FindFirstObjectByType<IronCareerEncounterPanelUI>(FindObjectsInactive.Include);
        }

        if (_ironUI)
            _ironUI.ShowRules(immediate: true);
        else
            Debug.LogWarning("[IronCareerStarterPanelUI] OpenRules failed: missing IronCareerEncounterPanelUI.");
    }
}