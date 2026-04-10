// Assets/Scripts/Arena/UI/ArenaMainPanelUI.cs
// BRN Arena v1 — Main arena panel with header, player stats, current week card,
// action buttons, and tournament history list.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Root MonoBehaviour for the Arena main panel.
/// Wired to a PanelEntry with <see cref="PanelId.ArenaMain"/> in UIManager.
/// </summary>
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
    [SerializeField] private Button historyButton;

    [Header("History List")]
    [SerializeField] private Transform historyListRoot;
    [SerializeField] private ArenaHistoryCardUI historyCardPrefab;
    [SerializeField] private ScrollRect historyScrollRect;
    [SerializeField] private GameObject historyEmptyLabel;

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
    }

    void OnEnable()
    {
        // ── Events ──
        GameEvents.OnResourcesChanged += RefreshAll;
        GameEvents.ArenaDataChanged += RefreshAll;

        // ── Buttons ──
        if (buyTicketButton)       { buyTicketButton.onClick.RemoveAllListeners();       buyTicketButton.onClick.AddListener(HandleBuyTicket); }
        if (editTeamButton)        { editTeamButton.onClick.RemoveAllListeners();        editTeamButton.onClick.AddListener(HandleEditTeam); }
        if (enterTournamentButton) { enterTournamentButton.onClick.RemoveAllListeners(); enterTournamentButton.onClick.AddListener(HandleEnterTournament); }
        if (viewTournamentButton)  { viewTournamentButton.onClick.RemoveAllListeners();  viewTournamentButton.onClick.AddListener(HandleViewTournament); }
        if (historyButton)         { historyButton.onClick.RemoveAllListeners();         historyButton.onClick.AddListener(HandleToggleHistory); }

        // ── Week card callbacks ──
        if (weekCard) weekCard.Bind(HandleEnterTournament, HandleViewTournament);

        // ── Onboarding ──
        if (ArenaOnboardingManager.NeedsOnboarding())
        {
            ArenaOnboardingManager.TryAdvanceOnboarding();
        }
        // Force username popup if the player still has no username,
        // even if the normal onboarding flow was already completed.
        else if (!ArenaSaveHelper.HasArenaUsername())
        {
            ForceShowUsernamePopup();
        }

        RefreshAll();
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= RefreshAll;
        GameEvents.ArenaDataChanged -= RefreshAll;

        if (buyTicketButton)       buyTicketButton.onClick.RemoveListener(HandleBuyTicket);
        if (editTeamButton)        editTeamButton.onClick.RemoveListener(HandleEditTeam);
        if (enterTournamentButton) enterTournamentButton.onClick.RemoveListener(HandleEnterTournament);
        if (viewTournamentButton)  viewTournamentButton.onClick.RemoveListener(HandleViewTournament);
        if (historyButton)         historyButton.onClick.RemoveListener(HandleToggleHistory);
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
            usernameLabel.text = !string.IsNullOrEmpty(arena?.arenaUsername) ? arena.arenaUsername : "Set Username";

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
            tournamentsEnteredLabel.text = stats != null ? stats.tournamentsEntered.ToString() : "0";

        if (highestRankMonthLabel)
        {
            int rank = stats != null ? stats.highestRankThisMonth : 0;
            highestRankMonthLabel.text = rank > 0 ? GetOrdinal(rank) : "—";
        }

        if (avgRankAllTimeLabel)
        {
            if (stats != null && stats.tournamentsEntered > 0)
            {
                float avg = (float)stats.totalPlacementSum / stats.tournamentsEntered;
                avgRankAllTimeLabel.text = avg.ToString("F1");
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

        // Enter — only when registration is open and not already entered
        bool canEnter = status == ArenaPlayerTournamentStatus.NotEntered
                     && ArenaTeamValidator.IsBattleTeamComplete()
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

        // History — visible when we have any history
        if (historyButton)
            historyButton.interactable = ArenaSaveHelper.GetTournamentHistoryCount() > 0;
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
        // Entry logic will be wired to the arena tournament entry service.
        // For now, validate prerequisites and show a toast.
        if (!ArenaTeamValidator.IsBattleTeamComplete())
        {
            GameEvents.RaiseToast("Complete your Battle Team first.");
            return;
        }

        if (ArenaTicketManager.GetTicketCount() <= 0)
        {
            GameEvents.RaiseToast("You need an Arena Ticket to enter.");
            return;
        }

        // Spend ticket.
        if (!ArenaTicketManager.TrySpendTicket())
        {
            GameEvents.RaiseToast("Unable to spend ticket.");
            return;
        }

        GameEvents.RaiseToast("Entered this week's tournament!");
        GameEvents.ArenaDataChanged?.Invoke();
        RefreshAll();
    }

    private void HandleViewTournament()
    {
        if (UIManager.I) UIManager.I.Show(PanelId.ArenaTournamentDetail);

        var root = UIManager.I != null ? UIManager.I.GetRoot(PanelId.ArenaTournamentDetail) : null;
        if (root != null)
        {
            var detail = root.GetComponent<ArenaTournamentDetailPanelUI>();
            if (detail != null) detail.ShowCurrent();
        }
    }

    private void HandleToggleHistory()
    {
        // Scroll the history list into view or toggle visibility.
        if (historyScrollRect != null)
            historyScrollRect.normalizedPosition = Vector2.one; // scroll to top
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
    //  Helpers
    // ═════════════════════════════════════════════════════════════

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
