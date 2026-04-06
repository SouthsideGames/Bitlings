// Assets/Scripts/Arena/UI/ArenaWeekCardUI.cs
// BRN Arena v1 — State-driven current-week card used inside ArenaMainPanelUI.
// Shows registration status, tournament progress, or completion result.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the player's current-week tournament state: registration, active, eliminated, or completed.
/// Designed to be a child GameObject inside the arena main panel with all fields wired in the inspector.
/// </summary>
public class ArenaWeekCardUI : MonoBehaviour
{
    // ── Header ──
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI weekRangeLabel;
    [SerializeField] private TextMeshProUGUI statusLabel;

    // ── Registration state ──
    [Header("Registration")]
    [SerializeField] private GameObject registrationGroup;
    [SerializeField] private TextMeshProUGUI registrationDeadlineLabel;

    // ── Active / In-Progress state ──
    [Header("Active")]
    [SerializeField] private GameObject activeGroup;
    [SerializeField] private TextMeshProUGUI currentRoundLabel;
    [SerializeField] private TextMeshProUGUI lastMatchResultLabel;

    // ── Completed state ──
    [Header("Completed")]
    [SerializeField] private GameObject completedGroup;
    [SerializeField] private TextMeshProUGUI placementLabel;
    [SerializeField] private TextMeshProUGUI rewardSummaryLabel;

    // ── Action buttons ──
    [Header("Buttons")]
    [SerializeField] private Button enterButton;
    [SerializeField] private Button viewButton;

    private Action _onEnterClicked;
    private Action _onViewClicked;

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void OnEnable()
    {
        if (enterButton) { enterButton.onClick.RemoveAllListeners(); enterButton.onClick.AddListener(HandleEnter); }
        if (viewButton)  { viewButton.onClick.RemoveAllListeners();  viewButton.onClick.AddListener(HandleView); }
    }

    void OnDisable()
    {
        if (enterButton) enterButton.onClick.RemoveListener(HandleEnter);
        if (viewButton)  viewButton.onClick.RemoveListener(HandleView);
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    public void Bind(Action onEnter, Action onView)
    {
        _onEnterClicked = onEnter;
        _onViewClicked = onView;
    }

    /// <summary>
    /// Refreshes the card based on current save data.
    /// </summary>
    public void Refresh()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) { SetAllGroupsHidden(); return; }

        var cache = arena.currentTournamentCache;
        var status = cache?.playerStatus ?? ArenaPlayerTournamentStatus.NotEntered;

        SetWeekRange(cache);

