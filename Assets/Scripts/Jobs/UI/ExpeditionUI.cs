using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class ExpeditionUI : MonoBehaviour
{
    [Header("Refs - Current Season Shop")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private PackShopItemUI packShopPrefab;
    [SerializeField] private PackDetailPanelUI packDetailPanel;

    [Header("Refs - Header")]
    [SerializeField] private TextMeshProUGUI currencyHeader;
    [SerializeField] private TextMeshProUGUI seasonLabelText;
    [SerializeField] private TextMeshProUGUI seasonCountdownText;

    [Header("Refs - Upcoming Teaser")]
    [SerializeField] private TextMeshProUGUI upcomingHeaderText;

    [Header("Refs - Manager")]
    [Tooltip("Assign the MonsterPackManager from the scene here (preferred).")]
    [SerializeField] private MonsterPackManager packManager;

    [Header("Countdown Warning")]
    [SerializeField, Min(1)] private int warningDaysThreshold = 10;

    private Coroutine _countdownRoutine;

    private Color _countdownBaseColor;
    private bool _cachedCountdownColor;

    private bool _loggedMissingMgrOnce;

    void OnEnable()
    {
        CacheCountdownBaseColor();

        RefreshCurrencyHeader();

        // Resolve manager before doing any season UI work
        if (!EnsureManager())
        {
            LogMissingManagerOnce();
            return;
        }

        RefreshSeasonHeader();
        BuildCurrentSeasonList();

        GameEvents.OnResourcesChanged += RefreshCurrencyHeader;
        MonsterPackManager.OnPackUnlocked += OnPackUnlocked;

        if (_countdownRoutine == null)
            _countdownRoutine = StartCoroutine(SeasonCountdownLoop());
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= RefreshCurrencyHeader;
        MonsterPackManager.OnPackUnlocked -= OnPackUnlocked;

        if (_countdownRoutine != null)
        {
            StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }
    }

    private void CacheCountdownBaseColor()
    {
        if (_cachedCountdownColor) return;
        if (seasonCountdownText == null) return;

        _countdownBaseColor = seasonCountdownText.color;
        _cachedCountdownColor = true;
    }

    private bool EnsureManager()
    {
        // Preferred: inspector reference
        if (packManager != null)
            return true;

        packManager = MonsterPackManager.I;

        return packManager != null;
    }

    private void LogMissingManagerOnce()
    {
        if (_loggedMissingMgrOnce) return;
        _loggedMissingMgrOnce = true;

        Debug.LogError(
            "[ExpeditionUI] MonsterPackManager reference is missing.\n" +
            "Fix: Assign the MonsterPackManager in the ExpeditionUI inspector (preferred).\n" +
            "If you rely on MonsterPackManager.I, ensure it initializes before this UI enables."
        );
    }

    private void OnPackUnlocked(string _)
    {
        if (!EnsureManager())
        {
            LogMissingManagerOnce();
            return;
        }

        RefreshCurrencyHeader();
        RefreshSeasonHeader();
        BuildCurrentSeasonList();
    }

    private void RefreshCurrencyHeader()
    {
        int have = ResourceBank.Get(ResourceType.PackVoucher);
        if (currencyHeader)
            currencyHeader.text = $"Pack Vouchers: {have}";
    }

    private void RefreshSeasonHeader()
    {
        if (!EnsureManager()) return;

        // Season label (prefer name, fallback to number)
        if (seasonLabelText)
        {
            string name = packManager.GetCurrentSeasonName();
            int seasonNum = packManager.GetCurrentSeasonNumber1Based();

            if (!string.IsNullOrEmpty(name))
                seasonLabelText.text = $"Expedition Packs — {name}";
            else if (seasonNum > 0)
                seasonLabelText.text = $"Expedition Packs — Season {seasonNum}";
            else
                seasonLabelText.text = "Expedition Packs";
        }

        // Upcoming header (keep if you still want a "Next Season" label)
        if (upcomingHeaderText)
        {
            string nextName = packManager.GetNextSeasonName();
            upcomingHeaderText.text = string.IsNullOrEmpty(nextName) ? "Next Season" : $"Next Season — {nextName}";
        }
    }

    private IEnumerator SeasonCountdownLoop()
    {
        var wait = new WaitForSeconds(1f);

        while (true)
        {
            UpdateCountdownText();
            yield return wait;
        }
    }

    private void UpdateCountdownText()
    {
        if (!EnsureManager() || seasonCountdownText == null)
            return;

        long endUnix = packManager.GetCurrentSeasonEndUnix();
        if (endUnix <= 0)
        {
            seasonCountdownText.text = "";
            RestoreCountdownColor();
            return;
        }

        long now = SaveManager.NowUnix();
        long remaining = endUnix - now;

        if (remaining <= 0)
        {
            seasonCountdownText.text = "Season ends soon";
            seasonCountdownText.color = Color.red;

            RefreshSeasonHeader();
            BuildCurrentSeasonList();
            return;
        }

        // Warning color if < threshold days remaining
        long warningSeconds = Mathf.Max(1, warningDaysThreshold) * 86400L;
        if (remaining < warningSeconds)
            seasonCountdownText.color = Color.red;
        else
            RestoreCountdownColor();

        // "Season ends in 12d 4h" (minutes if under 1 day)
        long days = remaining / 86400L;
        long hours = (remaining % 86400L) / 3600L;
        long mins = (remaining % 3600L) / 60L;

        if (days > 0)
            seasonCountdownText.text = $"Season ends in {days}d {hours}h";
        else
            seasonCountdownText.text = $"Season ends in {hours}h {mins}m";
    }

    private void RestoreCountdownColor()
    {
        if (!_cachedCountdownColor || seasonCountdownText == null) return;
        seasonCountdownText.color = _countdownBaseColor;
    }

    private void BuildCurrentSeasonList()
    {
        if (!contentRoot || !packShopPrefab) return;
        if (!EnsureManager()) { LogMissingManagerOnce(); return; }

        // Clear
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        // REQUIRED: seasonal list
        List<MonsterPackSO> packs = packManager.GetActiveSeasonPacks();
        if (packs == null || packs.Count == 0)
            return;

        // Locked packs first
        packs.Sort((a, b) =>
        {
            bool aUnlocked = a != null && packManager.IsUnlocked(a.id);
            bool bUnlocked = b != null && packManager.IsUnlocked(b.id);
            return aUnlocked.CompareTo(bUnlocked);
        });

        foreach (var pack in packs)
        {
            if (!pack) continue;

            var row = Instantiate(packShopPrefab, contentRoot);
            row.Bind(pack, packDetailPanel);
        }
    }
}
