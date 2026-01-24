using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

public class CyroLabUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button useButton;
    [SerializeField] private TextMeshProUGUI useButtonLabel;
    [SerializeField] private TextMeshProUGUI ordersLabel;
    [SerializeField] private TextMeshProUGUI orderTimerText;
    [SerializeField] private GameObject orderActiveImage;

    [Header("Effect")]
    [SerializeField, Range(0f,1f)] private float bonus = 0.25f;
    [SerializeField, Min(1)] private int durationHours = 2;
    [SerializeField] private bool consumeWorkOrderItem = true;

    Coroutine _ticker;

    void OnEnable()
    {
        Wire();
        Refresh();
        RefreshButtonLabel();
        RefreshWorkOrderVisual();
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
        if (useButton == null) return;
        useButton.onClick.RemoveAllListeners();
        useButton.onClick.AddListener(OnClickUse);
    }

    void OnResourcesChanged()
    {
        Refresh();
        RefreshButtonLabel();
        RefreshWorkOrderVisual();
    }

    void Refresh()
    {
        if (!ordersLabel || !useButton) return;

        int have = ResourceBank.Get(ResourceType.WorkOrder);
        ordersLabel.text = $"Work Orders: {have}";

        bool active = GetSecondsRemaining() > 0;
        useButton.interactable = (!active) && (!consumeWorkOrderItem || have > 0);
    }

    void OnClickUse()
    {
        if (consumeWorkOrderItem && !ResourceBank.TrySpend(ResourceType.WorkOrder, 1))
        {
            Refresh();
            RefreshButtonLabel();
            RefreshWorkOrderVisual();
            return;
        }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, durationHours) * 3600L;

        if (SaveManager.Data.activeWorkOrders == null)
            SaveManager.Data.activeWorkOrders = new List<WorkOrderData>();

        SaveManager.Data.activeWorkOrders.Clear();
        SaveManager.Data.activeWorkOrders.Add(new WorkOrderData
        {
            bonus = Mathf.Clamp01(bonus),
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();

        RefreshButtonLabel();
        RefreshWorkOrderVisual();
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
            if (orderTimerText)
            {
                long rem = GetSecondsRemaining();
                orderTimerText.text = rem > 0 ? FormatHMS(rem) : "Use Work Order";
            }

            RefreshButtonLabel();
            RefreshWorkOrderVisual();

            yield return wait;
        }
    }

    void RefreshWorkOrderVisual()
    {
        if (!orderActiveImage) return;

        bool active = GetSecondsRemaining() > 0;
        orderActiveImage.SetActive(active);
    }

    long GetSecondsRemaining()
    {
        var list = SaveManager.Data?.activeWorkOrders;
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

    void RefreshButtonLabel()
    {
        if (!useButtonLabel) return;

        bool active = GetSecondsRemaining() > 0;
        useButtonLabel.text = active ? "Replace" : "Use";
    }
}