        switch (status)
        {
            case ArenaPlayerTournamentStatus.NotEntered:
                ShowRegistrationOpen(cache);
                break;

            case ArenaPlayerTournamentStatus.Entered:
                ShowRegistered();
                break;

            case ArenaPlayerTournamentStatus.Active:
                ShowActive(cache);
                break;

            case ArenaPlayerTournamentStatus.Eliminated:
                ShowEliminated(cache);
                break;

            case ArenaPlayerTournamentStatus.Completed:
                ShowCompleted(arena, cache);
                break;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  State rendering
    // ═════════════════════════════════════════════════════════════

    private void ShowRegistrationOpen(ArenaCurrentTournamentCache cache)
    {
        SetGroups(registration: true, active: false, completed: false);

        if (statusLabel) statusLabel.text = "Registration Open";
        if (registrationDeadlineLabel)
            registrationDeadlineLabel.text = $"Closes Monday {ArenaConstants.RegistrationCloseHourET}:{ArenaConstants.RegistrationCloseMinuteET:D2} ET";

        SetButtonState(enter: true, view: false);
        UpdateEnterButtonInteractable();
    }

    private void ShowRegistered()
    {
        SetGroups(registration: true, active: false, completed: false);

        if (statusLabel) statusLabel.text = "Registered";
        if (registrationDeadlineLabel) registrationDeadlineLabel.text = "Waiting for tournament to begin...";

        SetButtonState(enter: false, view: false);
    }

    private void ShowActive(ArenaCurrentTournamentCache cache)
    {
        SetGroups(registration: false, active: true, completed: false);

        if (statusLabel) statusLabel.text = "Tournament Active";

        int round = cache != null ? cache.currentRoundIndex + 1 : 1;
        if (currentRoundLabel) currentRoundLabel.text = $"Round {round} of {ArenaConstants.TotalRounds}";
        if (lastMatchResultLabel) lastMatchResultLabel.text = "";

        SetButtonState(enter: false, view: true);
    }

    private void ShowEliminated(ArenaCurrentTournamentCache cache)
    {
        SetGroups(registration: false, active: true, completed: false);

        if (statusLabel) statusLabel.text = "Eliminated";

        int round = cache != null ? cache.currentRoundIndex + 1 : 1;
        if (currentRoundLabel) currentRoundLabel.text = $"Eliminated in round {round}";
        if (lastMatchResultLabel) lastMatchResultLabel.text = "";

        SetButtonState(enter: false, view: true);
    }

    private void ShowCompleted(ArenaSaveData arena, ArenaCurrentTournamentCache cache)
    {
        SetGroups(registration: false, active: false, completed: true);

        if (statusLabel) statusLabel.text = "Tournament Complete";

        int placement = cache != null ? cache.finalPlacement : 0;
        if (placementLabel)
            placementLabel.text = placement > 0 ? $"Placed {GetOrdinal(placement)}" : "—";

        if (rewardSummaryLabel)
            rewardSummaryLabel.text = BuildRewardSummary(arena, cache);

        SetButtonState(enter: false, view: true);
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    private void SetWeekRange(ArenaCurrentTournamentCache cache)
    {
        if (!weekRangeLabel) return;

        if (cache == null || cache.weekStartUtc <= 0)
        {
            weekRangeLabel.text = "This Week";
            return;
        }

        try
        {
            var tz = ArenaConstants.EasternTimeZone;
            var start = TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeSeconds(cache.weekStartUtc), tz);
            var end = TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeSeconds(cache.weekEndUtc), tz);
            weekRangeLabel.text = $"{start:MMM d} – {end:MMM d} ET";
        }
        catch
        {
            weekRangeLabel.text = "This Week";
        }
    }

    private void UpdateEnterButtonInteractable()
    {
        if (!enterButton) return;

        bool canEnter = ArenaSaveHelper.IsArenaUnlocked()
                     && ArenaTeamValidator.IsBattleTeamComplete()
                     && ArenaTicketManager.GetTicketCount() > 0;

        enterButton.interactable = canEnter;
    }

    private void SetGroups(bool registration, bool active, bool completed)
    {
        if (registrationGroup) registrationGroup.SetActive(registration);
        if (activeGroup) activeGroup.SetActive(active);
        if (completedGroup) completedGroup.SetActive(completed);
    }

    private void SetAllGroupsHidden()
    {
        SetGroups(false, false, false);
        SetButtonState(false, false);
        if (statusLabel) statusLabel.text = "";
        if (weekRangeLabel) weekRangeLabel.text = "";
    }

    private void SetButtonState(bool enter, bool view)
    {
        if (enterButton) enterButton.gameObject.SetActive(enter);
        if (viewButton)  viewButton.gameObject.SetActive(view);
    }

    private string BuildRewardSummary(ArenaSaveData arena, ArenaCurrentTournamentCache cache)
    {
        if (arena?.recentTournamentHistory == null || arena.recentTournamentHistory.Count == 0)
            return "";

        // Find the matching history entry for the current tournament.
        string tid = cache?.tournamentId ?? "";
        for (int i = 0; i < arena.recentTournamentHistory.Count; i++)
        {
            var hist = arena.recentTournamentHistory[i];
            if (hist == null) continue;
            if (!string.Equals(hist.tournamentId, tid, StringComparison.Ordinal)) continue;

            var rw = hist.rewardResult;
            if (rw == null || !rw.wasGranted) return "Rewards pending";

            var parts = new System.Collections.Generic.List<string>();
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

        return "";
    }

    private void HandleEnter() => _onEnterClicked?.Invoke();
    private void HandleView()  => _onViewClicked?.Invoke();

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
