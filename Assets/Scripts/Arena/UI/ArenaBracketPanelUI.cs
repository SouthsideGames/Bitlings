// Assets/Scripts/Arena/UI/ArenaBracketPanelUI.cs

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the full tournament bracket round-by-round.
/// Opened from the week-card "View" button after brackets are assigned.
/// Subscribes to <see cref="GameEvents.ArenaDataChanged"/> so it updates live
/// as rounds are resolved throughout the week.
/// </summary>
public class ArenaBracketPanelUI : MonoBehaviour
{
    // ── Header ──────────────────────────────────────────────────────
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI weekRangeLabel;
    [SerializeField] private Button closeButton;

    // ── Round selector ───────────────────────────────────────────────
    [Header("Round Selector")]
    [SerializeField] private Button[] roundTabButtons;      // 5 elements: rounds 0-4
    [SerializeField] private TextMeshProUGUI[] roundTabLabels;

    // ── Match list ───────────────────────────────────────────────────
    [Header("Match List")]
    [SerializeField] private Transform matchListRoot;
    [SerializeField] private ArenaBracketMatchRowUI matchRowPrefab;
    [SerializeField] private ScrollRect matchScrollRect;
    [SerializeField] private TextMeshProUGUI emptyLabel;

    // ── State ────────────────────────────────────────────────────────

    private static readonly string[] RoundTabNames =
        { "Round 1", "Round 2", "Round 3", "Round 4", "Final" };

    private int _selectedRound = -1;
    private bool _hasPopulated;

    private readonly List<ArenaBracketMatchRowUI> _rows = new List<ArenaBracketMatchRowUI>();

    // ═════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════════

    void OnEnable()
    {
        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(HandleClose);
        }

        for (int i = 0; i < roundTabButtons.Length; i++)
        {
            int roundIndex = i; // capture for lambda
            if (roundTabButtons[i])
            {
                roundTabButtons[i].onClick.RemoveAllListeners();
                roundTabButtons[i].onClick.AddListener(() => SelectRound(roundIndex));
            }
        }

