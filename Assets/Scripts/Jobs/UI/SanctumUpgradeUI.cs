using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System;

public class SanctumUpgradeUI : MonoBehaviour
{
    
    [Header("UI")]
    [SerializeField] private TMP_Dropdown siteDropdown;
    [SerializeField] private Button       upgradeBtn;
    [SerializeField] private TextMeshProUGUI tokenLabel;
    [SerializeField] private TextMeshProUGUI capPreviewLabel;

    [Header("Tuning")]
    [SerializeField, Min(1)] private int   flatPerToken = 50;   // +50 storage per token
    [SerializeField, Min(1)] private int   maxTokensPerSite = 10; // soft cap (optional)

    private JobSiteSO[] _sites;

    void OnEnable()
    {
        RefreshTokens();
        BuildDropdown();
        Hook();
        RefreshPreview();
    }

    void OnDisable()
    {
        if (upgradeBtn) upgradeBtn.onClick.RemoveAllListeners();
        if (siteDropdown) siteDropdown.onValueChanged.RemoveAllListeners();
    }

    void Hook()
    {
        if (upgradeBtn) upgradeBtn.onClick.AddListener(DoUpgrade);
        if (siteDropdown) siteDropdown.onValueChanged.AddListener(_ => RefreshPreview());
    }

    void BuildDropdown()
    {
        // Pull the sites from JobManager (dragged in the inspector there)
        var jm = JobManager.I;
        if (jm == null) return;

        _sites = jm.GetSitesArray(); // add helper below if you don't have one

        siteDropdown.ClearOptions();
        var options = _sites.Select(s => new TMP_Dropdown.OptionData(JobStrings.SiteName(s.jobType))).ToList();
        siteDropdown.AddOptions(options);
        siteDropdown.value = 0;
        siteDropdown.RefreshShownValue();
    }

    void RefreshTokens()
    {
        int count = ResourceBank.Get(ResourceType.BlessingTokens);
        if (tokenLabel) tokenLabel.text = $"Blessing Tokens: {count}";
        upgradeBtn.interactable = count > 0;
    }

    void RefreshPreview()
    {
        if (_sites == null || _sites.Length == 0) { if (capPreviewLabel) capPreviewLabel.text = ""; return; }
        var jm = JobManager.I;
        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];
        int current = jm.GetEffectiveStorageCap(site);
        int after   = current + flatPerToken;
        if (capPreviewLabel) capPreviewLabel.text = $"Cap: {current} → {after}";
    }

   void DoUpgrade()
    {
        if (_sites == null || _sites.Length == 0) return;

        if (!ResourceBank.TrySpend(ResourceType.BlessingTokens, 1)) { 
            RefreshTokens(); 
            return; 
        }

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];

        if (maxTokensPerSite > 0)
        {
            int currentExtra = SaveManager.Data != null ? SaveManager.Data.GetJobStorageExtra(site.jobType) : 0;
            int usedTokens   = currentExtra / Mathf.Max(1, flatPerToken);
            if (usedTokens >= maxTokensPerSite)
            {
                ResourceBank.Add(ResourceType.BlessingTokens, 1);
                return;
            }
        }

        SaveManager.Data.AddJobStorageExtra(site.jobType, flatPerToken);
        SaveManager.Save();

        RefreshTokens();
        RefreshPreview();

        GameEvents.OnJobsChanged?.Invoke(); 
    }
}
