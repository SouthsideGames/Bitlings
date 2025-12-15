// Scripts/Jobs/LuckBoosterUI.cs
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

public class WyrmDenUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button useButton;
    [SerializeField] private TextMeshProUGUI favorLabel;
    [SerializeField] private TextMeshProUGUI favorTimerText;

    [Header("Effect")]
    [Tooltip("0..1 where 0.25 = +25% increased encounter odds while active.")]
    [SerializeField, Range(0f,1f)] private float bonus = 0.25f;

    [Tooltip("How long the boost lasts (CaptureBand-style hours).")]
    [SerializeField, Min(1)] private int durationHours = 2;

    [SerializeField] private bool consumeFavorItem = true;

    Coroutine _ticker;

    void OnEnable()
    {
        Wire();
        Refresh();
        StartTicker();
        GameEvents.OnResourcesChanged += OnResourcesChanged;
    }

    void OnDisable()
    {
        StopTicker();
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
    }

    void Wire()
    {
        if (!useButton) return;
        useButton.onClick.RemoveAllListeners();
        useButton.onClick.AddListener(OnClickUse);
    }

    void OnResourcesChanged() => Refresh();

    void Refresh()
    {
        if (!favorLabel || !useButton) return;
        int have = ResourceBank.Get(ResourceType.Favor);

        bool active = GetSecondsRemaining() > 0;
        useButton.interactable = (!active) && (!consumeFavorItem || have > 0);

        favorLabel.text = $"Favor: {have}";
    }

    void OnClickUse()
    {
        if (consumeFavorItem && !ResourceBank.TrySpend(ResourceType.Favor, 1))
        {
            Refresh();
            return;
        }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, durationHours) * 3600L;

        if (SaveManager.Data.activeFavorBoosts == null)
            SaveManager.Data.activeFavorBoosts = new List<LuckBoostData>();

        // Same behavior as CaptureBand: keep a single active entry
        SaveManager.Data.activeFavorBoosts.Clear();
        SaveManager.Data.activeFavorBoosts.Add(new LuckBoostData
        {
            bonus = Mathf.Clamp01(bonus),
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    void StartTicker()
    {
        if (_ticker != null) StopCoroutine(_ticker);
        _ticker = StartCoroutine(Tick());
    }

    void StopTicker()
    {
        if (_ticker != null) { StopCoroutine(_ticker); _ticker = null; }
    }

    IEnumerator Tick()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            if (favorTimerText)
            {
                long rem = GetSecondsRemaining();
                favorTimerText.text = rem > 0 ? FormatHMS(rem) : "No active Favor";
            }
            yield return wait;
        }
    }

    long GetSecondsRemaining()
    {
        var list = SaveManager.Data?.activeFavorBoosts;
        if (list == null || list.Count == 0) return -1;
        var cur = list[0];
        if (cur == null) return -1;
        long rem = cur.expireUnix - SaveManager.NowUnix();
        return Math.Max(0L, rem);
    }

    string FormatHMS(long seconds)
    {
        if (seconds < 0) return "--";
        var t = TimeSpan.FromSeconds(seconds);
        return (t.TotalHours >= 1.0)
            ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
            : $"{t.Minutes}m {t.Seconds}s";
    }
}
