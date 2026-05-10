using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Personal Record Board for Executive Trial.
/// Overlay popup (like Rules) — shown on top of the Starter panel.
/// Displays all-time records and current month stats.
/// </summary>
public sealed class ExecutiveTrialRecordsPanelUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleTMP;

    [Header("All-Time Section")]
    [SerializeField] private GameObject allTimeSection;
    [SerializeField] private TextMeshProUGUI allTimeTMP;

    [Header("This Month Section")]
    [SerializeField] private GameObject monthSection;
    [SerializeField] private TextMeshProUGUI monthHeaderTMP;
    [SerializeField] private TextMeshProUGUI monthTMP;

    [Header("Buttons")]
    [SerializeField] private Button closeButton;

    private ExecutiveTrialRiftPanelUI _ironUI;

    private void Awake()
    {
        _ironUI = ExecutiveTrialRiftPanelUI.I;
        if (!_ironUI) _ironUI = FindFirstObjectByType<ExecutiveTrialRiftPanelUI>(FindObjectsInactive.Include);

        if (closeButton) closeButton.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (closeButton) closeButton.onClick.RemoveAllListeners();
    }

    private void OnEnable()
    {
        Refresh();
    }

    public void Refresh()
    {
        var data = ExecutiveTrialStats.Load();
        var month = ExecutiveTrialStats.GetCurrentMonthRecord();

        if (canvasGroup)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (titleTMP) titleTMP.text = "EXECUTIVE TRIAL RECORDS";

        // ── All-Time ──
        if (allTimeSection) allTimeSection.SetActive(true);
        if (allTimeTMP)
        {
            int bestFloorStd = data.bestStandardWins + 1;
            int bestFloorHc = data.bestHardcoreWins + 1;

            allTimeTMP.text =
                $"Total Runs: {data.totalRuns:N0}\n" +
                $"Total Wins: {data.totalWinsAcrossRuns:N0}\n" +
                $"Total Forfeits: {data.totalForfeits:N0}\n" +
                $"\n" +
                $"Best Streak (Standard): {data.bestStandardWins:N0}  (Floor {bestFloorStd})\n" +
                $"Best Streak (Hardcore): {data.bestHardcoreWins:N0}  (Floor {bestFloorHc})\n" +
                $"\n" +
                $"Longest Run: {FormatDuration(data.longestRunSeconds)}\n" +
                $"Most Battles in a Run: {data.mostBattlesInRun:N0}\n" +
                $"Most Damage Dealt: {data.mostDamageDealtInRun:N0}\n" +
                $"Most Damage Taken: {data.mostDamageTakenInRun:N0}\n" +
                $"Most Crits in a Run: {data.mostCritsInRun:N0}";
        }

        // ── This Month ──
        if (monthSection) monthSection.SetActive(true);
        if (monthHeaderTMP)
        {
            string label = FormatMonthLabel(month.monthKey);
            monthHeaderTMP.text = label;
        }

        if (monthTMP)
        {
            int monthBestFloorStd = month.bestStandardWins + 1;
            int monthBestFloorHc = month.bestHardcoreWins + 1;

            monthTMP.text =
                $"Runs: {month.runs:N0}\n" +
                $"Wins: {month.wins:N0}\n" +
                $"Forfeits: {month.forfeits:N0}\n" +
                $"\n" +
                $"Best Streak (Standard): {month.bestStandardWins:N0}  (Floor {monthBestFloorStd})\n" +
                $"Best Streak (Hardcore): {month.bestHardcoreWins:N0}  (Floor {monthBestFloorHc})";
        }
    }

    private void Close()
    {
        if (!_ironUI)
        {
            _ironUI = ExecutiveTrialRiftPanelUI.I;
            if (!_ironUI) _ironUI = FindFirstObjectByType<ExecutiveTrialRiftPanelUI>(FindObjectsInactive.Include);
        }

        if (_ironUI)
            _ironUI.HideRecords(immediate: true);
    }

    private static string FormatDuration(float totalSeconds)
    {
        totalSeconds = Mathf.Max(0f, totalSeconds);
        int hours = (int)(totalSeconds / 3600f);
        int minutes = (int)((totalSeconds % 3600f) / 60f);
        int seconds = (int)(totalSeconds % 60f);

        return hours > 0
            ? $"{hours:D2}:{minutes:D2}:{seconds:D2}"
            : $"{minutes:D2}:{seconds:D2}";
    }

    private static string FormatMonthLabel(string monthKey)
    {
        if (string.IsNullOrEmpty(monthKey) || monthKey.Length < 7)
            return "THIS MONTH";

        // monthKey is "yyyy-MM"
        if (DateTime.TryParseExact(monthKey, "yyyy-MM", null,
                System.Globalization.DateTimeStyles.None, out var dt))
        {
            return dt.ToString("MMMM yyyy").ToUpperInvariant();
        }

        return "THIS MONTH";
    }
}
