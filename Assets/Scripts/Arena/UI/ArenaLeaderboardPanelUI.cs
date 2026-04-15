// Assets/Scripts/Arena/UI/ArenaLeaderboardPanelUI.cs
// BRN Arena v1 — Leaderboard sub-panel shown from ArenaMainPanelUI.
// Displays two tabs: Weekly top placements and All-Time championships.

using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Leaderboards.Models;
using UnityEngine;
using UnityEngine.UI;

public class ArenaLeaderboardPanelUI : MonoBehaviour
{
    public static ArenaLeaderboardPanelUI I { get; private set; }

    // ═════════════════════════════════════════════════════════════
    //  Inspector references
    // ═════════════════════════════════════════════════════════════

    [Header("Tabs")]
    [SerializeField] private Button weeklyTabButton;
    [SerializeField] private Button allTimeTabButton;
    [SerializeField] private Image weeklyTabHighlight;
    [SerializeField] private Image allTimeTabHighlight;

    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private ArenaLeaderboardRowUI rowPrefab;
    [SerializeField] private ScrollRect scrollRect;

    [Header("Player Entry")]
    [SerializeField] private GameObject playerEntrySection;
    [SerializeField] private TextMeshProUGUI playerRankLabel;
    [SerializeField] private TextMeshProUGUI playerScoreLabel;

    [Header("State")]
    [SerializeField] private GameObject loadingIndicator;
    [SerializeField] private GameObject emptyLabel;

    [Header("Navigation")]
    [SerializeField] private Button closeButton;

    // ═════════════════════════════════════════════════════════════
    //  State
    // ═════════════════════════════════════════════════════════════

    private enum Tab { Weekly, AllTime }
    private Tab _activeTab = Tab.Weekly;
    private readonly List<ArenaLeaderboardRowUI> _rows = new List<ArenaLeaderboardRowUI>();
    private bool _isLoading;

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (weeklyTabButton) weeklyTabButton.onClick.AddListener(() => SwitchTab(Tab.Weekly));
        if (allTimeTabButton) allTimeTabButton.onClick.AddListener(() => SwitchTab(Tab.AllTime));
        if (closeButton) closeButton.onClick.AddListener(HandleClose);
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        if (weeklyTabButton) weeklyTabButton.onClick.RemoveAllListeners();
        if (allTimeTabButton) allTimeTabButton.onClick.RemoveAllListeners();
        if (closeButton) closeButton.onClick.RemoveAllListeners();
    }

    void OnEnable()
    {
        SwitchTab(Tab.Weekly);
    }

    // ═════════════════════════════════════════════════════════════
    //  Tab switching
    // ═════════════════════════════════════════════════════════════

    private void SwitchTab(Tab tab)
    {
        _activeTab = tab;

        if (weeklyTabHighlight) weeklyTabHighlight.enabled = tab == Tab.Weekly;
        if (allTimeTabHighlight) allTimeTabHighlight.enabled = tab == Tab.AllTime;

        RefreshAsync();
    }

    // ═════════════════════════════════════════════════════════════
    //  Data loading
    // ═════════════════════════════════════════════════════════════

    private async void RefreshAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        ClearRows();
        SetLoading(true);

        string localPlayerId = AuthenticationService.Instance.IsSignedIn
            ? AuthenticationService.Instance.PlayerId
            : null;

        List<LeaderboardEntry> entries;
        LeaderboardEntry playerEntry;

        if (_activeTab == Tab.Weekly)
        {
            entries = await ArenaLeaderboardService.GetWeeklyTopAsync(50);
            playerEntry = await ArenaLeaderboardService.GetPlayerWeeklyEntryAsync();
        }
        else
        {
            entries = await ArenaLeaderboardService.GetAllTimeTopAsync(50);
            playerEntry = await ArenaLeaderboardService.GetPlayerAllTimeEntryAsync();
        }

        // Guard — panel may have been closed/destroyed while awaiting
        if (this == null) return;

        SetLoading(false);

        if (entries == null || entries.Count == 0)
        {
            if (emptyLabel) emptyLabel.SetActive(true);
            ShowPlayerEntry(null);
            _isLoading = false;
            return;
        }

        if (emptyLabel) emptyLabel.SetActive(false);

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (rowPrefab == null || listRoot == null) break;

            var row = Instantiate(rowPrefab, listRoot);
            bool isLocal = !string.IsNullOrEmpty(localPlayerId) && e.PlayerId == localPlayerId;
            string scoreText = FormatScore(e.Score, _activeTab);
            string tierId = e.Tier;
            row.Setup(e.Rank + 1, e.PlayerName, scoreText, isLocal, tierId);
            _rows.Add(row);
        }

        ShowPlayerEntry(playerEntry);
        _isLoading = false;
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    private void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null) Destroy(_rows[i].gameObject);
        }
        _rows.Clear();
    }

    private void SetLoading(bool loading)
    {
        if (loadingIndicator) loadingIndicator.SetActive(loading);
        if (emptyLabel) emptyLabel.SetActive(false);
    }

    private void ShowPlayerEntry(LeaderboardEntry entry)
    {
        if (playerEntrySection == null) return;

        if (entry == null)
        {
            playerEntrySection.SetActive(false);
            return;
        }

        playerEntrySection.SetActive(true);
        if (playerRankLabel) playerRankLabel.text = $"Your Rank: #{entry.Rank + 1}";
        if (playerScoreLabel) playerScoreLabel.text = FormatScore(entry.Score, _activeTab);
    }

    private string FormatScore(double score, Tab tab)
    {
        if (tab == Tab.Weekly)
        {
            // Invert back: score 32 → placement 1
            int placement = (ArenaConstants.BracketSize + 1) - (int)score;
            return GetOrdinal(Mathf.Max(1, placement));
        }
        return ((int)score).ToString();
    }

    private void HandleClose()
    {
        gameObject.SetActive(false);
    }

    private static string GetOrdinal(int n)
    {
        if (n <= 0) return n.ToString();
        int rem100 = n % 100;
        if (rem100 >= 11 && rem100 <= 13) return $"{n}th";
        switch (n % 10)
        {
            case 1: return $"{n}st";
            case 2: return $"{n}nd";
            case 3: return $"{n}rd";
            default: return $"{n}th";
        }
    }
}
