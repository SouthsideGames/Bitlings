using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System;

public class SanctumUpgradeUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Dropdown      siteDropdown;
    [SerializeField] private Button            upgradeBtn;
    [SerializeField] private TextMeshProUGUI   tokenLabel;
    [SerializeField] private TextMeshProUGUI   capPreviewLabel;
    [SerializeField] private TextMeshProUGUI   timerLabel;   // NEW: shows remaining blessing time

    [Header("Tuning")]
    [SerializeField, Min(1)] private int   flatPerToken = 50;       // +50 storage per token
    [SerializeField, Min(1)] private int   maxTokensPerSite = 10;   // max active tokens per site
    [SerializeField, Min(0.1f)] private float blessingDurationMinutes = 30f; // how long a token lasts

    private JobSiteSO[] _sites;

    void OnEnable()
    {
        RefreshTokens();
        BuildDropdown();
        Hook();
        RefreshPreview();
        RefreshTimer();  // show correct timer immediately
    }

    void OnDisable()
    {
        if (upgradeBtn)    upgradeBtn.onClick.RemoveAllListeners();
        if (siteDropdown)  siteDropdown.onValueChanged.RemoveAllListeners();
    }

    void Update()
    {
        // Live countdown while Sanctum panel is open
        RefreshTimer();
    }

    void Hook()
    {
        if (upgradeBtn)   upgradeBtn.onClick.AddListener(DoUpgrade);
        if (siteDropdown) siteDropdown.onValueChanged.AddListener(_ =>
        {
            RefreshPreview();
            RefreshTimer();
        });
    }

    void BuildDropdown()
    {
        var jm = JobManager.I;
        if (!jm || siteDropdown == null) return;

        _sites = jm.GetSitesArray(); // JobManager helper

        siteDropdown.ClearOptions();
        if (_sites == null || _sites.Length == 0) return;

        var options = _sites
            .Select(s => new TMP_Dropdown.OptionData(JobStrings.SiteName(s.jobType)))
            .ToList();

        siteDropdown.AddOptions(options);
        siteDropdown.value = 0;
        siteDropdown.RefreshShownValue();
    }

    void RefreshTokens()
    {
        int count = ResourceBank.Get(ResourceType.BlessingTokens);
        if (tokenLabel) tokenLabel.text = $"Blessing Tokens: {count}";
        if (upgradeBtn) upgradeBtn.interactable = count > 0;
    }

    void RefreshPreview()
    {
        if (capPreviewLabel == null) return;
        if (_sites == null || _sites.Length == 0)
        {
            capPreviewLabel.text = "";
            return;
        }

        var jm = JobManager.I;
        if (!jm) { capPreviewLabel.text = ""; return; }

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];

        // Current cap already includes any active temporary blessings via JobManager
        int current = jm.GetEffectiveStorageCap(site);

        // Preview simply adds one more token’s worth
        int after = current + flatPerToken;
        capPreviewLabel.text = $"Cap: {current} → {after}";
    }

    void RefreshTimer()
    {
        if (timerLabel == null || _sites == null || _sites.Length == 0) return;

        var jm = JobManager.I;
        if (!jm)
        {
            timerLabel.text = "";
            return;
        }

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];

        float seconds = jm.GetBlessingSecondsRemaining(site.jobType);
        if (seconds <= 0.5f)
        {
            timerLabel.text = "No active blessing";
            return;
        }

        TimeSpan ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
        {
            timerLabel.text = $"Blessing: {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }
        else
        {
            timerLabel.text = $"Blessing: {ts.Minutes:00}:{ts.Seconds:00}";
        }
    }

    void DoUpgrade()
    {
        if (_sites == null || _sites.Length == 0) return;

        // Spend token first
        if (!ResourceBank.TrySpend(ResourceType.BlessingTokens, 1))
        {
            RefreshTokens();
            return;
        }

        var jm = JobManager.I;
        if (!jm)
        {
            // Refund if something went wrong
            ResourceBank.Add(ResourceType.BlessingTokens, 1);
            RefreshTokens();
            return;
        }

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];

        // Respect per-site token cap based on *active* temporary bonus
        if (maxTokensPerSite > 0)
        {
            int activeExtra = jm.GetTemporaryStorageBonus(site.jobType);   // NEW API
            int usedTokens  = activeExtra / Mathf.Max(1, flatPerToken);
            if (usedTokens >= maxTokensPerSite)
            {
                // Refund and bail
                ResourceBank.Add(ResourceType.BlessingTokens, 1);
                RefreshTokens();
                return;
            }
        }

        // Apply temporary blessing (runtime-only; duration in seconds)
        float durationSeconds = blessingDurationMinutes * 60f;
        jm.ApplyTemporaryStorageBlessing(site.jobType, flatPerToken, durationSeconds);

        RefreshTokens();
        RefreshPreview();
        RefreshTimer();

        GameEvents.OnJobsChanged?.Invoke();
    }
}
