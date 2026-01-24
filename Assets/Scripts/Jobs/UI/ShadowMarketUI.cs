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
        RefreshShinyVisual();
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
        RefreshShinyVisual();
    }

    void RefreshCounts()
    {
        int have = ResourceBank.Get(ResourceType.ShinyOrb);

        if (orbsLabel) orbsLabel.text = $"Shiny Orbs: {have}";

        bool active = GetSecondsRemaining() > 0;
        if (useOrbBtn)
            useOrbBtn.interactable = (!active) && have > 0;
    }

    void RefreshButtonLabel()
    {
        if (!useOrbBtnLabel) return;

        bool active = GetSecondsRemaining() > 0;
        useOrbBtnLabel.text = active ? "Replace" : "Use";
    }

    void RefreshShinyVisual()
    {
        if (!orbsActiveImage) return;

        bool active = GetSecondsRemaining() > 0;
        orbsActiveImage.SetActive(active);
    }

    void OnClickUseOrb()
    {
        if (!ResourceBank.TrySpend(ResourceType.ShinyOrb, 1))
        {
            RefreshCounts();
            RefreshButtonLabel();
            RefreshShinyVisual();
            return;
        }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, durationMinutes) * 60L;

        var list = SaveManager.Data.activeShinyBoosts;
        if (list == null)
        {
            SaveManager.Data.activeShinyBoosts = new System.Collections.Generic.List<ShinyBoostData>();
            list = SaveManager.Data.activeShinyBoosts;
        }

        list.Clear();
        list.Add(new ShinyBoostData
        {
            bonus = Mathf.Max(1f, bonusMultiplier),
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();

        RefreshCounts();
        RefreshButtonLabel();
        RefreshShinyVisual();
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
                orbsTimerText.text = rem > 0 ? FormatHMS(rem) : "No shiny boost";

            RefreshButtonLabel();
            RefreshShinyVisual();

            yield return wait;
        }
    }

    long GetSecondsRemaining()
    {
        var list = SaveManager.Data?.activeShinyBoosts;
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
