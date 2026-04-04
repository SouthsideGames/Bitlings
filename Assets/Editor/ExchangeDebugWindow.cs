#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ExchangeDebugWindow : EditorWindow
{
    private Vector2 _scroll;
    private string _searchFilter = "";
    private bool _showOnlyChanged;
    private int _simDays = 1;

    [MenuItem("Bitlings/Exchange/Debug Window")]
    public static void Open()
    {
        GetWindow<ExchangeDebugWindow>("Exchange Debug").Show();
    }

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the Exchange debugger.", MessageType.Info);
            return;
        }

        if (ExchangeManager.I == null)
        {
            EditorGUILayout.HelpBox("ExchangeManager not found in scene.", MessageType.Warning);
            return;
        }

        DrawActions();
        EditorGUILayout.Space(8);
        DrawFilters();
        EditorGUILayout.Space(4);
        DrawMarketTable();
    }

    private void DrawActions()
    {
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Force Recalculate", GUILayout.Height(28)))
        {
            ExchangeManager.I.RecalculateAll();
            Debug.Log("[ExchangeDebug] Forced recalculation.");
        }

        if (GUILayout.Button("Reset All to Base", GUILayout.Height(28)))
        {
            // Use reflection to call private ResetAllValuesToBase
            var method = typeof(ExchangeManager).GetMethod("ResetAllValuesToBase",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(ExchangeManager.I, null);
                Debug.Log("[ExchangeDebug] Reset all values to base.");
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        _simDays = EditorGUILayout.IntSlider("Simulate Days", _simDays, 1, 30);
        if (GUILayout.Button($"Simulate {_simDays}d", GUILayout.Width(100), GUILayout.Height(18)))
        {
            SimulateDays(_simDays);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Log All Values", GUILayout.Height(22)))
        {
            LogAllValues();
        }
        if (GUILayout.Button("Log Save Data", GUILayout.Height(22)))
        {
            LogSaveData();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawFilters()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
        _searchFilter = EditorGUILayout.TextField(_searchFilter);
        _showOnlyChanged = EditorGUILayout.ToggleLeft("Changed only", _showOnlyChanged, GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMarketTable()
    {
        var allStates = ExchangeManager.I.AllStates;
        if (allStates == null || allStates.Count == 0)
        {
            EditorGUILayout.HelpBox("No market states yet. Try Force Recalculate.", MessageType.Info);
            return;
        }

        // Header
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("Species", EditorStyles.miniLabel, GUILayout.Width(140));
        EditorGUILayout.LabelField("Base", EditorStyles.miniLabel, GUILayout.Width(50));
        EditorGUILayout.LabelField("Current", EditorStyles.miniLabel, GUILayout.Width(60));
        EditorGUILayout.LabelField("Prev", EditorStyles.miniLabel, GUILayout.Width(50));
        EditorGUILayout.LabelField("Delta", EditorStyles.miniLabel, GUILayout.Width(50));
        EditorGUILayout.LabelField("Trend", EditorStyles.miniLabel, GUILayout.Width(50));
        EditorGUILayout.LabelField("Demand", EditorStyles.miniLabel, GUILayout.Width(55));
        EditorGUILayout.LabelField("Broker", EditorStyles.miniLabel, GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        bool hasFilter = !string.IsNullOrWhiteSpace(_searchFilter);

        foreach (var kv in allStates)
        {
            var state = kv.Value;
            if (state == null) continue;

            var def = MonsterCatalog.GetById(state.speciesId);
            string displayName = def != null ? def.displayName : state.speciesId;
            int baseVal = def != null ? def.baseMarketValue : 0;

            // Filter
            if (hasFilter && displayName.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            int delta = state.currentValue - state.previousValue;
            if (_showOnlyChanged && delta == 0) continue;

            // Color the row based on delta
            Color rowColor = delta > 0 ? new Color(0.2f, 0.8f, 0.2f, 0.15f)
                           : delta < 0 ? new Color(0.9f, 0.2f, 0.2f, 0.15f)
                           : Color.clear;

            var rect = EditorGUILayout.BeginHorizontal();
            if (rowColor != Color.clear)
                EditorGUI.DrawRect(rect, rowColor);

            EditorGUILayout.LabelField(displayName, GUILayout.Width(140));
            EditorGUILayout.LabelField(baseVal.ToString(), GUILayout.Width(50));
            EditorGUILayout.LabelField(state.currentValue.ToString(), EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField(state.previousValue.ToString(), GUILayout.Width(50));

            string deltaStr = delta > 0 ? $"+{delta}" : delta.ToString();
            EditorGUILayout.LabelField(deltaStr, GUILayout.Width(50));

            string trendStr = state.trend == TrendDirection.Rising ? "▲"
                            : state.trend == TrendDirection.Falling ? "▼" : "→";
            EditorGUILayout.LabelField(trendStr, GUILayout.Width(50));

            EditorGUILayout.LabelField(state.demandLevel.ToString(), GUILayout.Width(55));

            int broker = ExchangeManager.I.GetBrokerPayout(state.speciesId);
            EditorGUILayout.LabelField(broker.ToString(), GUILayout.Width(50));

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        // Summary
        EditorGUILayout.Space(4);
        var save = ExchangeManager.I.SaveData;
        if (save != null)
        {
            EditorGUILayout.LabelField($"Species tracked: {allStates.Count}  |  Day seed: {save.dailySeed}  |  Brokered: {save.totalBrokered}  |  Credits brokered: {save.totalCreditsBrokered}", EditorStyles.miniLabel);
        }
    }

    private void SimulateDays(int days)
    {
        // Temporarily adjust lastDayIndex backwards to trigger day changes
        var save = ExchangeManager.I.SaveData;
        if (save == null) return;

        int originalDay = save.lastDayIndex;
        for (int d = 0; d < days; d++)
        {
            save.lastDayIndex = originalDay + d;
            save.dailySeed = StableHash("ExchangeDay" + save.lastDayIndex);
            ExchangeManager.I.RecalculateAll();
        }

        // Set to current real day
        int realDay = (int)(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);
        save.lastDayIndex = realDay;
        save.dailySeed = StableHash("ExchangeDay" + realDay);
        ExchangeManager.I.RecalculateAll();

        Debug.Log($"[ExchangeDebug] Simulated {days} day(s) of market activity.");
    }

    private void LogAllValues()
    {
        var allStates = ExchangeManager.I.AllStates;
        if (allStates == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ExchangeDebug] === Market Snapshot ===");
        sb.AppendLine($"{"Species",-20} {"Base",6} {"Curr",6} {"Prev",6} {"Delta",6} {"Demand",-8} {"Trend",-6}");
        sb.AppendLine(new string('-', 62));

        foreach (var kv in allStates)
        {
            var s = kv.Value;
            var def = MonsterCatalog.GetById(s.speciesId);
            string name = def != null ? def.displayName : s.speciesId;
            int baseVal = def != null ? def.baseMarketValue : 0;
            int delta = s.currentValue - s.previousValue;
            string deltaStr = delta > 0 ? $"+{delta}" : delta.ToString();
            sb.AppendLine($"{name,-20} {baseVal,6} {s.currentValue,6} {s.previousValue,6} {deltaStr,6} {s.demandLevel,-8} {s.trend,-6}");
        }

        Debug.Log(sb.ToString());
    }

    private void LogSaveData()
    {
        var save = ExchangeManager.I.SaveData;
        if (save == null) { Debug.Log("[ExchangeDebug] No save data."); return; }

        Debug.Log($"[ExchangeDebug] Save Data:\n" +
                  $"  Species states: {save.speciesStates?.Count ?? 0}\n" +
                  $"  Active requests: {save.activeRequests?.Count ?? 0}\n" +
                  $"  Daily seed: {save.dailySeed}\n" +
                  $"  Last day index: {save.lastDayIndex}\n" +
                  $"  Last recalc unix: {save.lastRecalcUnix}\n" +
                  $"  Total brokered: {save.totalBrokered}\n" +
                  $"  Credits brokered: {save.totalCreditsBrokered}\n" +
                  $"  Requests fulfilled: {save.totalRequestsFulfilled}\n" +
                  $"  Sentiment month key: {save.battleSentimentMonthKey}");
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = (int)2166136261;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 16777619;
            }
            return hash & 0x7FFFFFFF;
        }
    }
}
#endif
