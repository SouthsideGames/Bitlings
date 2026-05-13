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

    [Header("Badge")]
    [Tooltip("Small pill/label that shows CORE or the PackId (e.g., MP-001).")]
    [SerializeField] private GameObject packBadgeRoot;

    [Header("Alerts")]
    [SerializeField] private GameObject evolveAlert;
    [SerializeField] private GameObject favoriteAlert;

    [Header("Team Status")]
    [SerializeField] private GameObject idleTeamAlert;
    [SerializeField] private GameObject arenaTeamAlert;

    [Header("Detail Panel (Assign Mode / Directory)")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    // data
    private OwnedMonsterData _data;
    private MonsterDataSO _def;

    // runtime
    private bool _allowDetail = true;
    private MonsterDetailPanelUI _detailPanelOverride;
    private int _rowPunchTweenId = -1;
    private int _favoritePulseTweenId = -1;
    private int _evolvePulseTweenId = -1;
    private Coroutine _koCountdownCoroutine; // UPGRADED

    // Directory browse context
    private bool _isDirectoryRow;
    private IReadOnlyList<MonsterDataSO> _directoryBrowseDefs;

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
        StopAllTweens();

        if (rootButton)
            rootButton.onClick.RemoveListener(OnClickOpenDetails);
    }

    void OnEnable() // UPGRADED
    {
        GameEvents.OnTeamChanged += RefreshKOCountdown; // UPGRADED
        GameEvents.BattleFinished += OnBattleFinished; // UPGRADED
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged -= RefreshKOCountdown; // UPGRADED
        GameEvents.BattleFinished -= OnBattleFinished; // UPGRADED
        if (_koCountdownCoroutine != null) // UPGRADED
        {
            StopCoroutine(_koCountdownCoroutine); // UPGRADED
            _koCountdownCoroutine = null; // UPGRADED
        }
        StopAllTweens();
        if (cooldownText) cooldownText.gameObject.SetActive(false);
    }

    // UPGRADED: event-driven KO countdown — replaces the old per-frame Update() poll.
    private void RefreshKOCountdown() // UPGRADED
    {
        if (!HasValidMonster(_data) || !IsKO(_data)) return; // UPGRADED
        if (_koCountdownCoroutine != null) StopCoroutine(_koCountdownCoroutine); // UPGRADED
        _koCountdownCoroutine = StartCoroutine(Co_KOCountdown()); // UPGRADED
    }

    private void OnBattleFinished(BattleResult result) // UPGRADED
    {
        RefreshKOCountdown(); // UPGRADED
    }

    private System.Collections.IEnumerator Co_KOCountdown() // UPGRADED
    {
        while (HasValidMonster(_data) && IsKO(_data)) // UPGRADED
        {
            UpdateKOCountdown(); // UPGRADED
            yield return new WaitForSecondsRealtime(1f); // UPGRADED
        }
        UpdateKOCountdown(); // UPGRADED — final call clears the text when no longer KO
        _koCountdownCoroutine = null; // UPGRADED
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
        _isDirectoryRow = false;
        _directoryBrowseDefs = null;

        _allowDetail = true;
        _detailPanelOverride = null;

        _data = data;
        _def = def;

        bool isPremium = data != null && (data.isPremium || data.premiumTier > 0);

        // Icon
        if (icon)
        {
            if (def)
            {
                var s = MonsterNameFormatter.GetIcon(def, isPremium, backIcon: false);
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
                nameText.text = MonsterNameFormatter.Format(def, isPremium);
            else
                nameText.text = "Unknown";
        }

        if (idText)
            idText.text = HasValidMonster(data) ? data.monsterId : "—";

        RefreshPackBadge(def);

        // Favorites and team alerts are only shown for Directory entries; hide here.
        if (favoriteAlert)
            favoriteAlert.SetActive(false);

        if (idleTeamAlert) idleTeamAlert.SetActive(false);
        if (arenaTeamAlert) arenaTeamAlert.SetActive(false);

        ApplyState();

        RefreshKOCountdown(); // UPGRADED

        RefreshEvolutionAlert();
    }

    // ---------------------------------------------------------------------
    // Directory-specific setup
    // ---------------------------------------------------------------------

    /// <summary>
    /// Directory row setup. "captured" here means "revealed/known" in the Directory context.
    /// If not captured/revealed, row shows ??? and is not interactable.
    /// </summary>
    public void SetupForDirectory(
        MonsterDataSO def,
        OwnedMonsterData ownedData,
        bool captured,
        bool isFavorite,
        bool allowDetail,
        MonsterDetailPanelUI detailPanelOverride,
        bool isOnIdleTeam = false,
        bool isOnArenaTeam = false)
    {
        _isDirectoryRow = true;
        _directoryBrowseDefs = null; // set later by DirectoryPanelUI after it knows the final visible list

        _detailPanelOverride = detailPanelOverride;
        _allowDetail = allowDetail && captured; // cannot open detail for unrevealed

        _def = def;
        _data = captured ? ownedData : null; // unrevealed entries have no OwnedMonsterData

        bool isPremium = captured && ownedData != null && (ownedData.isPremium || ownedData.premiumTier > 0);
        bool isOwned = captured && ownedData != null;

        // Icon
        if (icon)
        {
            if (def)
            {
                var s = MonsterNameFormatter.GetIcon(def, isPremium, backIcon: false);
                if (s)
                {
                    icon.enabled = true;
                    icon.sprite = s;

                    // Silhouette effect for unrevealed.
                    // If revealed but NOT owned, reduce alpha so players can tell at a glance.
                    if (!captured)
                    {
                        icon.color = Color.black;
                    }
                    else
                    {
                        var c = Color.white;
                        if (!isOwned) c.a = 0.5f;
                        icon.color = c;
                    }
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
                nameText.text = MonsterNameFormatter.Format(def, isPremium);
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

        // Pack badge shows for revealed entries (owned OR discovered).
        if (captured && def)
            RefreshPackBadge(def);
        else
            SetPackBadgeActive(false, null);

        // Favorites icon (only for captured + feature unlocked)
        if (favoriteAlert)
        {
            bool hasFeature = FeatureUnlockManager.I &&
                              FeatureUnlockManager.I.IsUnlocked(FeatureId.Directory_Favorites);
            bool showFav = hasFeature && captured && isFavorite;
            favoriteAlert.SetActive(showFav);
            if (showFav) StartFavoritePulse();
            else StopFavoritePulse();
        }

        // Idle / arena team status badges
        if (idleTeamAlert)
            idleTeamAlert.SetActive(isOnIdleTeam && isOwned);

        if (arenaTeamAlert)
            arenaTeamAlert.SetActive(isOnArenaTeam && isOwned);

        // Arena monsters are committed — block interaction in directory.
        if (isOnArenaTeam && isOwned)
            _allowDetail = false;

        // KO / cooldown text only makes sense for owned monsters, not directory silhouettes.
        if (cooldownText)
            cooldownText.gameObject.SetActive(false);

        // Evolve alert makes no sense for directory grid rows.
        if (evolveAlert)
            evolveAlert.SetActive(false);

        ApplyState();

        RefreshKOCountdown(); // UPGRADED

        RefreshEvolutionAlert();
    }

    // ---------------------------------------------------------------------
    // Pack badge
    // ---------------------------------------------------------------------

    private void RefreshPackBadge(MonsterDataSO def)
    {
        if (!packBadgeRoot) return;

        if (!def || string.IsNullOrEmpty(def.id))
        {
            SetPackBadgeActive(false, null);
            return;
        }

        var badge = MonsterPackTagCache.GetBadge(def.id);

        bool isPackMonster = !string.IsNullOrEmpty(badge) && !badge.Equals("CORE", StringComparison.OrdinalIgnoreCase);

        if (!isPackMonster)
        {
            SetPackBadgeActive(false, null);
            return;
        }

        SetPackBadgeActive(true, badge); 
    }

    private void SetPackBadgeActive(bool active, string text)
    {
        if (packBadgeRoot) packBadgeRoot.SetActive(active);
    }

    /// <summary>
    /// Called by DirectoryPanelUI after it knows the final visible list of defs.
    /// Enables swipe-browse context in MonsterDetailPanelUI.
    /// </summary>
    public void SetDirectoryBrowseContext(IReadOnlyList<MonsterDataSO> visibleDefs)
    {
        _directoryBrowseDefs = visibleDefs;
    }

    // ---------------------------------------------------------------------
    // Interactions
    // ---------------------------------------------------------------------

    public void SetInteractable(bool on)
    {
        if (rootButton)
        {
            if (_isDirectoryRow)
                rootButton.interactable = on && _allowDetail && _def != null;
            else
                // Allow interacting with KO'd monsters so players can assign them to the team for healing.
                rootButton.interactable = on && HasValidMonster(_data) && _allowDetail;
        }

        ApplyKOVisualsOnly();
    }

    private void OnClickOpenDetails()
    {
        if (!_allowDetail)
            return;

        PunchRow();

        var panel = _detailPanelOverride ? _detailPanelOverride : detailPanel;
        if (panel == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[OwnedMonsterListItemUI] MonsterDetailPanelUI not found in scene.");
            #endif
            return;
        }

        AudioManager.I?.PlayClick();

        // Directory behavior: open by def (not OwnedMonsterData) and set browse list for swipe
        if (_isDirectoryRow)
        {
            if (_def == null) return;

            // Use the APIs that exist in MonsterDetailPanelUI
            if (_directoryBrowseDefs != null && _directoryBrowseDefs.Count > 1)
            {
                int startIndex = 0;
                for (int i = 0; i < _directoryBrowseDefs.Count; i++)
                {
                    var d = _directoryBrowseDefs[i];
                    if (d && (_def == d || (!string.IsNullOrEmpty(d.id) && d.id == _def.id)))
                    {
                        startIndex = i;
                        break;
                    }
                }

                panel.SetStarterBrowseContext(_directoryBrowseDefs, startIndex);
            }
            else
            {
                panel.ClearStarterBrowseContext();
            }

            if (HasValidMonster(_data))
                panel.ShowDirectoryOwned(_def, _data);
            else
                panel.ShowDirectory(_def);
            return;
        }

        // Owned/team behavior
        if (!HasValidMonster(_data)) return;

        panel.ShowAssign(_data);
    }

    private void ApplyState()
    {
        if (rootButton)
        {
            if (_isDirectoryRow)
                rootButton.interactable = (_def != null) && _allowDetail;
            else
                // Allow interacting with KO'd monsters so players can assign them to the team for healing.
                rootButton.interactable = HasValidMonster(_data) && _allowDetail;
        }

        ApplyKOVisualsOnly();
        RefreshEvolutionAlert();
    }

    private void ApplyKOVisualsOnly()
    {
        bool isKO = IsKO(_data);

        if (cooldownText)
            cooldownText.gameObject.SetActive(isKO);
    }

    private void UpdateKOCountdown()
    {
        if (!cooldownText) return;
        if (!HasValidMonster(_data)) { cooldownText.gameObject.SetActive(false); return; }
        if (!IsKO(_data)) { cooldownText.gameObject.SetActive(false); return; }

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
        if (show) StartEvolvePulse();
        else StopEvolvePulse();
    }

    private void PunchRow()
    {
        if (_rowPunchTweenId != -1)
        {
            LeanTween.cancel(_rowPunchTweenId);
            _rowPunchTweenId = -1;
        }

        var rt = transform as RectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;
        _rowPunchTweenId = LeanTween.scale(rt, Vector3.one * 1.04f, 0.07f)
            .setEase(LeanTweenType.easeOutQuad)
            .setLoopPingPong(1)
            .id;
    }

    private void StartFavoritePulse()
    {
        if (!favoriteAlert) return;

        StopFavoritePulse();

        var rt = favoriteAlert.transform as RectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;
        _favoritePulseTweenId = LeanTween.scale(rt, Vector3.one * 1.07f, 0.4f)
            .setEase(LeanTweenType.easeInOutSine)
            .setLoopPingPong()
            .id;
    }

    private void StopFavoritePulse()
    {
        if (_favoritePulseTweenId != -1)
        {
            LeanTween.cancel(_favoritePulseTweenId);
            _favoritePulseTweenId = -1;
        }

        if (favoriteAlert)
        {
            var rt = favoriteAlert.transform as RectTransform;
            if (rt) rt.localScale = Vector3.one;
        }
    }

    private void StartEvolvePulse()
    {
        if (!evolveAlert) return;

        StopEvolvePulse();

        var rt = evolveAlert.transform as RectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;
        _evolvePulseTweenId = LeanTween.scale(rt, Vector3.one * 1.07f, 0.44f)
            .setEase(LeanTweenType.easeInOutSine)
            .setLoopPingPong()
            .id;
    }

    private void StopEvolvePulse()
    {
        if (_evolvePulseTweenId != -1)
        {
            LeanTween.cancel(_evolvePulseTweenId);
            _evolvePulseTweenId = -1;
        }

        if (evolveAlert)
        {
            var rt = evolveAlert.transform as RectTransform;
            if (rt) rt.localScale = Vector3.one;
        }
    }

    private void StopAllTweens()
    {
        if (_rowPunchTweenId != -1)
        {
            LeanTween.cancel(_rowPunchTweenId);
            _rowPunchTweenId = -1;
        }

        StopFavoritePulse();
        StopEvolvePulse();

        var rt = transform as RectTransform;
        if (rt) rt.localScale = Vector3.one;
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
        // HP invariant: 0 = KO, >0 = usable.
        return HasValidMonster(d) && d.currentHP > 0;
    }

    private static bool IsKO(OwnedMonsterData d)
    {
        // KO is 0 HP (defensive: treat <=0 as KO).
        return HasValidMonster(d) && d.currentHP <= 0;
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