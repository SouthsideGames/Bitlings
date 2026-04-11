using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ArenaTournamentDetailPanelUI : MonoBehaviour
{
    // ═════════════════════════════════════════════════════════════
    //  Inspector references
    // ═════════════════════════════════════════════════════════════

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI weekRangeLabel;
    [SerializeField] private Button closeButton;

    [Header("Player Status")]
    [SerializeField] private TextMeshProUGUI statusLabel;
    [SerializeField] private TextMeshProUGUI placementLabel;
    [SerializeField] private TextMeshProUGUI rewardSummaryLabel;

    [Header("Standings")]
    [SerializeField] private GameObject standingsGroup;
    [SerializeField] private Transform standingsListRoot;
    [SerializeField] private TextMeshProUGUI standingsEntryPrefabLabel;

    [Header("Match History")]
    [SerializeField] private Transform matchListRoot;
    [SerializeField] private ArenaMatchCardUI matchCardPrefab;
    [SerializeField] private ScrollRect matchScrollRect;
    [SerializeField] private GameObject matchEmptyLabel;

    // ═════════════════════════════════════════════════════════════
    //  State
    // ═════════════════════════════════════════════════════════════

    private enum ViewMode { Current, History }
    private ViewMode _mode;
    private ArenaTournamentHistoryEntry _historyEntry;
    private readonly List<ArenaMatchCardUI> _matchCards = new List<ArenaMatchCardUI>();
    private readonly List<GameObject> _standingsRows = new List<GameObject>();

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void OnEnable()
    {
        if (closeButton) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(HandleClose); }
        GameEvents.ArenaDataChanged += RefreshIfCurrent;
    }

    void OnDisable()
    {
        if (closeButton) closeButton.onClick.RemoveListener(HandleClose);
        GameEvents.ArenaDataChanged -= RefreshIfCurrent;
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Shows the player's current-week tournament details.
    /// </summary>
    public void ShowCurrent()
    {
        _mode = ViewMode.Current;
        _historyEntry = null;
        Populate();
    }

    /// <summary>
    /// Shows details for a completed tournament from history.
    /// </summary>
    public void ShowHistory(ArenaTournamentHistoryEntry entry)
    {
        _mode = ViewMode.History;
        _historyEntry = entry;
        Populate();
    }

    // ═════════════════════════════════════════════════════════════
    //  Population
    // ═════════════════════════════════════════════════════════════

    private void Populate()
    {
        if (_mode == ViewMode.Current)
            PopulateCurrent();
        else
            PopulateHistory();
    }

    private void PopulateCurrent()
    {
        var arena = SaveManager.GetArenaSaveData();
        var cache = arena?.currentTournamentCache;
        var status = cache?.playerStatus ?? ArenaPlayerTournamentStatus.NotEntered;

        if (titleLabel) titleLabel.text = "Current Tournament";

        SetWeekRange(cache?.weekStartUtc ?? 0, cache?.weekEndUtc ?? 0);

        // Status
        if (statusLabel)
        {
            switch (status)
            {
                case ArenaPlayerTournamentStatus.NotEntered:  statusLabel.text = "Not Entered"; break;
                case ArenaPlayerTournamentStatus.Entered:     statusLabel.text = "Registered"; break;
                case ArenaPlayerTournamentStatus.Active:      statusLabel.text = "In Progress"; break;
                case ArenaPlayerTournamentStatus.Eliminated:  statusLabel.text = "Eliminated"; break;
                case ArenaPlayerTournamentStatus.Completed:   statusLabel.text = "Completed"; break;
                default:                                      statusLabel.text = ""; break;
            }
        }

        // Placement
        int placement = cache?.finalPlacement ?? 0;
        if (placementLabel)
            placementLabel.text = placement > 0 ? $"Placed {GetOrdinal(placement)}" : "—";

        // Reward summary (from matching history entry if completed)
        if (rewardSummaryLabel)
        {
            string tid = cache?.tournamentId ?? "";
            rewardSummaryLabel.text = BuildRewardSummaryFromHistory(arena, tid);
        }

        // Standings — only shown when completed
        bool showStandings = status == ArenaPlayerTournamentStatus.Completed;
        if (standingsGroup) standingsGroup.SetActive(showStandings);
        ClearStandings();

        // Try to load full record from the tournament service
        var record = ArenaTournamentService.GetActiveRecord();
        string playerEntryId = cache?.playerEntryId;
        if (record != null && !string.IsNullOrEmpty(playerEntryId))
        {
            PopulateMatchesFromRecord(record, playerEntryId);
            if (showStandings) PopulateStandings(record, playerEntryId);
        }
        else
        {
            BuildMatchListFromSave(arena, cache?.tournamentId);
        }
    }

    private void PopulateHistory()
    {
        if (_historyEntry == null) return;

        if (titleLabel) titleLabel.text = "Past Tournament";

        SetWeekRange(_historyEntry.weekStartUtc, 0);

        if (statusLabel) statusLabel.text = "Completed";

        if (placementLabel)
            placementLabel.text = _historyEntry.finalPlacement > 0
                ? $"Placed {GetOrdinal(_historyEntry.finalPlacement)}"
                : "—";

        if (rewardSummaryLabel)
            rewardSummaryLabel.text = FormatRewardResult(_historyEntry.rewardResult);

        // Standings not available from history summary.
        if (standingsGroup) standingsGroup.SetActive(false);
        ClearStandings();

        // No per-match data in history summary — show empty.
        ClearMatchCards();
        if (matchEmptyLabel) matchEmptyLabel.SetActive(true);
    }

    // ═════════════════════════════════════════════════════════════
    //  Match list
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Populates the match list from the current tournament record on disk.
    /// This provides a public entry point for external code that loads the
    /// full <see cref="ArenaTournamentRecord"/> (e.g. from a cache file).
    /// </summary>
    public void PopulateMatchesFromRecord(ArenaTournamentRecord record, string playerEntryId)
    {
        ClearMatchCards();
        if (record == null || record.matches == null || string.IsNullOrEmpty(playerEntryId))
        {
            if (matchEmptyLabel) matchEmptyLabel.SetActive(true);
            return;
        }

        if (matchEmptyLabel) matchEmptyLabel.SetActive(false);

        for (int i = 0; i < record.matches.Count; i++)
        {
            var match = record.matches[i];
            if (match == null) continue;
            if (string.IsNullOrEmpty(match.winnerEntryId)) continue; // unresolved

            // Only show matches involving the player.
            bool isLeft  = string.Equals(match.leftEntryId,  playerEntryId, StringComparison.Ordinal);
            bool isRight = string.Equals(match.rightEntryId, playerEntryId, StringComparison.Ordinal);
            if (!isLeft && !isRight) continue;

            var histEntry = BuildMatchHistoryEntry(match, record, playerEntryId, isLeft);
            if (matchCardPrefab == null || matchListRoot == null) continue;

            var card = Instantiate(matchCardPrefab, matchListRoot);
            card.Setup(histEntry, OnMatchCardClicked);
            _matchCards.Add(card);
        }

        if (_matchCards.Count == 0 && matchEmptyLabel)
            matchEmptyLabel.SetActive(true);
    }

    /// <summary>
    /// Builds match list if we don't have the full record — placeholder for save-only data.
    /// </summary>
    private void BuildMatchListFromSave(ArenaSaveData arena, string tournamentId)
    {
        ClearMatchCards();

        // The save data doesn't store per-match details for the current tournament.
        // This placeholder shows the empty state — real population happens via
        // PopulateMatchesFromRecord when the full record is loaded.
        if (matchEmptyLabel) matchEmptyLabel.SetActive(true);
    }

    private void ClearMatchCards()
    {
        for (int i = 0; i < _matchCards.Count; i++)
        {
            if (_matchCards[i] != null)
                Destroy(_matchCards[i].gameObject);
        }
        _matchCards.Clear();
    }

    // ═════════════════════════════════════════════════════════════
    //  Standings
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Populates standings from a completed tournament record.
    /// Call after loading the full record for display.
    /// </summary>
    public void PopulateStandings(ArenaTournamentRecord record, string playerEntryId)
    {
        ClearStandings();
        if (record == null || record.standings == null || record.standings.placementOrder == null)
            return;

        if (standingsGroup) standingsGroup.SetActive(true);

        for (int i = 0; i < record.standings.placementOrder.Count; i++)
        {
            string entryId = record.standings.placementOrder[i];
            var entry = FindEntry(record, entryId);
            if (entry == null) continue;

            string displayName = entry.teamSnapshot != null
                ? entry.teamSnapshot.ownerDisplayName
                : (entry.isBot ? "Bot" : "Player");

            bool isPlayer = string.Equals(entryId, playerEntryId, StringComparison.Ordinal);

            CreateStandingsRow(i + 1, displayName, entry.arenaScore, isPlayer);
        }
    }

    private void CreateStandingsRow(int placement, string name, int score, bool highlight)
    {
        if (standingsEntryPrefabLabel == null || standingsListRoot == null) return;

        var label = Instantiate(standingsEntryPrefabLabel, standingsListRoot);
        label.text = $"{GetOrdinal(placement)}  {name}  ({score})";

        if (highlight)
            label.fontStyle = FontStyles.Bold;

        _standingsRows.Add(label.gameObject);
    }

    private void ClearStandings()
    {
        for (int i = 0; i < _standingsRows.Count; i++)
        {
            if (_standingsRows[i] != null)
                Destroy(_standingsRows[i]);
        }
        _standingsRows.Clear();
    }

    // ═════════════════════════════════════════════════════════════
    //  Match detail navigation
    // ═════════════════════════════════════════════════════════════

    private void OnMatchCardClicked(ArenaMatchHistoryEntry entry)
    {
        if (entry == null) return;

        if (UIManager.I) UIManager.I.Show(PanelId.ArenaMatchDetail);

        var root = UIManager.I != null ? UIManager.I.GetRoot(PanelId.ArenaMatchDetail) : null;
        if (root != null)
        {
            var detail = root.GetComponent<ArenaMatchDetailPanelUI>();
            if (detail != null) detail.Show(entry, entry.playerSnapshot, entry.opponentSnapshot);
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    private void HandleClose()
    {
        if (UIManager.I) UIManager.I.Hide(PanelId.ArenaTournamentDetail);
    }

    private void RefreshIfCurrent()
    {
        if (_mode == ViewMode.Current) PopulateCurrent();
    }

    private void SetWeekRange(long startUtc, long endUtc)
    {
        if (!weekRangeLabel) return;

        if (startUtc <= 0)
        {
            weekRangeLabel.text = "";
            return;
        }

        try
        {
            var tz = ArenaConstants.EasternTimeZone;
            var start = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(startUtc), tz);

            if (endUtc > 0)
            {
                var end = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(endUtc), tz);
                weekRangeLabel.text = $"{start:MMM d} – {end:MMM d} ET";
            }
            else
            {
                weekRangeLabel.text = $"Week of {start:MMM d} ET";
            }
        }
        catch
        {
            weekRangeLabel.text = "";
        }
    }

    private static ArenaMatchHistoryEntry BuildMatchHistoryEntry(
        ArenaTournamentMatch match,
        ArenaTournamentRecord record,
        string playerEntryId,
        bool playerIsLeft)
    {
        string opponentEntryId = playerIsLeft ? match.rightEntryId : match.leftEntryId;
        var opponent = FindEntry(record, opponentEntryId);
        var player = FindEntry(record, playerEntryId);

        return new ArenaMatchHistoryEntry
        {
            matchId = match.matchId,
            tournamentId = match.tournamentId,
            roundIndex = match.roundIndex,
            opponentDisplayName = opponent?.teamSnapshot?.ownerDisplayName ?? "Unknown",
            opponentIsBot = opponent?.isBot ?? false,
            opponentArenaScore = opponent?.arenaScore ?? 0,
            playerWon = string.Equals(match.winnerEntryId, playerEntryId, StringComparison.Ordinal),
            turnCount = match.turnCount,
            processedUtc = match.processedUtc,
            playerSnapshot = player?.teamSnapshot,
            opponentSnapshot = opponent?.teamSnapshot,
            battleLog = match.battleLog ?? new List<ArenaBattleLogEvent>()
        };
    }

    private static string BuildRewardSummaryFromHistory(ArenaSaveData arena, string tournamentId)
    {
        if (arena?.recentTournamentHistory == null || string.IsNullOrEmpty(tournamentId))
            return "";

        for (int i = 0; i < arena.recentTournamentHistory.Count; i++)
        {
            var hist = arena.recentTournamentHistory[i];
            if (hist != null && string.Equals(hist.tournamentId, tournamentId, StringComparison.Ordinal))
                return FormatRewardResult(hist.rewardResult);
        }
        return "";
    }

    private static string FormatRewardResult(ArenaRewardResult rw)
    {
        if (rw == null) return "";
        if (!rw.wasGranted) return "Rewards pending";

        var parts = new List<string>();
        if (rw.creditsAwarded > 0) parts.Add($"{rw.creditsAwarded} Credits");
        if (rw.packVoucherAmount > 0) parts.Add($"{rw.packVoucherAmount} Pack Voucher");
        if (rw.featuredResourceAmount > 0) parts.Add($"{rw.featuredResourceAmount} {rw.featuredResourceType}");
        if (rw.arenaTicketAmount > 0) parts.Add($"{rw.arenaTicketAmount} Ticket");
        if (rw.randomResourceRewards != null)
        {
            for (int j = 0; j < rw.randomResourceRewards.Count; j++)
            {
                var b = rw.randomResourceRewards[j];
                if (b != null && b.amount > 0)
                    parts.Add($"{b.amount} {b.resourceType}");
            }
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "No rewards";
    }

    private static ArenaTournamentEntry FindEntry(ArenaTournamentRecord record, string entryId)
    {
        if (record?.entries == null || string.IsNullOrEmpty(entryId)) return null;
        for (int i = 0; i < record.entries.Count; i++)
        {
            if (string.Equals(record.entries[i].entryId, entryId, StringComparison.Ordinal))
                return record.entries[i];
        }
        return null;
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
