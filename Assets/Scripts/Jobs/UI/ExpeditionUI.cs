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
    [SerializeField] private TextMeshProUGUI seasonLabelText;       // "Expedition Packs — Season 3"
    [SerializeField] private TextMeshProUGUI seasonCountdownText;   // "Season ends in 12d 4h"

    [Header("Refs - Upcoming Teaser")]
    [SerializeField] private TextMeshProUGUI upcomingHeaderText;    // optional: "Next Season"
    [SerializeField] private Transform upcomingRoot;
    [SerializeField] private PackTeaserItemUI upcomingPrefab;

    private Coroutine _countdownRoutine;

    void OnEnable()
    {
        RefreshCurrencyHeader();
        RefreshSeasonHeader();
        BuildCurrentSeasonList();
        BuildUpcomingTeaser();

        GameEvents.OnResourcesChanged += RefreshCurrencyHeader;
        MonsterPackManager.OnPackUnlocked += OnPackUnlocked;

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

    private void OnPackUnlocked(string _)
    {
        RefreshCurrencyHeader();
        RefreshSeasonHeader();
        BuildCurrentSeasonList();
        BuildUpcomingTeaser();
    }

    private void RefreshCurrencyHeader()
    {
        int have = ResourceBank.Get(ResourceType.PackVoucher);
        if (currencyHeader)
            currencyHeader.text = $"Pack Vouchers: {have}";
    }

    private void RefreshSeasonHeader()
    {
        var mgr = MonsterPackManager.I;
        if (mgr == null) return;

        // Season label
        if (seasonLabelText)
        {
            int seasonNum = mgr.GetCurrentSeasonNumber1Based();
            seasonLabelText.text = seasonNum > 0
                ? $"Expedition Packs — Season {seasonNum}"
                : "Expedition Packs";
        }

        // Upcoming header (optional)
        if (upcomingHeaderText)
            upcomingHeaderText.text = "Next Season";
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
        var mgr = MonsterPackManager.I;
        if (mgr == null || seasonCountdownText == null)
            return;

        long endUnix = mgr.GetCurrentSeasonEndUnix();
        if (endUnix <= 0)
        {
            seasonCountdownText.text = "";
            return;
        }

        long now = SaveManager.NowUnix();
        long remaining = endUnix - now;

        if (remaining <= 0)
        {
            // On boundary, force a rebuild so the new season appears quickly
            seasonCountdownText.text = "Season ends soon";
            RefreshSeasonHeader();
            BuildCurrentSeasonList();
            BuildUpcomingTeaser();
            return;
        }

        // Format: "Season ends in 12d 4h" (and include minutes when under 1 day)
        long days = remaining / 86400L;
        long hours = (remaining % 86400L) / 3600L;
        long mins = (remaining % 3600L) / 60L;

        if (days > 0)
            seasonCountdownText.text = $"Season ends in {days}d {hours}h";
        else
            seasonCountdownText.text = $"Season ends in {hours}h {mins}m";
    }

    private void BuildCurrentSeasonList()
    {
        if (!contentRoot || !packShopPrefab) return;

        // Clear
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        var mgr = MonsterPackManager.I;
        if (mgr == null)
        {
            Debug.LogError("[ExpeditionUI] MonsterPackManager not found.");
            return;
        }

        // Always current-season list (returns current packs if seasons enabled; if misconfigured, list can be empty)
        List<MonsterPackSO> packs = mgr.GetActiveSeasonPacks();
        if (packs == null || packs.Count == 0)
            return;

        // Locked packs first
        packs.Sort((a, b) =>
        {
            bool aUnlocked = a != null && mgr.IsUnlocked(a.id);
            bool bUnlocked = b != null && mgr.IsUnlocked(b.id);
            return aUnlocked.CompareTo(bUnlocked);
        });

        foreach (var pack in packs)
        {
            if (!pack) continue;

            var row = Instantiate(packShopPrefab, contentRoot);
            row.Bind(pack, packDetailPanel);
        }
    }

    private void BuildUpcomingTeaser()
    {
        if (!upcomingRoot || !upcomingPrefab) return;

        // Clear
        for (int i = upcomingRoot.childCount - 1; i >= 0; i--)
            Destroy(upcomingRoot.GetChild(i).gameObject);

        var mgr = MonsterPackManager.I;
        if (mgr == null) return;

        var nextPacks = mgr.GetNextSeasonPacks();
        if (nextPacks == null || nextPacks.Count == 0) return;

        foreach (var pack in nextPacks)
        {
            if (!pack) continue;

            bool unlocked = mgr.IsUnlocked(pack.id);

            var item = Instantiate(upcomingPrefab, upcomingRoot);
            item.Bind(pack, unlocked);
        }
    }
}
