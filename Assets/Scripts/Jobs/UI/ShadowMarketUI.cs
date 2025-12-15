using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System;

public class ShadowMarketUI : MonoBehaviour
{
    [SerializeField] private Button useOrbBtn;
    [SerializeField] private TextMeshProUGUI orbsLabel;
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("Tuning")]
    [SerializeField, Range(1f, 5f)] private float bonusMultiplier = 1.5f; 
    [SerializeField, Min(5)] private int durationMinutes = 30;

    Coroutine _ticker;

    void OnEnable()
    {
        Wire();
        RefreshCounts();
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
    }

    void OnResChanged() => RefreshCounts();

    void RefreshCounts()
    {
        int have = ResourceBank.Get(ResourceType.ShinyOrb);
        if (orbsLabel) orbsLabel.text = $"Shiny Orbs: {have}";
        if (useOrbBtn) useOrbBtn.interactable = have > 0;
    }

    void OnClickUseOrb()
    {
        if (!ResourceBank.TrySpend(ResourceType.ShinyOrb, 1))
        {
            RefreshCounts();
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

        // Clear and set one active (same pattern as Bands/Luck in your code)
        list.Clear();
        list.Add(new ShinyBoostData { bonus = Mathf.Max(1f, bonusMultiplier), expireUnix = expiry });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        RefreshCounts();
    }

    void StartTicker() { if (_ticker != null) StopCoroutine(_ticker); _ticker = StartCoroutine(Tick()); }
    void StopTicker() { if (_ticker != null) { StopCoroutine(_ticker); _ticker = null; } }

    IEnumerator Tick()
    {
        while (true)
        {
            var cur = Current();
            long rem = (cur == null) ? -1 : (cur.expireUnix - SaveManager.NowUnix());
            if (timerText)
            {
                if (rem > 0) timerText.text = FormatHMS(rem);
                else timerText.text = "No shiny boost";
            }
            yield return new WaitForSecondsRealtime(1f);
        }
    }

    ShinyBoostData Current()
    {
        var list = SaveManager.Data?.activeShinyBoosts;
        if (list == null || list.Count == 0) return null;
        var cur = list[0];
        if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
        {
            list.Clear();
            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();
            return null;
        }
        return cur;
    }

    string FormatHMS(long seconds)
    {
        seconds = Math.Max(0L, seconds);
        var t = TimeSpan.FromSeconds(seconds);
        return (t.TotalHours >= 1.0) ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s" : $"{t.Minutes}m {t.Seconds}s";
    }
}
