// Scripts/Jobs/LuckBoosterUI.cs
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

public class LuckBoosterUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button useLuckBtn;
    [SerializeField] private TextMeshProUGUI luckLabel;
    [SerializeField] private TextMeshProUGUI luckTimerText;

    [Header("Effect")]
    [Tooltip("0..1 where 0.25 = +25% increased encounter odds while active.")]
    [SerializeField, Range(0f,1f)] private float bonus = 0.25f;

    [Tooltip("How long the boost lasts (CaptureBand-style hours).")]
    [SerializeField, Min(1)] private int durationHours = 2;

    [SerializeField] private bool consumeLuckItem = true;

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
        if (!useLuckBtn) return;
        useLuckBtn.onClick.RemoveAllListeners();
        useLuckBtn.onClick.AddListener(OnClickUseLuck);
    }

    void OnResourcesChanged() => Refresh();

    void Refresh()
    {
        if (!luckLabel || !useLuckBtn) return;
        int have = ResourceBank.Get(ResourceType.Favor);

        bool active = GetSecondsRemaining() > 0;
        useLuckBtn.interactable = (!active) && (!consumeLuckItem || have > 0);

        luckLabel.text = $"Luck: {have}";
    }

    void OnClickUseLuck()
    {
        if (consumeLuckItem && !ResourceBank.TrySpend(ResourceType.Favor, 1))
        {
            Refresh();
            return;
        }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, durationHours) * 3600L;

        if (SaveManager.Data.activeLuckBoosts == null)
            SaveManager.Data.activeLuckBoosts = new List<LuckBoostData>();

        // Same behavior as CaptureBand: keep a single active entry
        SaveManager.Data.activeLuckBoosts.Clear();
        SaveManager.Data.activeLuckBoosts.Add(new LuckBoostData
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
            if (luckTimerText)
            {
                long rem = GetSecondsRemaining();
                luckTimerText.text = rem > 0 ? FormatHMS(rem) : "No active Luck";
            }
            yield return wait;
        }
    }

    long GetSecondsRemaining()
    {
        var list = SaveManager.Data?.activeLuckBoosts;
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
