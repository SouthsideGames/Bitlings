using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OwnedMonsterListItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI idText;
    [SerializeField] private Button rootButton;
    [SerializeField] private TextMeshProUGUI cooldownText;

    [Header("Alerts")]
    [SerializeField] private GameObject evolveAlert;
    [SerializeField] private GameObject favoriteAlert;

    [Header("Detail Panel (Assign Mode)")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    // data
    private OwnedMonsterData _data;
    private MonsterDataSO _def;

    // runtime
    private float _nextUiRefreshAt;
    private bool _allowDetail = true;
    private MonsterDetailPanelUI _detailPanelOverride;

    void Awake()
    {
        if (detailPanel == null)
            detailPanel = FindAnyObjectByType<MonsterDetailPanelUI>(FindObjectsInactive.Include);

        if (cooldownText) cooldownText.gameObject.SetActive(false);

        if (rootButton)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.onClick.AddListener(OnClickOpenDetails);
        }

        if (evolveAlert)
            evolveAlert.SetActive(false);

        GameEvents.MonsterLeveled -= HandleMonsterLeveled;
        GameEvents.MonsterLeveled += HandleMonsterLeveled;
    }

    private void OnDestroy()
    {
        GameEvents.MonsterLeveled -= HandleMonsterLeveled;
    }

    void OnDisable()
    {
        _nextUiRefreshAt = 0f;
        if (cooldownText) cooldownText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!HasValidMonster(_data)) return;
        if (IsUsable(_data)) return;

        if (Time.unscaledTime >= _nextUiRefreshAt)
        {
            _nextUiRefreshAt = Time.unscaledTime + 1f;
            UpdateKOCountdown();
        }
    }

    // ---------------------------------------------------------------------
    // Standard setup (owned lists, team assigners, etc.)
    // ---------------------------------------------------------------------

    public void Setup(OwnedMonsterData data)
    {
        var def = HasValidMonster(data) ? MonsterLibraryLocator.GetById(data.monsterId) : null;
        Setup(data, def);
    }

    public void Setup(OwnedMonsterData data, MonsterDataSO def)
    {
        _allowDetail = true;
        _detailPanelOverride = null;

        _data = data;
        _def  = def;

        // Icon
        if (icon)
        {
            if (def && def.icon)
            {
                icon.enabled = true;
                icon.sprite  = def.icon;
                icon.color   = Color.white;
            }
            else
            {
                icon.enabled = false;
                icon.sprite  = null;
            }
        }

        // Name / ID
        if (nameText)
        {
            if (def)
                nameText.text = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName;
            else
                nameText.text = "Unknown";
        }

        if (idText)
            idText.text = HasValidMonster(data) ? data.monsterId : "—";

        // Favorites are only shown for Codex entries; hide here.
        if (favoriteAlert)
            favoriteAlert.SetActive(false);

        ApplyState();

        _nextUiRefreshAt = 0f;
        if (!IsUsable(_data)) UpdateKOCountdown();

        RefreshEvolutionAlert();
    }

    // ---------------------------------------------------------------------
    // Codex-specific setup
    // ---------------------------------------------------------------------

    /// <summary>
    /// Setup this row for Codex usage, where every monster in the (captured) pool
    /// is listed. Uncaptured entries would be shown as ??? and are not clickable.
    /// </summary>
    public void SetupForCodex(
        MonsterDataSO def,
        OwnedMonsterData ownedData,
        bool captured,
        bool isFavorite,
        bool allowDetail,
        MonsterDetailPanelUI detailPanelOverride)
    {
        _detailPanelOverride = detailPanelOverride;
        _allowDetail = allowDetail && captured;    // cannot open detail for uncaptured

        _def = def;
        _data = captured ? ownedData : null;       // unknown entries would have no OwnedMonsterData

        // Icon
        if (icon)
        {
            if (def && def.icon)
            {
                icon.enabled = true;
                icon.sprite  = def.icon;
                icon.color   = captured ? Color.white : Color.black;
            }
            else
            {
                icon.enabled = false;
                icon.sprite  = null;
            }
        }

        // Text: captured vs unknown
        if (nameText)
        {
            if (captured && def)
                nameText.text = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName;
            else
                nameText.text = "???";
        }

        if (idText)
        {
            if (captured && def)
                idText.text = def.id;
            else
                idText.text = "???";
        }

        // Favorites icon (only for captured + feature unlocked)
        if (favoriteAlert)
        {
            bool hasFeature = FeatureUnlockManager.I &&
                              FeatureUnlockManager.I.IsUnlocked(FeatureId.Codex_Favorites);
            favoriteAlert.SetActive(hasFeature && captured && isFavorite);
        }

        // KO / cooldown text only makes sense for captured monsters.
        if (cooldownText)
            cooldownText.gameObject.SetActive(false);

        // Evolve alert makes no sense for unknown entries.
        if (evolveAlert)
            evolveAlert.SetActive(false);

        ApplyState();

        _nextUiRefreshAt = 0f;
        if (!IsUsable(_data)) UpdateKOCountdown();

        RefreshEvolutionAlert();
    }

    // ---------------------------------------------------------------------
    // Interactions
    // ---------------------------------------------------------------------

    public void SetInteractable(bool on)
    {
        if (rootButton)
            rootButton.interactable = on && HasValidMonster(_data) && IsUsable(_data) && _allowDetail;

        ApplyKOVisualsOnly();
    }

    private void OnClickOpenDetails()
    {
        if (!_allowDetail)
            return;

        var panel = _detailPanelOverride ? _detailPanelOverride : detailPanel;
        if (panel == null)
        {
            Debug.LogWarning("[OwnedMonsterListItemUI] MonsterDetailPanelUI not found in scene.");
            return;
        }

        if (!HasValidMonster(_data)) return;
        if (!IsUsable(_data)) return;

        AudioManager.I.PlayClick();

        panel.ShowAssign(_data);
    }

    private void ApplyState()
    {
        if (rootButton)
            rootButton.interactable = HasValidMonster(_data) && IsUsable(_data) && _allowDetail;

        ApplyKOVisualsOnly();
        RefreshEvolutionAlert();
    }

    private void ApplyKOVisualsOnly()
    {
        bool isKO = HasValidMonster(_data) && !IsUsable(_data);

        if (cooldownText)
            cooldownText.gameObject.SetActive(isKO);
    }

    private void UpdateKOCountdown()
    {
        if (!cooldownText) return;
        if (!HasValidMonster(_data)) { cooldownText.gameObject.SetActive(false); return; }
        if (IsUsable(_data))        { cooldownText.gameObject.SetActive(false); return; }

        var (ok, eta) = TryGetETAForNextHP(_data, _def);
        cooldownText.gameObject.SetActive(true);
        cooldownText.text = ok ? FormatETA(eta) : "Healing…";
    }

    private void RefreshEvolutionAlert()
    {
        if (!evolveAlert) return;

        var def = _def;
        if (def == null && HasValidMonster(_data))
        {
            def = MonsterLibraryLocator.GetById(_data.monsterId);
            _def = def;
        }

        bool show = false;
        if (_data != null && def != null)
            show = EvolutionHelper.CanEvolve(_data, def);

        evolveAlert.SetActive(show);
    }

    // ---------------------------------------------------------------------
    // Static helpers
    // ---------------------------------------------------------------------

    private static bool HasValidMonster(OwnedMonsterData d)
    {
        return d != null && !string.IsNullOrEmpty(d.monsterId);
    }

    private static bool IsUsable(OwnedMonsterData d)
    {
        return HasValidMonster(d) && d.currentHP != 0;
    }

    private static (bool ok, TimeSpan eta) TryGetETAForNextHP(OwnedMonsterData d, MonsterDataSO def)
    {
        if (!HasValidMonster(d)) return (false, TimeSpan.Zero);

        float perHour = 0f;
        if (def && def.hpRegenPerHour > 0f)
            perHour = def.hpRegenPerHour;
        else
            perHour = HealthRegenSystem.GetDefaultRegenPerHour();

        if (perHour <= 0.0001f) return (false, TimeSpan.Zero);

        int secondsPerHP = Mathf.CeilToInt(3600f / perHour);

        long now  = SaveManager.NowUnix();
        long last = d.lastHPUnix > 0 ? d.lastHPUnix : now;
        long elapsed = Math.Max(0, now - last);

        int remain = Mathf.Clamp(secondsPerHP - (int)elapsed, 1, secondsPerHP);
        return (true, TimeSpan.FromSeconds(remain));
    }

    private static string FormatETA(TimeSpan span)
    {
        if (span.TotalHours >= 1.0)
            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        return $"{span.Minutes:D2}:{span.Seconds:D2}";
    }

    // ---------------------------------------------------------------------
    // Event handlers
    // ---------------------------------------------------------------------

    private void HandleMonsterLeveled(string ownedIdOrDefId, int newLevel)
    {
        if (_data == null)
            return;

        string myKey = !string.IsNullOrEmpty(_data.ownedUID)
            ? _data.ownedUID
            : _data.monsterId;

        if (myKey != ownedIdOrDefId)
            return;

        _data.level = newLevel;

        ApplyState();
    }
}
