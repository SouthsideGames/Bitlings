using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System;

public class ShadowMarketUI : MonoBehaviour
{
    [SerializeField] private Button useOrbBtn;
    [SerializeField] private TMP_Text useOrbBtnLabel;
    [SerializeField] private TextMeshProUGUI orbsLabel;
    [SerializeField] private TextMeshProUGUI orbsTimerText;
    [SerializeField] private GameObject orbsActiveImage;

    [Header("Effect")]
    [SerializeField, Range(1f, 5f)] private float bonusMultiplier = 1.5f;
    [SerializeField, Min(5)] private int durationMinutes = 30;

    Coroutine _ticker;

    void OnEnable()
    {
        Wire();
        RefreshCounts();
        RefreshButtonLabel();
        RefreshPremiumVisual();
        RefreshUseButtonVisibility();

        StartTicker();
        GameEvents.OnResourcesChanged += OnResChanged;
    }

    void OnDisable()
    {
        StopTicker();
        GameEvents.OnResourcesChanged -= OnResChanged;
        if (useOrbBtn) useOrbBtn.onClick.RemoveAllListeners();
    }

    void Wire()
    {
        if (useOrbBtn == null) return;

        useOrbBtn.onClick.RemoveAllListeners();
        useOrbBtn.onClick.AddListener(OnClickUseOrb);

        if (useOrbBtnLabel == null)
            useOrbBtnLabel = useOrbBtn.GetComponentInChildren<TMP_Text>(true);
    }

    void OnResChanged()
    {
        RefreshCounts();
        RefreshButtonLabel();
        RefreshPremiumVisual();
        RefreshUseButtonVisibility();
    }

    void RefreshCounts()
    {
        int have = ResourceBank.Get(ResourceType.PremiumOrb);

        if (orbsLabel) orbsLabel.text = $"Premium Orbs: {have}";

        bool active = GetSecondsRemaining() > 0;
        if (useOrbBtn)
            useOrbBtn.interactable = (!active) && have > 0;

        RefreshUseButtonVisibility();
    }

    /// <summary>
    /// If we do not have any Premium Orbs, hide the entire Use button GameObject.
    /// </summary>
    void RefreshUseButtonVisibility()
    {
        if (!useOrbBtn) return;

        // Safe default: if save isn't ready, hide.
        if (SaveManager.Data == null)
        {
            useOrbBtn.gameObject.SetActive(false);
            return;
        }

        int have = ResourceBank.Get(ResourceType.PremiumOrb);
        bool shouldShow = have > 0;

        if (useOrbBtn.gameObject.activeSelf != shouldShow)
            useOrbBtn.gameObject.SetActive(shouldShow);
    }

    void RefreshButtonLabel()
    {
        if (!useOrbBtnLabel) return;

        bool active = GetSecondsRemaining() > 0;
        useOrbBtnLabel.text = active ? "Replace" : "Use";
    }

    void RefreshPremiumVisual()
    {
        if (!orbsActiveImage) return;

        bool active = GetSecondsRemaining() > 0;
        orbsActiveImage.SetActive(active);
    }

    void OnClickUseOrb()
    {
        // Only toast if we successfully activate the premium boost:
        // - Save exists
        // - Spend succeeds
        // - We actually write a new boost + save
        if (SaveManager.Data == null)
        {
            RefreshCounts();
            RefreshButtonLabel();
            RefreshPremiumVisual();
            RefreshUseButtonVisibility();
            return;
        }

        if (!ResourceBank.TrySpend(ResourceType.PremiumOrb, 1))
        {
            RefreshCounts();
            RefreshButtonLabel();
            RefreshPremiumVisual();
            RefreshUseButtonVisibility();
            return;
        }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, durationMinutes) * 60L;

        var list = SaveManager.Data.activePremiumBoosts;
        if (list == null)
        {
            SaveManager.Data.activePremiumBoosts = new System.Collections.Generic.List<PremiumBoostData>();
            list = SaveManager.Data.activePremiumBoosts;
        }

        list.Clear();
        list.Add(new PremiumBoostData
        {
            bonus = Mathf.Max(1f, bonusMultiplier),
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();

        GameEvents.RaiseToast("PREMIUM ORB ACTIVATED");

        RefreshCounts();
        RefreshButtonLabel();
        RefreshPremiumVisual();
        RefreshUseButtonVisibility();
    }

    void StartTicker()
    {
        if (_ticker != null) StopCoroutine(_ticker);
        _ticker = StartCoroutine(Tick());
    }

    void StopTicker()
    {
        if (_ticker != null)
        {
            StopCoroutine(_ticker);
            _ticker = null;
        }
    }

    IEnumerator Tick()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            long rem = GetSecondsRemaining();

            if (orbsTimerText)
                orbsTimerText.text = rem > 0 ? FormatHMS(rem) : "No premium boost";

            RefreshButtonLabel();
            RefreshPremiumVisual();
            RefreshUseButtonVisibility();

            yield return wait;
        }
    }

    long GetSecondsRemaining()
    {
        var list = SaveManager.Data?.activePremiumBoosts;
        if (list == null || list.Count == 0) return -1;

        var cur = list[0];
        if (cur == null) return -1;

        long rem = cur.expireUnix - SaveManager.NowUnix();
        if (rem <= 0)
        {
            list.Clear();
            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();
            return -1;
        }

        return rem;
    }

    string FormatHMS(long seconds)
    {
        seconds = Math.Max(0L, seconds);
        var t = TimeSpan.FromSeconds(seconds);
        return (t.TotalHours >= 1.0)
            ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
            : $"{t.Minutes}m {t.Seconds}s";
    }
}
