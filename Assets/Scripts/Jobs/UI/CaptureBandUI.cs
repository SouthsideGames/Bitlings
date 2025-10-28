using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

public class CaptureBandUI : MonoBehaviour
{
    [SerializeField] private Button useBandBtn;
    [SerializeField] private TextMeshProUGUI bandsLabel;
    [SerializeField] private TextMeshProUGUI bandTimerText;
    [SerializeField, Range(0f,1f)] private float bonus = 0.25f;
    [SerializeField, Min(1)] private int durationHours = 2;
    [SerializeField] private bool consumeBandItem = true;

    Coroutine _ticker;

   void OnEnable()
    {
        Wire();
        Refresh();
        StartTicker();
        GameEvents.OnResourcesChanged += OnResourcesChanged;
    }

    void Wire()
    {
        if (useBandBtn == null) return;
        useBandBtn.onClick.RemoveAllListeners();
        useBandBtn.onClick.AddListener(OnClickUseBand);
    }

    void OnDisable()
    {
        StopTicker();
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
    }

    void OnResourcesChanged() { Refresh(); }

    void Refresh()
    {
        if (!bandsLabel || !useBandBtn) return;
        int have = ResourceBank.Get(ResourceType.CaptureBands);
        bandsLabel.text = $"CaptureBands: {have}";
        useBandBtn.interactable = !consumeBandItem || have > 0;
    }

    void OnClickUseBand()
    {
        if (consumeBandItem && !ResourceBank.TrySpend(ResourceType.CaptureBands, 1))
        {
            Refresh();
            return;
        }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, durationHours) * 3600L;

        if (SaveManager.Data.activeCaptureBands == null)
            SaveManager.Data.activeCaptureBands = new List<CaptureBandData>();
        SaveManager.Data.activeCaptureBands.Clear();
        SaveManager.Data.activeCaptureBands.Add(new CaptureBandData { bonus = Mathf.Clamp01(bonus), expireUnix = expiry });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    void StartTicker() { if (_ticker != null) StopCoroutine(_ticker); _ticker = StartCoroutine(Tick()); }
    void StopTicker() { if (_ticker != null) { StopCoroutine(_ticker); _ticker = null; } }

    IEnumerator Tick()
    {
        while (true)
        {
            if (bandTimerText)
            {
                long rem = GetSecondsRemaining();
                bandTimerText.text = rem > 0 ? FormatHMS(rem) : "No active CaptureBand";
            }
            yield return new WaitForSecondsRealtime(1f);
        }
    }

    long GetSecondsRemaining()
    {
        var list = SaveManager.Data?.activeCaptureBands;
        if (list == null || list.Count == 0) return -1;
        var cur = list[0];
        if (cur == null) return -1;
        long rem = cur.expireUnix - SaveManager.NowUnix();
        return System.Math.Max(0L, rem);
    }

    string FormatHMS(long seconds)
    {
        if (seconds < 0) return "--";
        var t = TimeSpan.FromSeconds(seconds);
        return (t.TotalHours >= 1.0) ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s" : $"{t.Minutes}m {t.Seconds}s";
    }
}
