using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ArenaMainPanelUI : MonoBehaviour
{
    // ── Singleton (optional — mirrors other panels) ──
    public static ArenaMainPanelUI I { get; private set; }

    // ═════════════════════════════════════════════════════════════
    //  Inspector references
    // ═════════════════════════════════════════════════════════════

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI usernameLabel;
    [SerializeField] private TextMeshProUGUI ticketCountLabel;
    [SerializeField] private TextMeshProUGUI ticketCostLabel;
    [SerializeField] private Button buyTicketButton;

    [Header("Player Stats")]
    [SerializeField] private TextMeshProUGUI tournamentsEnteredLabel;
    [SerializeField] private TextMeshProUGUI highestRankMonthLabel;
    [SerializeField] private TextMeshProUGUI avgRankAllTimeLabel;

    [Header("Current Week")]
    [SerializeField] private ArenaWeekCardUI weekCard;

    [Header("Action Buttons")]
    [SerializeField] private Button editTeamButton;
    [SerializeField] private Button enterTournamentButton;
    [SerializeField] private Button viewTournamentButton;

    [Header("History List")]
    [SerializeField] private Transform historyListRoot;
    [SerializeField] private ArenaHistoryCardUI historyCardPrefab;
    [SerializeField] private ScrollRect historyScrollRect;
    [SerializeField] private GameObject historyEmptyLabel;

    [Header("Leaderboard")]
    [SerializeField] private Button leaderboardButton;
    [SerializeField] private ArenaLeaderboardPanelUI leaderboardPanel;

    [Header("Navigation")]
    [SerializeField] private Button closeButton;

    [Header("Online")]
    [SerializeField] private GameObject offlineOverlay;
    [SerializeField] private TextMeshProUGUI offlineReasonLabel;
    [SerializeField] private Button retryConnectionButton;

    // ═════════════════════════════════════════════════════════════
    //  State
    // ═════════════════════════════════════════════════════════════

    private readonly List<ArenaHistoryCardUI> _historyCards = new List<ArenaHistoryCardUI>();

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        ResolveCloseButtonIfMissing();
    }

    void OnEnable()
    {
        // ── Events ──
        GameEvents.OnResourcesChanged += RefreshAll;
        GameEvents.ArenaDataChanged += RefreshAll;
        TutorialOverlayPanel.OnCompleted += OnTutorialCompleted;
        UGSInitializer.OnReady += OnUGSReady;

        // ── Buttons ──
        if (buyTicketButton)       { buyTicketButton.onClick.RemoveAllListeners();       buyTicketButton.onClick.AddListener(HandleBuyTicket); }
        if (editTeamButton)        { editTeamButton.onClick.RemoveAllListeners();        editTeamButton.onClick.AddListener(HandleEditTeam); }
        if (enterTournamentButton) { enterTournamentButton.onClick.RemoveAllListeners(); enterTournamentButton.onClick.AddListener(HandleEnterTournament); }
        if (viewTournamentButton)  { viewTournamentButton.onClick.RemoveAllListeners();  viewTournamentButton.onClick.AddListener(HandleViewTournament); }
        if (retryConnectionButton) { retryConnectionButton.onClick.RemoveAllListeners(); retryConnectionButton.onClick.AddListener(HandleRetryConnection); }
        if (leaderboardButton)      { leaderboardButton.onClick.RemoveAllListeners();      leaderboardButton.onClick.AddListener(HandleOpenLeaderboard); }
        if (closeButton)           { closeButton.onClick.RemoveAllListeners();           closeButton.onClick.AddListener(HandleClose); }

        // ── Week card callbacks ──
        if (weekCard) weekCard.Bind(HandleEnterTournament, HandleViewTournament);

        // ── Online check ──
        RefreshOfflineOverlay();

        if (!ArenaNetworkGuard.IsOnline)
        {
            // Still show whatever local data we have, but the overlay blocks interaction.
            RefreshAll();
            return;
        }

        // ── Onboarding ──
        if (ArenaOnboardingManager.NeedsOnboarding())
        {
            ArenaOnboardingManager.TryAdvanceOnboarding();
        }

        // Always force the username popup if the player has no username,
        // regardless of whether onboarding thinks it already ran.
        // No one can use the arena without a username.
        if (!ArenaSaveHelper.HasArenaUsername())
        {
            ForceShowUsernamePopup();
        }

        RefreshAll();

        // If registered and brackets might be ready, sync in background.
        TrySyncBracketOnOpen();
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= RefreshAll;
        GameEvents.ArenaDataChanged -= RefreshAll;
        TutorialOverlayPanel.OnCompleted -= OnTutorialCompleted;
        UGSInitializer.OnReady -= OnUGSReady;

        if (buyTicketButton)       buyTicketButton.onClick.RemoveListener(HandleBuyTicket);
        if (editTeamButton)        editTeamButton.onClick.RemoveListener(HandleEditTeam);
        if (enterTournamentButton) enterTournamentButton.onClick.RemoveListener(HandleEnterTournament);
        if (viewTournamentButton)  viewTournamentButton.onClick.RemoveListener(HandleViewTournament);
        if (retryConnectionButton) retryConnectionButton.onClick.RemoveListener(HandleRetryConnection);
        if (leaderboardButton)      leaderboardButton.onClick.RemoveListener(HandleOpenLeaderboard);
        if (closeButton)           closeButton.onClick.RemoveListener(HandleClose);
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    // ═════════════════════════════════════════════════════════════
    //  Refresh
    // ═════════════════════════════════════════════════════════════

    public void RefreshAll()
    {
        RefreshHeader();
        RefreshPlayerStats();
        RefreshWeekCard();
        RefreshActionButtons();
        RefreshHistoryList();
    }

    // ── Header ──

    private void RefreshHeader()
    {
        // Username
        var arena = SaveManager.GetArenaSaveData();
        if (usernameLabel)
            usernameLabel.text = $"USERNAME: {(!string.IsNullOrEmpty(arena?.arenaUsername) ? arena.arenaUsername : "Set Username")}";

        int tickets = ArenaTicketManager.GetTicketCount();
        if (ticketCountLabel) ticketCountLabel.text = $"{tickets}/{ArenaConstants.MaxTickets}";

        if (ticketCostLabel)
        {
            ticketCostLabel.text = $"{ArenaConstants.TicketCreditCost} Credits";
            ticketCostLabel.gameObject.SetActive(true);
        }

        if (buyTicketButton) buyTicketButton.interactable = ArenaTicketManager.CanBuyArenaTicket();
    }

    // ── Player stats ──

    private void RefreshPlayerStats()
    {
        var arena = SaveManager.GetArenaSaveData();
        var stats = arena?.lifetimeStats;

        if (tournamentsEnteredLabel)
            tournamentsEnteredLabel.text = $"TOURNAMENTS ENTERED: {(stats != null ? stats.tournamentsEntered : 0)}";

        if (highestRankMonthLabel)
        {
            int rank = stats != null ? stats.highestRankThisMonth : 0;
            highestRankMonthLabel.text = $"HIGHEST THIS MONTH: {(rank > 0 ? GetOrdinal(rank) : "—")}";
        }

        if (avgRankAllTimeLabel)
        {
            if (stats != null && stats.tournamentsEntered > 0)
            {
                float avg = (float)stats.totalPlacementSum / stats.tournamentsEntered;
                avgRankAllTimeLabel.text = $"AVERAGE ALL TIME: {avg.ToString("F1")}";
            }
            else
            {
                avgRankAllTimeLabel.text = "—";
            }
        }
    }

    // ── Week card ──

    private void RefreshWeekCard()
    {
        if (weekCard) weekCard.Refresh();
    }

    // ── Action buttons ──

    private void RefreshActionButtons()
    {
        var status = ArenaSaveHelper.GetPlayerTournamentStatus();

        // Edit team — always available when arena is unlocked (locked team shows visual hint)
        if (editTeamButton) editTeamButton.interactable = ArenaSaveHelper.IsArenaUnlocked();

        // Enter — only when registration is open, not already entered, has a username, and is online
        bool canEnter = status == ArenaPlayerTournamentStatus.NotEntered
                     && ArenaNetworkGuard.IsOnline
                     && ArenaSaveHelper.HasArenaUsername()
                     && ArenaTeamValidator.IsBattleTeamComplete()
                     && ArenaScheduleService.IsRegistrationOpen()
                     && ArenaTicketManager.GetTicketCount() > 0;

        if (enterTournamentButton)
        {
            enterTournamentButton.gameObject.SetActive(
                status == ArenaPlayerTournamentStatus.NotEntered);
            enterTournamentButton.interactable = canEnter;
        }

        // View — visible when entered, active, eliminated, or completed
        bool hasActiveTournament = status != ArenaPlayerTournamentStatus.NotEntered;
        if (viewTournamentButton)
            viewTournamentButton.gameObject.SetActive(hasActiveTournament);

    }

    // ── History list ──

    private void RefreshHistoryList()
    {
        ClearHistoryCards();

        var arena = SaveManager.GetArenaSaveData();
        var history = arena?.recentTournamentHistory;

        if (history == null || history.Count == 0)
        {
            if (historyEmptyLabel) historyEmptyLabel.SetActive(true);
            return;
        }

        if (historyEmptyLabel) historyEmptyLabel.SetActive(false);

        for (int i = 0; i < history.Count; i++)
        {
            if (historyCardPrefab == null || historyListRoot == null) break;

            var card = Instantiate(historyCardPrefab, historyListRoot);
            card.Setup(history[i], OnHistoryCardClicked);
            _historyCards.Add(card);
        }
    }

    private void ClearHistoryCards()
    {
        for (int i = 0; i < _historyCards.Count; i++)
        {
            if (_historyCards[i] != null)
                Destroy(_historyCards[i].gameObject);
        }
        _historyCards.Clear();
    }

    // ═════════════════════════════════════════════════════════════
    //  Button handlers
    // ═════════════════════════════════════════════════════════════

    private void HandleClose()
    {
        if (UIManager.I) UIManager.I.Hide(PanelId.ArenaMain);
    }

    private void HandleBuyTicket()
    {
        if (ArenaTicketManager.TryBuyArenaTicket())
        {
            GameEvents.RaiseToast("Arena Ticket purchased!");
            RefreshAll();
        }
    }

    private void HandleOpenLeaderboard()
    {
        if (leaderboardPanel) leaderboardPanel.gameObject.SetActive(true);
    }

    private void HandleEditTeam()
    {
        // Open Directory in Arena loadout mode.
        if (UIManager.I) UIManager.I.Show(PanelId.Directory);

        var dirPanel = UIManager.I != null ? UIManager.I.GetRoot(PanelId.Directory) : null;
        if (dirPanel != null)
        {
            var dir = dirPanel.GetComponent<DirectoryPanelUI>();
            if (dir != null) dir.OpenInArenaMode();
        }
    }

    private void HandleEnterTournament()
    {
        // Block entry if offline.
        if (!ArenaNetworkGuard.IsOnline)
        {
            GameEvents.RaiseToast(ArenaNetworkGuard.GetOfflineReason() ?? "No connection.");
            return;
        }

        // Block entry and show username popup if the player hasn't set one.
        if (!ArenaSaveHelper.HasArenaUsername())
        {
            ForceShowUsernamePopup();
            return;
        }

        // Online path — register via Cloud Code.
        EnterTournamentOnlineAsync();
    }

    private async void EnterTournamentOnlineAsync()
    {
        if (enterTournamentButton) enterTournamentButton.interactable = false;

        try
        {
            var (success, error) = await ArenaTournamentService.TryEnterTournamentAsync();

            if (success)
            {
                GameEvents.RaiseToast("Registered for this week's tournament!");
            }
            else
            {
                GameEvents.RaiseToast(error ?? "Unable to register.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArenaMainPanelUI] EnterTournamentOnlineAsync failed: {ex.Message}");
            GameEvents.RaiseToast("Unable to register right now. Please try again.");
        }
        finally
        {
            if (enterTournamentButton) enterTournamentButton.interactable = true;
            RefreshAll();
        }
    }

    /// <summary>
    /// If the player is in Registered state and brackets might be ready,
    /// sync in the background and auto-resolve any available rounds.
    /// </summary>
    private async void TrySyncBracketOnOpen()
    {
        var arena = SaveManager.GetArenaSaveData();
        var status = arena?.currentTournamentCache?.playerStatus ?? ArenaPlayerTournamentStatus.NotEntered;

        if (status == ArenaPlayerTournamentStatus.Registered && ArenaNetworkGuard.IsOnline)
        {
            var (synced, _) = await ArenaTournamentService.SyncBracketAsync();
            if (synced)
            {
                // Catch up on any available rounds
                ArenaTournamentService.ResolveAvailableRounds();
                RefreshAll();
            }
        }
        else if (status == ArenaPlayerTournamentStatus.Entered || status == ArenaPlayerTournamentStatus.Active)
        {
            // Catch up on any rounds that became available since last open
            int resolved = ArenaTournamentService.ResolveAvailableRounds();
            if (resolved > 0) RefreshAll();
        }
    }

    private void HandleViewTournament()
    {
        if (UIManager.I) UIManager.I.Show(PanelId.ArenaTournamentDetail);

        var root = UIManager.I != null ? UIManager.I.GetRoot(PanelId.ArenaTournamentDetail) : null;
        if (root != null)
        {
            var detail = root.GetComponent<ArenaTournamentDetailPanelUI>();
            if (detail != null)
            {
                detail.ShowCurrent();

                // Inject full record for match history & standings
                var record = ArenaTournamentService.GetActiveRecord();
                var arena = SaveManager.GetArenaSaveData();
                string playerEntryId = arena?.currentTournamentCache?.playerEntryId;
                if (record != null && !string.IsNullOrEmpty(playerEntryId))
                {
                    detail.PopulateMatchesFromRecord(record, playerEntryId);
                    detail.PopulateStandings(record, playerEntryId);
                }
            }
        }
    }

    private void OnHistoryCardClicked(ArenaTournamentHistoryEntry entry)
    {
        if (entry == null) return;

        if (UIManager.I) UIManager.I.Show(PanelId.ArenaTournamentDetail);

        var root = UIManager.I != null ? UIManager.I.GetRoot(PanelId.ArenaTournamentDetail) : null;
        if (root != null)
        {
            var detail = root.GetComponent<ArenaTournamentDetailPanelUI>();
            if (detail != null) detail.ShowHistory(entry);
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Tutorial callback
    // ═════════════════════════════════════════════════════════════

    private void OnTutorialCompleted(string key)
    {
        if (key != ArenaOnboardingManager.ArenaIntroTutorialKey) return;
        ArenaOnboardingManager.CompleteIntro();
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    private void RefreshOfflineOverlay()
    {
        bool online = ArenaNetworkGuard.IsOnline;
        if (offlineOverlay) offlineOverlay.SetActive(!online);
        if (offlineReasonLabel && !online)
            offlineReasonLabel.text = ArenaNetworkGuard.GetOfflineReason() ?? "";
    }

    private void OnUGSReady()
    {
        // Services came online while the panel was visible — hide overlay and run normal flow.
        RefreshOfflineOverlay();
        if (ArenaNetworkGuard.IsOnline)
        {
            if (ArenaOnboardingManager.NeedsOnboarding())
                ArenaOnboardingManager.TryAdvanceOnboarding();

            if (!ArenaSaveHelper.HasArenaUsername())
                ForceShowUsernamePopup();

            RefreshAll();
        }
    }

    private void HandleRetryConnection()
    {
        if (UGSInitializer.I != null && !UGSInitializer.I.IsReady)
            UGSInitializer.I.Retry();

        RefreshOfflineOverlay();
    }

    private void ForceShowUsernamePopup()
    {
        var popup = ArenaUsernamePopupUI.I;
        if (popup == null)
        {
            // Singleton may be null if the popup GameObject was inactive (Awake never ran).
            popup = FindAnyObjectByType<ArenaUsernamePopupUI>(FindObjectsInactive.Include);
            if (popup != null)
            {
                popup.gameObject.SetActive(true);
            }
        }

        if (popup != null)
        {
            popup.Show();
        }
        else
        {
            Debug.LogWarning("[ArenaMainPanelUI] ArenaUsernamePopupUI not found in scene.");
        }
    }

    private void ResolveCloseButtonIfMissing()
    {
        if (closeButton != null) return;

        var buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null) continue;

            string n = button.name;
            if (string.IsNullOrEmpty(n)) continue;

            n = n.ToLowerInvariant();
            if (n.Contains("close") || n.Contains("back"))
            {
                closeButton = button;
                return;
            }
        }
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
