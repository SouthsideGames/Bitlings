// Assets/Scripts/Arena/UI/ArenaBracketMatchRowUI.cs

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single match row inside <see cref="ArenaBracketPanelUI"/>.
/// Shows both sides of a match, highlights the player's entry, and fires a
/// callback when clicked (only for resolved matches the player was part of).
/// </summary>
public class ArenaBracketMatchRowUI : MonoBehaviour
{
    [Header("Left Side")]
    [SerializeField] private TextMeshProUGUI leftNameLabel;
    [SerializeField] private Image leftResultBadge;
    [SerializeField] private Image leftSlot1Icon;
    [SerializeField] private Image leftSlot2Icon;
    [SerializeField] private Image leftSlot3Icon;

    [Header("Right Side")]
    [SerializeField] private TextMeshProUGUI rightNameLabel;
    [SerializeField] private Image rightResultBadge;
    [SerializeField] private Image rightSlot1Icon;
    [SerializeField] private Image rightSlot2Icon;
    [SerializeField] private Image rightSlot3Icon;

    [Header("Centre")]
    [SerializeField] private TextMeshProUGUI vsLabel;

    [Header("Interaction")]
    [SerializeField] private Button rowButton;

    // ── colours ──────────────────────────────────────────────────────
    private static readonly Color WinColor  = new Color32(0x4C, 0xAF, 0x50, 0xFF); // green
    private static readonly Color LossColor = new Color32(0xE5, 0x39, 0x35, 0xFF); // red
    private static readonly Color TbdColor  = new Color32(0x80, 0x80, 0x80, 0xFF); // grey
    private static readonly Color PlayerHighlight = new Color32(0xFF, 0xD7, 0x00, 0xFF); // gold

