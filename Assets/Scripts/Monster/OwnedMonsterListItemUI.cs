using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(Button))]
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

    [Header("Detail Panel (Assign Mode / Codex)")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    // data
    private OwnedMonsterData _data;
    private MonsterDataSO _def;

    // runtime
    private float _nextUiRefreshAt;
    private bool _allowDetail = true;
    private MonsterDetailPanelUI _detailPanelOverride;

    // Codex browse context
    private bool _isCodexRow;
    private IReadOnlyList<MonsterDataSO> _codexBrowseDefs;

    void Awake()
    {
        if (detailPanel == null)
            detailPanel = FindAnyObjectByType<MonsterDetailPanelUI>(FindObjectsInactive.Include);

        if (cooldownText) cooldownText.gameObject.SetActive(false);

        if (rootButton == null)
            rootButton = GetComponent<Button>();

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

        if (rootButton)
            rootButton.onClick.RemoveListener(OnClickOpenDetails);
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
        _isCodexRow = false;
        _codexBrowseDefs = null;

        _allowDetail = true;
        _detailPanelOverride = null;

        _data = data;
        _def = def;

        bool isShiny = data != null && (data.isShiny || data.shinyTier > 0);

        // Icon
        if (icon)
        {
            if (def)
            {
                var s = MonsterNameFormatter.GetIcon(def, isShiny, backIcon: false);
                if (s)
                {
                    icon.enabled = true;
                    icon.sprite = s;
                    icon.color = Color.white;
                }
                else
                {
                    icon.enabled = false;
                    icon.sprite = null;
                }
            }
            else
            {
                icon.enabled = false;
                icon.sprite = null;
            }
        }

        // Name / ID
        if (nameText)
        {
            if (def)
                nameText.text = MonsterNameFormatter.Format(def, isShiny);
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
    /// Codex row setup. "captured" here means "revealed/known" in the Codex context.
    /// If not captured/revealed, row shows ??? and is not interactable.
    /// </summary>
    public void SetupForCodex(
        MonsterDataSO def,
        OwnedMonsterData ownedData,
        bool captured,
        bool isFavorite,
        bool allowDetail,
        MonsterDetailPanelUI detailPanelOverride)
    {
        _isCodexRow = true;
        _codexBrowseDefs = null; // set later by CodexPanelUI after it knows the final visible list

        _detailPanelOverride = detailPanelOverride;
        _allowDetail = allowDetail && captured; // cannot open detail for unrevealed

        _def = def;
        _data = captured ? ownedData : null; // unrevealed entries have no OwnedMonsterData

        bool isShiny = captured && ownedData != null && (ownedData.isShiny || ownedData.shinyTier > 0);

        // Icon
        if (icon)
        {
            if (def)
            {
                var s = MonsterNameFormatter.GetIcon(def, isShiny, backIcon: false);
                if (s)
                {
                    icon.enabled = true;
                    icon.sprite = s;

                    // Silhouette effect for unrevealed
                    icon.color = captured ? Color.white : Color.black;
                }
                else
                {
                    icon.enabled = false;
                    icon.sprite = null;
                }
            }
            else
            {
                icon.enabled = false;
                icon.sprite = null;
            }
        }

        // Text: captured vs unknown
        if (nameText)
        {
            if (captured && def)
                nameText.text = MonsterNameFormatter.Format(def, isShiny);
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

        // KO / cooldown text only makes sense for owned monsters, not codex silhouettes.
        if (cooldownText)
            cooldownText.gameObject.SetActive(false);

        // Evolve alert makes no sense for codex grid rows.
        if (evolveAlert)
            evolveAlert.SetActive(false);

        ApplyState();

        _nextUiRefreshAt = 0f;
        if (!IsUsable(_data)) UpdateKOCountdown();

        RefreshEvolutionAlert();
    }

    /// <summary>
    /// Called by CodexPanelUI after it knows the final visible list of defs.
    /// Enables swipe-browse context in MonsterDetailPanelUI.
    /// </summary>
    public void SetCodexBrowseContext(IReadOnlyList<MonsterDataSO> visibleDefs)
    {
        _codexBrowseDefs = visibleDefs;
    }

    // ---------------------------------------------------------------------
    // Interactions
    // ---------------------------------------------------------------------

    public void SetInteractable(bool on)
    {
        if (rootButton)
        {
            if (_isCodexRow)
                rootButton.interactable = on && _allowDetail && _def != null;
            else
                rootButton.interactable = on && HasValidMonster(_data) && IsUsable(_data) && _allowDetail;
        }

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

        AudioManager.I?.PlayClick();

        // Codex behavior: open by def (not OwnedMonsterData) and set browse list for swipe
        if (_isCodexRow)
        {
            if (_def == null) return;

            // Use the APIs that exist in MonsterDetailPanelUI
            if (_codexBrowseDefs != null && _codexBrowseDefs.Count > 1)
            {
                int startIndex = 0;
                for (int i = 0; i < _codexBrowseDefs.Count; i++)
                {
                    var d = _codexBrowseDefs[i];
                    if (d && (_def == d || (!string.IsNullOrEmpty(d.id) && d.id == _def.id)))
                    {
                        startIndex = i;
                        break;
                    }
                }

                panel.SetStarterBrowseContext(_codexBrowseDefs, startIndex);
            }
            else
            {
                panel.ClearStarterBrowseContext();
            }

            if (HasValidMonster(_data))
                panel.ShowCodexOwned(_def, _data);
            else
                panel.ShowCodex(_def);
            return;
        }

        // Owned/team behavior
        if (!HasValidMonster(_data)) return;
        if (!IsUsable(_data)) return;

        panel.ShowAssign(_data);
    }

    private void ApplyState()
    {
        if (rootButton)
        {
            if (_isCodexRow)
                rootButton.interactable = (_def != null) && _allowDetail;
            else
                rootButton.interactable = HasValidMonster(_data) && IsUsable(_data) && _allowDetail;
        }

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
        if (IsUsable(_data)) { cooldownText.gameObject.SetActive(false); return; }

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

        long now = SaveManager.NowUnix();
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
