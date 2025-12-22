using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text;

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
    [SerializeField] private TextMeshProUGUI upcomingPacksText;  

    private Coroutine _countdownRoutine;

    void OnEnable()
    {
        RefreshCurrencyHeader();
        RefreshSeasonHeader();
        BuildCurrentSeasonList();
        BuildUpcomingTeaserText();

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
        BuildUpcomingTeaserText();
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

        // Season label (prefer name, fallback to number)
        if (seasonLabelText)
        {
            string name = mgr.GetCurrentSeasonName();
            int seasonNum = mgr.GetCurrentSeasonNumber1Based();

            if (!string.IsNullOrEmpty(name))
                seasonLabelText.text = $"Expedition Packs — {name}";
            else if (seasonNum > 0)
                seasonLabelText.text = $"Expedition Packs — Season {seasonNum}";
            else
                seasonLabelText.text = "Expedition Packs";
        }

        // Upcoming header
        if (upcomingHeaderText)
        {
            string nextName = mgr.GetNextSeasonName();
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
            seasonCountdownText.text = "Season ends soon";
            RefreshSeasonHeader();
            BuildCurrentSeasonList();
            BuildUpcomingTeaserText();
            return;
        }

        // "Season ends in 12d 4h" (minutes if under 1 day)
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

        // REQUIRED: seasonal list
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

    private void BuildUpcomingTeaserText()
    {
        if (upcomingPacksText == null) return;

        var mgr = MonsterPackManager.I;
        if (mgr == null) { upcomingPacksText.text = ""; return; }

        var nextPacks = mgr.GetNextSeasonPacks();
        if (nextPacks == null || nextPacks.Count == 0)
        {
            upcomingPacksText.text = "";
            return;
        }

        // Text-only: "PackName — Rarity"
        var sb = new StringBuilder(256);

        for (int i = 0; i < nextPacks.Count; i++)
        {
            var pack = nextPacks[i];
            if (!pack) continue;

            string rarity = string.IsNullOrEmpty(pack.rarityLabel) ? "Unknown" : pack.rarityLabel;
            sb.Append(pack.displayName).Append(" — ").Append(rarity);

            if (i < nextPacks.Count - 1)
                sb.AppendLine();
        }

        upcomingPacksText.text = sb.ToString();
    }
}
