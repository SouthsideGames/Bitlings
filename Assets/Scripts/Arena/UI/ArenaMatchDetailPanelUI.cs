using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class ArenaMatchDetailPanelUI : MonoBehaviour
{
    // ═════════════════════════════════════════════════════════════
    //  Inspector references
    // ═════════════════════════════════════════════════════════════

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI matchTitleLabel;
    [SerializeField] private Button closeButton;

    [Header("Result")]
    [SerializeField] private TextMeshProUGUI resultLabel;
    [SerializeField] private TextMeshProUGUI turnCountLabel;
    [SerializeField] private TextMeshProUGUI timestampLabel;

    [Header("Player Team")]
    [SerializeField] private TextMeshProUGUI playerTeamHeader;
    [SerializeField] private TextMeshProUGUI playerSlot1Label;
    [SerializeField] private TextMeshProUGUI playerSlot2Label;
    [SerializeField] private TextMeshProUGUI playerSlot3Label;

    [Header("Opponent Team")]
    [SerializeField] private TextMeshProUGUI opponentTeamHeader;
    [SerializeField] private TextMeshProUGUI opponentSlot1Label;
    [SerializeField] private TextMeshProUGUI opponentSlot2Label;
    [SerializeField] private TextMeshProUGUI opponentSlot3Label;
    [SerializeField] private TextMeshProUGUI visibilityModeLabel;

    [Header("Battle Log")]
    [SerializeField] private Transform logListRoot;
    [SerializeField] private TextMeshProUGUI logEntry;
    [SerializeField] private ScrollRect logScrollRect;

    [Header("Colors")]
    [SerializeField] private Color winColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color lossColor = new Color(0.9f, 0.3f, 0.3f);

    // ═════════════════════════════════════════════════════════════
    //  State
    // ═════════════════════════════════════════════════════════════

    private ArenaMatchHistoryEntry _match;
    private ArenaTeamSnapshot _playerSnapshot;
    private ArenaTeamSnapshot _opponentSnapshot;
    private readonly List<GameObject> _logRows = new List<GameObject>();

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void OnEnable()
    {
        if (closeButton) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(HandleClose); }
    }

    void OnDisable()
    {
        if (closeButton) closeButton.onClick.RemoveListener(HandleClose);
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Shows match detail from a history entry.
    /// Team snapshots are optional — when provided, team slots display species/title.
    /// </summary>
    public void Show(ArenaMatchHistoryEntry match,
                     ArenaTeamSnapshot playerSnapshot = null,
                     ArenaTeamSnapshot opponentSnapshot = null)
    {
        _match = match;
        _playerSnapshot = playerSnapshot;
        _opponentSnapshot = opponentSnapshot;

        Populate();
    }

    // ═════════════════════════════════════════════════════════════
    //  Population
    // ═════════════════════════════════════════════════════════════

    private void Populate()
    {
        if (_match == null) return;

        // ── Header ──
        if (matchTitleLabel)
            matchTitleLabel.text = $"Round {_match.roundIndex + 1}";

        // ── Result ──
        if (resultLabel)
        {
            resultLabel.text = _match.playerWon ? "VICTORY" : "DEFEAT";
            resultLabel.color = _match.playerWon ? winColor : lossColor;
        }

        if (turnCountLabel)
            turnCountLabel.text = $"{_match.turnCount} turn{(_match.turnCount != 1 ? "s" : "")}";

        if (timestampLabel)
            timestampLabel.text = FormatTimestamp(_match.processedUtc);

        // ── Player team ──
        if (playerTeamHeader) playerTeamHeader.text = "Your Team";
        PopulateTeamSlots(
            _playerSnapshot,
            playerSlot1Label, playerSlot2Label, playerSlot3Label,
            revealAll: true);

        // ── Opponent team (respect visibility) ──
        string oppName = !string.IsNullOrEmpty(_match.opponentDisplayName)
            ? _match.opponentDisplayName : "Opponent";
        if (opponentTeamHeader) opponentTeamHeader.text = oppName;

        bool opponentFullReveal = ShouldRevealOpponent();
        PopulateTeamSlots(
            _opponentSnapshot,
            opponentSlot1Label, opponentSlot2Label, opponentSlot3Label,
            revealAll: opponentFullReveal);

        if (visibilityModeLabel)
        {
            if (!opponentFullReveal)
                visibilityModeLabel.text = "Limited Reveal — details hidden";
            else
                visibilityModeLabel.text = "";
        }

        // ── Battle log ──
        PopulateBattleLog();
    }

    // ═════════════════════════════════════════════════════════════
    //  Team slot population
    // ═════════════════════════════════════════════════════════════

    private void PopulateTeamSlots(
        ArenaTeamSnapshot snapshot,
        TextMeshProUGUI slot1, TextMeshProUGUI slot2, TextMeshProUGUI slot3,
        bool revealAll)
    {
        var labels = new[] { slot1, slot2, slot3 };

        if (snapshot == null || snapshot.slotSnapshots == null)
        {
            for (int i = 0; i < labels.Length; i++)
                if (labels[i]) labels[i].text = revealAll ? "—" : "???";
            return;
        }

        for (int i = 0; i < labels.Length; i++)
        {
            if (!labels[i]) continue;

            if (i >= snapshot.slotSnapshots.Count)
            {
                labels[i].text = "—";
                continue;
            }

            var slot = snapshot.slotSnapshots[i];
            if (slot == null)
            {
                labels[i].text = "—";
                continue;
            }

            if (revealAll)
            {
                string title = !string.IsNullOrEmpty(slot.titleName) ? $" [{slot.titleName}]" : "";
                labels[i].text = $"{slot.monsterName}{title}";
            }
            else
            {
                // LimitedReveal — show type silhouette only.
                labels[i].text = !string.IsNullOrEmpty(slot.publicInfo)
                    ? slot.publicInfo
                    : $"{slot.monsterType} type";
            }
        }
    }

    /// <summary>
    /// Determines whether the opponent's team details should be fully revealed.
    /// Full reveal when: match is resolved, opponent uses FullReveal mode, or
    /// the match has already been played (post-match reveal).
    /// </summary>
    private bool ShouldRevealOpponent()
    {
        // After match completion, always reveal.
        if (_match != null && _match.processedUtc > 0)
            return true;

        // If we have a snapshot and it's FullReveal, show everything.
        if (_opponentSnapshot != null && _opponentSnapshot.visibilityMode == ArenaVisibilityMode.FullReveal)
            return true;

        return false;
    }

    // ═════════════════════════════════════════════════════════════
    //  Battle log
    // ═════════════════════════════════════════════════════════════

    private void PopulateBattleLog()
    {
        ClearLogRows();

        if (_match == null || _match.battleLog == null || _match.battleLog.Count == 0)
            return;

        for (int i = 0; i < _match.battleLog.Count; i++)
        {
            var evt = _match.battleLog[i];
            if (evt == null) continue;

            if (logEntry == null || logListRoot == null) break;

            var row = Instantiate(logEntry, logListRoot);
            row.text = FormatLogEvent(evt);
            _logRows.Add(row.gameObject);
        }

        // Scroll to top.
        if (logScrollRect != null)
            logScrollRect.normalizedPosition = Vector2.one;
    }

    private void ClearLogRows()
    {
        for (int i = 0; i < _logRows.Count; i++)
        {
            if (_logRows[i] != null)
                Destroy(_logRows[i]);
        }
        _logRows.Clear();
    }

    private static string FormatLogEvent(ArenaBattleLogEvent evt)
    {
        string side = evt.side == 0 ? "L" : "R";
        string turn = $"T{evt.turn + 1}";

        switch (evt.eventType)
        {
            case ArenaBattleLogEventType.TurnStart:
                return $"── Turn {evt.turn + 1} ──";
            case ArenaBattleLogEventType.Damage:
                return $"[{turn}] [{side}] {evt.description} ({evt.value} dmg)";
            case ArenaBattleLogEventType.Heal:
                return $"[{turn}] [{side}] {evt.description} (+{evt.value} HP)";
            case ArenaBattleLogEventType.Knockout:
                return $"[{turn}] [{side}] {evt.description}";
            case ArenaBattleLogEventType.Victory:
                return $"── {evt.description} ──";
            default:
                return !string.IsNullOrEmpty(evt.description)
                    ? $"[{turn}] [{side}] {evt.description}"
                    : $"[{turn}] [{side}] {evt.eventType}";
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    private void HandleClose()
    {
        if (UIManager.I) UIManager.I.Hide(PanelId.ArenaMatchDetail);
    }

    private static string FormatTimestamp(long unixUtc)
    {
        if (unixUtc <= 0) return "";

        try
        {
            var tz = ArenaConstants.EasternTimeZone;
            var dt = TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeSeconds(unixUtc), tz);
            return dt.ToString("MMM d, h:mm tt") + " ET";
        }
        catch
        {
            return "";
        }
    }
}
