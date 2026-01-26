using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System;
using System.Collections.Generic;

public class SanctumUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button useButton;
    [SerializeField] private TextMeshProUGUI ShardLabel;
    [SerializeField] private TextMeshProUGUI capPreviewLabel;
    [SerializeField] private TextMeshProUGUI timerLabel;
    [SerializeField] private TMP_Text useButtonLabel;

    [Header("Effect")]
    [SerializeField] private TMP_Dropdown siteDropdown;
    [SerializeField] private Image siteIcon;

    [Serializable]
    public struct JobTypeIcon
    {
        public JobType jobType;
        public Sprite sprite;
    }

    [SerializeField] private List<JobTypeIcon> jobTypeIcons = new();
    private Dictionary<JobType, Sprite> _iconMap;

    [Header("Tuning")]
    [SerializeField, Min(1)] private int flatPerToken = 50;
    [SerializeField, Min(1)] private int maxTokensPerSite = 10;
    [SerializeField, Min(0.1f)] private float blessingDurationMinutes = 30f;

    private JobSiteSO[] _sites;

    void OnEnable()
    {
        BuildIconMap();
        BuildDropdown();
        Hook();

        RefreshShards();
        RefreshPreview();
        RefreshTimer();
        RefreshSiteIcon();
        RefreshButtonLabel();
        RefreshUseButtonVisibility();

        GameEvents.OnResourcesChanged += OnResourcesChanged;
        GameEvents.OnJobsChanged += OnJobsChanged;
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
        GameEvents.OnJobsChanged -= OnJobsChanged;

        if (useButton) useButton.onClick.RemoveAllListeners();
        if (siteDropdown) siteDropdown.onValueChanged.RemoveAllListeners();
    }

    void Update()
    {
        RefreshTimer();
        RefreshButtonLabel();
        RefreshUseButtonVisibility();
    }

    void OnResourcesChanged()
    {
        RefreshShards();
        RefreshButtonLabel();
        RefreshUseButtonVisibility();
    }

    void OnJobsChanged()
    {
        RefreshPreview();
        RefreshTimer();
        RefreshButtonLabel();
        RefreshSiteIcon();
        RefreshUseButtonVisibility();
    }

    void Hook()
    {
        if (useButton)
        {
            useButton.onClick.RemoveAllListeners();
            useButton.onClick.AddListener(DoUpgrade);

            if (useButtonLabel == null)
                useButtonLabel = useButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (siteDropdown)
        {
            siteDropdown.onValueChanged.RemoveAllListeners();
            siteDropdown.onValueChanged.AddListener(_ =>
            {
                RefreshPreview();
                RefreshTimer();
                RefreshButtonLabel();
                RefreshSiteIcon();
                RefreshUseButtonVisibility();
            });
        }
    }

    void RefreshButtonLabel()
    {
        if (!useButtonLabel) return;
        bool active = GetBlessingSecondsRemainingForSelected() > 0.5f;
        useButtonLabel.text = active ? "Replace" : "Use";
    }

    /// <summary>
    /// If we do not have any Blessing Scales, hide the entire Use button GameObject.
    /// </summary>
    void RefreshUseButtonVisibility()
    {
        if (!useButton) return;

        if (SaveManager.Data == null)
        {
            useButton.gameObject.SetActive(false);
            return;
        }

        int count = ResourceBank.Get(ResourceType.BlessingScale);
        bool shouldShow = count > 0;

        if (useButton.gameObject.activeSelf != shouldShow)
            useButton.gameObject.SetActive(shouldShow);
    }

    float GetBlessingSecondsRemainingForSelected()
    {
        if (_sites == null || _sites.Length == 0) return 0f;
        var jm = JobManager.I;
        if (!jm) return 0f;

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];
        return jm.GetBlessingSecondsRemaining(site.jobType);
    }

    void BuildIconMap()
    {
        _iconMap = new Dictionary<JobType, Sprite>();
        foreach (var entry in jobTypeIcons)
            if (entry.sprite != null)
                _iconMap[entry.jobType] = entry.sprite;
    }

    void BuildDropdown()
    {
        var jm = JobManager.I;
        if (!jm || siteDropdown == null) return;

        _sites = jm.GetSitesArray();

        siteDropdown.ClearOptions();
        if (_sites == null || _sites.Length == 0)
        {
            RefreshSiteIcon();
            return;
        }

        var options = _sites
            .Select(s => new TMP_Dropdown.OptionData(JobStrings.SiteName(s.jobType)))
            .ToList();

        siteDropdown.AddOptions(options);
        siteDropdown.value = 0;
        siteDropdown.RefreshShownValue();

        RefreshSiteIcon();
    }

    void RefreshSiteIcon()
    {
        if (siteIcon == null) return;

        if (_sites == null || _sites.Length == 0)
        {
            siteIcon.gameObject.SetActive(false);
            return;
        }

        int idx = Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1);
        var site = _sites[idx];

        if (_iconMap != null && _iconMap.TryGetValue(site.jobType, out var sprite) && sprite != null)
        {
            siteIcon.sprite = sprite;
            siteIcon.gameObject.SetActive(true);
        }
        else
        {
            siteIcon.gameObject.SetActive(false);
        }
    }

    void RefreshShards()
    {
        int count = ResourceBank.Get(ResourceType.BlessingScale);
        if (ShardLabel) ShardLabel.text = $"Blessing Scales: {count}";

        if (useButton) useButton.interactable = count > 0;

        RefreshUseButtonVisibility();
    }

    void RefreshPreview()
    {
        if (capPreviewLabel == null) return;
        if (_sites == null || _sites.Length == 0) { capPreviewLabel.text = ""; return; }

        var jm = JobManager.I;
        if (!jm) { capPreviewLabel.text = ""; return; }

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];
        int current = jm.GetEffectiveStorageCap(site);
        int after = current + flatPerToken;

        capPreviewLabel.text = $"Cap: {current} → {after}";
    }

    void RefreshTimer()
    {
        if (timerLabel == null || _sites == null || _sites.Length == 0) return;

        var jm = JobManager.I;
        if (!jm) { timerLabel.text = ""; return; }

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];

        float seconds = jm.GetBlessingSecondsRemaining(site.jobType);
        if (seconds <= 0.5f)
        {
            timerLabel.text = "No active blessing";
            return;
        }

        TimeSpan ts = TimeSpan.FromSeconds(seconds);
        timerLabel.text = ts.TotalHours >= 1
            ? $"Blessing: {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"Blessing: {ts.Minutes:00}:{ts.Seconds:00}";
    }

    void DoUpgrade()
    {
        if (_sites == null || _sites.Length == 0) return;

        // Save guard (match other UIs: no toast if save isn't ready)
        if (SaveManager.Data == null)
        {
            RefreshShards();
            RefreshButtonLabel();
            RefreshUseButtonVisibility();
            return;
        }

        if (!ResourceBank.TrySpend(ResourceType.BlessingScale, 1))
        {
            RefreshShards();
            RefreshButtonLabel();
            RefreshUseButtonVisibility();
            return;
        }

        var jm = JobManager.I;
        if (!jm)
        {
            // Refund
            ResourceBank.Add(ResourceType.BlessingScale, 1);
            RefreshShards();
            RefreshButtonLabel();
            RefreshUseButtonVisibility();
            return;
        }

        var site = _sites[Mathf.Clamp(siteDropdown.value, 0, _sites.Length - 1)];

        if (maxTokensPerSite > 0)
        {
            int activeExtra = jm.GetTemporaryStorageBonus(site.jobType);
            int usedTokens = activeExtra / Mathf.Max(1, flatPerToken);
            if (usedTokens >= maxTokensPerSite)
            {
                // Refund
                ResourceBank.Add(ResourceType.BlessingScale, 1);
                RefreshShards();
                RefreshButtonLabel();
                RefreshUseButtonVisibility();
                return;
            }
        }

        float durationSeconds = blessingDurationMinutes * 60f;
        jm.ApplyTemporaryStorageBlessing(site.jobType, flatPerToken, durationSeconds);

        RefreshShards();
        RefreshPreview();
        RefreshTimer();
        RefreshButtonLabel();
        RefreshSiteIcon();
        RefreshUseButtonVisibility();

        GameEvents.OnJobsChanged?.Invoke();

        GameEvents.RaiseToast("BLESSING ACTIVATED"); // or "BLESSING SCALE ACTIVATED"
    }

}