        GameEvents.ArenaDataChanged += OnArenaDataChanged;
    }

    void OnDisable()
    {
        if (closeButton) closeButton.onClick.RemoveListener(HandleClose);

        for (int i = 0; i < roundTabButtons.Length; i++)
        {
            if (roundTabButtons[i]) roundTabButtons[i].onClick.RemoveAllListeners();
        }

        GameEvents.ArenaDataChanged -= OnArenaDataChanged;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads the active tournament record and populates the bracket view.
    /// Call this every time the panel is shown.
    /// </summary>
    public void ShowCurrent()
    {
        _hasPopulated = false;
        _selectedRound = -1;

        var record = ArenaTournamentService.GetActiveRecord();
        var cache = SaveManager.GetArenaSaveData()?.currentTournamentCache;
        string playerEntryId = cache?.playerEntryId ?? "";

        Populate(record, playerEntryId);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Population
    // ═════════════════════════════════════════════════════════════════

    private void Populate(ArenaTournamentRecord record, string playerEntryId)
    {
        if (titleLabel) titleLabel.text = "Tournament Bracket";

        SetWeekRange(record);

        // Build per-round match lists.
        var matchesByRound = new List<ArenaTournamentMatch>[ArenaConstants.TotalRounds];
        for (int r = 0; r < ArenaConstants.TotalRounds; r++)
            matchesByRound[r] = new List<ArenaTournamentMatch>();

        if (record?.matches != null)
        {
            for (int i = 0; i < record.matches.Count; i++)
            {
                var m = record.matches[i];
                if (m == null) continue;
                int ri = m.roundIndex;
                if (ri >= 0 && ri < ArenaConstants.TotalRounds)
                    matchesByRound[ri].Add(m);
            }
        }

        // Configure round tabs.
        for (int r = 0; r < roundTabButtons.Length && r < ArenaConstants.TotalRounds; r++)
        {
            bool hasMatches = matchesByRound[r].Count > 0;
            if (roundTabButtons[r]) roundTabButtons[r].interactable = hasMatches;
            if (r < roundTabLabels.Length && roundTabLabels[r])
                roundTabLabels[r].text = r < RoundTabNames.Length ? RoundTabNames[r] : $"Round {r + 1}";
        }

        // On first open: select the highest round that has any resolved match.
        // On refresh: keep the current tab if it still has matches, otherwise fall back.
        int targetRound = _selectedRound;
        if (!_hasPopulated || targetRound < 0 || matchesByRound[targetRound].Count == 0)
        {
            targetRound = FindDefaultRound(matchesByRound);
        }

        _hasPopulated = true;

        // Store the record and player id so ShowRound can use them.
        _cachedRecord       = record;
        _cachedPlayerEntryId = playerEntryId;
        _matchesByRound     = matchesByRound;

        SelectRound(targetRound);
    }

    // ── cached state for round switching ────────────────────────────

    private ArenaTournamentRecord _cachedRecord;
    private string _cachedPlayerEntryId;
    private List<ArenaTournamentMatch>[] _matchesByRound;

    // ═════════════════════════════════════════════════════════════════
    //  Round display
    // ═════════════════════════════════════════════════════════════════

    private void SelectRound(int roundIndex)
    {
        if (roundIndex < 0 || roundIndex >= ArenaConstants.TotalRounds) return;
        _selectedRound = roundIndex;

        HighlightSelectedTab(roundIndex);
        ShowRound(roundIndex);
    }

    private void ShowRound(int roundIndex)
    {
        ClearRows();

        var matches = _matchesByRound != null && roundIndex < _matchesByRound.Length
            ? _matchesByRound[roundIndex]
            : null;

        if (matches == null || matches.Count == 0)
        {
            if (emptyLabel) emptyLabel.gameObject.SetActive(true);
            return;
        }

        if (emptyLabel) emptyLabel.gameObject.SetActive(false);

        for (int i = 0; i < matches.Count; i++)
        {
            if (matchRowPrefab == null || matchListRoot == null) break;
            var row = Instantiate(matchRowPrefab, matchListRoot);
            row.Setup(matches[i], _cachedRecord, _cachedPlayerEntryId, OnMatchRowClicked);
            _rows.Add(row);
        }

        // Scroll back to the top.
        if (matchScrollRect)
            matchScrollRect.verticalNormalizedPosition = 1f;
    }

    private void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null) Destroy(_rows[i].gameObject);
        }
        _rows.Clear();
    }

    // ═════════════════════════════════════════════════════════════════
    //  Match click → detail panel
    // ═════════════════════════════════════════════════════════════════

    private void OnMatchRowClicked(ArenaMatchHistoryEntry entry)
    {
        if (entry == null) return;

        if (UIManager.I) UIManager.I.Show(PanelId.ArenaMatchDetail);

        var root = UIManager.I != null ? UIManager.I.GetRoot(PanelId.ArenaMatchDetail) : null;
        if (root != null)
        {
            var detail = root.GetComponent<ArenaMatchDetailPanelUI>();
            if (detail != null)
                detail.Show(entry, entry.playerSnapshot, entry.opponentSnapshot, PanelId.ArenaBracket);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Live refresh
    // ═════════════════════════════════════════════════════════════════

    private void OnArenaDataChanged()
    {
        var record = ArenaTournamentService.GetActiveRecord();
        var cache = SaveManager.GetArenaSaveData()?.currentTournamentCache;
        string playerEntryId = cache?.playerEntryId ?? "";

        Populate(record, playerEntryId);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private void HandleClose()
    {
        if (!UIManager.I) return;
        UIManager.I.Hide(PanelId.ArenaBracket);
        UIManager.I.Show(PanelId.ArenaMain);
    }

    private void HighlightSelectedTab(int selected)
    {
        for (int i = 0; i < roundTabButtons.Length; i++)
        {
            if (roundTabButtons[i] == null) continue;

            var colors = roundTabButtons[i].colors;
            bool isSelected = i == selected;
            colors.normalColor      = isSelected ? Color.white    : new Color(0.7f, 0.7f, 0.7f);
            colors.highlightedColor = isSelected ? Color.white    : new Color(0.85f, 0.85f, 0.85f);
            roundTabButtons[i].colors = colors;
        }
    }

    private static int FindDefaultRound(List<ArenaTournamentMatch>[] matchesByRound)
    {
        // Prefer the highest round that has at least one resolved match.
        for (int r = ArenaConstants.TotalRounds - 1; r >= 0; r--)
        {
            if (matchesByRound[r].Count == 0) continue;
            for (int m = 0; m < matchesByRound[r].Count; m++)
            {
                if (!string.IsNullOrEmpty(matchesByRound[r][m]?.winnerEntryId))
                    return r;
            }
        }

        // Fallback: first round that has any matches at all.
        for (int r = 0; r < ArenaConstants.TotalRounds; r++)
        {
            if (matchesByRound[r].Count > 0) return r;
        }

        return 0;
    }

    private void SetWeekRange(ArenaTournamentRecord record)
    {
        if (!weekRangeLabel) return;

        if (record == null || record.weekStartUtc <= 0)
        {
            weekRangeLabel.text = "";
            return;
        }

        try
        {
            var tz    = ArenaConstants.EasternTimeZone;
            var start = System.TimeZoneInfo.ConvertTime(
                System.DateTimeOffset.FromUnixTimeSeconds(record.weekStartUtc), tz);
            var end   = System.TimeZoneInfo.ConvertTime(
                System.DateTimeOffset.FromUnixTimeSeconds(record.weekEndUtc), tz);
            weekRangeLabel.text = $"{start:MMM d} – {end:MMM d} ET";
        }
        catch
        {
            weekRangeLabel.text = "";
        }
    }
}