    // ═════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configures the row for <paramref name="match"/> inside <paramref name="record"/>.
    /// </summary>
    public void Setup(
        ArenaTournamentMatch match,
        ArenaTournamentRecord record,
        string playerEntryId,
        Action<ArenaMatchHistoryEntry> onClicked)
    {
        if (match == null || record == null) return;

        bool playerIsLeft  = string.Equals(match.leftEntryId,  playerEntryId, StringComparison.Ordinal);
        bool playerIsRight = string.Equals(match.rightEntryId, playerEntryId, StringComparison.Ordinal);
        bool resolved      = !string.IsNullOrEmpty(match.winnerEntryId);
        bool playerInvolved = playerIsLeft || playerIsRight;

        string leftName  = ResolveDisplayName(record, match.leftEntryId);
        string rightName = ResolveDisplayName(record, match.rightEntryId);

        // Names
        if (leftNameLabel)
        {
            leftNameLabel.text = leftName;
            leftNameLabel.color = playerIsLeft ? PlayerHighlight : Color.white;
            leftNameLabel.fontStyle = playerIsLeft ? FontStyles.Bold : FontStyles.Normal;
        }

        if (rightNameLabel)
        {
            rightNameLabel.text = rightName;
            rightNameLabel.color = playerIsRight ? PlayerHighlight : Color.white;
            rightNameLabel.fontStyle = playerIsRight ? FontStyles.Bold : FontStyles.Normal;
        }

        // Team icons
        var leftEntry  = FindEntry(record, match.leftEntryId);
        var rightEntry = FindEntry(record, match.rightEntryId);
        FillTeamIcons(leftEntry?.teamSnapshot,  leftSlot1Icon,  leftSlot2Icon,  leftSlot3Icon);
        FillTeamIcons(rightEntry?.teamSnapshot, rightSlot1Icon, rightSlot2Icon, rightSlot3Icon);

        // Centre label
        if (vsLabel) vsLabel.text = "vs";

        // Result badges
        if (resolved)
        {
            bool leftWon = string.Equals(match.winnerEntryId, match.leftEntryId, StringComparison.Ordinal);
            SetBadge(leftResultBadge,  leftWon  ? WinColor : LossColor);
            SetBadge(rightResultBadge, !leftWon ? WinColor : LossColor);
        }
        else
        {
            SetBadge(leftResultBadge,  TbdColor);
            SetBadge(rightResultBadge, TbdColor);
        }

        // Button — active for any resolved match so all battles can be inspected
        if (rowButton)
        {
            rowButton.interactable = resolved;
            rowButton.onClick.RemoveAllListeners();

            if (resolved && onClicked != null)
            {
                // For matches the player isn't in, treat left side as the "player" perspective.
                bool viewerIsLeft = playerInvolved ? playerIsLeft : true;
                string viewerEntryId = playerInvolved ? playerEntryId : match.leftEntryId;
                var histEntry = BuildHistoryEntry(match, record, viewerEntryId, viewerIsLeft);
                rowButton.onClick.AddListener(() => onClicked(histEntry));
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static void SetBadge(Image badge, Color color)
    {
        if (badge == null) return;
        badge.color = color;
    }

    private static string ResolveDisplayName(ArenaTournamentRecord record, string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return "TBD";

        var entry = FindEntry(record, entryId);
        if (entry == null) return "Unknown";

        if (entry.teamSnapshot != null && !string.IsNullOrEmpty(entry.teamSnapshot.ownerDisplayName))
            return entry.teamSnapshot.ownerDisplayName;

        if (!string.IsNullOrEmpty(entry.displayNameSnapshot))
            return entry.displayNameSnapshot;

        return entry.isBot ? "Bot" : "Player";
    }

    private static ArenaMatchHistoryEntry BuildHistoryEntry(
        ArenaTournamentMatch match,
        ArenaTournamentRecord record,
        string playerEntryId,
        bool playerIsLeft)
    {
        string opponentEntryId = playerIsLeft ? match.rightEntryId : match.leftEntryId;
        var opponent = FindEntry(record, opponentEntryId);
        var player   = FindEntry(record, playerEntryId);

        return new ArenaMatchHistoryEntry
        {
            matchId              = match.matchId,
            tournamentId         = match.tournamentId,
            roundIndex           = match.roundIndex,
            opponentDisplayName  = opponent?.teamSnapshot?.ownerDisplayName ?? "Unknown",
            opponentIsBot        = opponent?.isBot ?? false,
            opponentArenaScore   = opponent?.arenaScore ?? 0,
            playerWon            = string.Equals(match.winnerEntryId, playerEntryId, StringComparison.Ordinal),
            turnCount            = match.turnCount,
            processedUtc         = match.processedUtc,
            playerSnapshot       = player?.teamSnapshot,
            opponentSnapshot     = opponent?.teamSnapshot,
            battleLog            = match.battleLog ?? new List<ArenaBattleLogEvent>()
        };
    }

    private static ArenaTournamentEntry FindEntry(ArenaTournamentRecord record, string entryId)
    {
        if (record?.entries == null || string.IsNullOrEmpty(entryId)) return null;
        for (int i = 0; i < record.entries.Count; i++)
        {
            if (string.Equals(record.entries[i]?.entryId, entryId, StringComparison.Ordinal))
                return record.entries[i];
        }
        return null;
    }

    private static void FillTeamIcons(ArenaTeamSnapshot snapshot, Image slot1, Image slot2, Image slot3)
    {
        var icons = new[] { slot1, slot2, slot3 };
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] == null) continue;

            var bitling = snapshot?.slotSnapshots != null && i < snapshot.slotSnapshots.Count
                ? snapshot.slotSnapshots[i]
                : null;

            if (bitling != null && !string.IsNullOrEmpty(bitling.monsterId))
            {
                var def = MonsterLibraryLocator.GetById(bitling.monsterId);
                if (def != null)
                {
                    icons[i].sprite = MonsterNameFormatter.GetIcon(def, isPremium: false, backIcon: false);
                    icons[i].enabled = true;
                    continue;
                }
            }

            icons[i].enabled = false;
        }
    }
}
