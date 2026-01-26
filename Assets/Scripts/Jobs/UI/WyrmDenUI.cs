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
    [SerializeField] private TextMeshProUGUI useButtonLabel;
    [SerializeField] private TextMeshProUGUI favorLabel;
    [SerializeField] private GameObject favorActiveImage;
    [SerializeField] private TextMeshProUGUI favorTimerText;

    [Header("Effect")]
    [Tooltip("0..1 where 0.25 = +25% increased encounter odds while active.")]
    [SerializeField, Range(0f, 1f)] private float bonus = 0.25f;
    [SerializeField, Min(1)] private int durationHours = 2;
    [SerializeField] private bool consumeFavorItem = true;

    Coroutine _ticker;

    void OnEnable()
    {
        Wire();
        Refresh();
        RefreshButtonLabel();
        RefreshFavorVisual();
        RefreshUseButtonVisibility();

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

    void OnResourcesChanged()
    {
        Refresh();
        RefreshButtonLabel();
        RefreshFavorVisual();
        RefreshUseButtonVisibility();
    }

    void Refresh()
    {
        if (!favorLabel || !useButton) return;

        int have = ResourceBank.Get(ResourceType.Favor);
        bool active = GetSecondsRemaining() > 0;

        useButton.interactable = (!active) && (!consumeFavorItem || have > 0);
        favorLabel.text = $"Favor: {have}";

        RefreshUseButtonVisibility();
    }

    /// <summary>
    /// If we do not have any Favor (and we consume Favor), hide the entire Use button GameObject.
    /// If consumeFavorItem is false, we always show the button.
    /// </summary>
    void RefreshUseButtonVisibility()
    {
        if (!useButton) return;

        if (SaveManager.Data == null)
        {
            useButton.gameObject.SetActive(!consumeFavorItem);
            return;
        }

        int have = ResourceBank.Get(ResourceType.Favor);
        bool shouldShow = !consumeFavorItem || have > 0;

        if (useButton.gameObject.activeSelf != shouldShow)
            useButton.gameObject.SetActive(shouldShow);
    }

    void RefreshButtonLabel()
    {
        if (!useButtonLabel) return;

        bool active = GetSecondsRemaining() > 0;
        useButtonLabel.text = active ? "Replace" : "Use";
    }

    void OnClickUse()
    {
        if (consumeFavorItem && !ResourceBank.TrySpend(ResourceType.Favor, 1))
        {
            Refresh();
            RefreshButtonLabel();
            RefreshUseButtonVisibility();
            return;
        }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, durationHours) * 3600L;

        if (SaveManager.Data.activeFavorBoosts == null)
            SaveManager.Data.activeFavorBoosts = new List<LuckBoostData>();

        SaveManager.Data.activeFavorBoosts.Clear();
        SaveManager.Data.activeFavorBoosts.Add(new LuckBoostData
        {
            bonus = Mathf.Clamp01(bonus),
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.RaiseToast("FAVOR ACTIVATED");

        RefreshButtonLabel();
        RefreshFavorVisual();
        RefreshUseButtonVisibility();
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
                favorTimerText.text = rem > 0
                    ? FormatHMS(rem)
                    : "No active Favor boost";
            }

            RefreshButtonLabel();
            RefreshFavorVisual();
            RefreshUseButtonVisibility();

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

    void RefreshFavorVisual()
    {
        if (!favorActiveImage) return;

        bool active = GetSecondsRemaining() > 0;
        favorActiveImage.SetActive(active);
    }
}
