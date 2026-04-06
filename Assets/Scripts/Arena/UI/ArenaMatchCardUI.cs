// Assets/Scripts/Arena/UI/ArenaMatchCardUI.cs
// BRN Arena v1 — List item for a single match in the tournament detail panel.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a single match result row inside the tournament detail panel.
/// Instantiated from a prefab by <see cref="ArenaTournamentDetailPanelUI"/>.
/// </summary>
public class ArenaMatchCardUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI roundLabel;
    [SerializeField] private TextMeshProUGUI opponentLabel;
    [SerializeField] private TextMeshProUGUI resultLabel;
    [SerializeField] private TextMeshProUGUI turnsLabel;
    [SerializeField] private Image resultIcon;
    [SerializeField] private Button rootButton;

    [Header("Colors")]
    [SerializeField] private Color winColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color lossColor = new Color(0.9f, 0.3f, 0.3f);

    private ArenaMatchHistoryEntry _data;
    private Action<ArenaMatchHistoryEntry> _onClick;

    void Awake()
    {
        if (rootButton == null)
            rootButton = GetComponent<Button>();

        if (rootButton)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.onClick.AddListener(HandleClick);
        }
    }

    void OnDestroy()
    {
        if (rootButton) rootButton.onClick.RemoveListener(HandleClick);
    }

    /// <summary>
    /// Populates the card with match data from the player's perspective.
    /// </summary>
    public void Setup(ArenaMatchHistoryEntry entry, Action<ArenaMatchHistoryEntry> onClick)
    {
        _data = entry;
        _onClick = onClick;

        if (entry == null)
        {
            ClearLabels();
            return;
        }

        if (roundLabel)
            roundLabel.text = $"Round {entry.roundIndex + 1}";

        if (opponentLabel)
        {
            string name = !string.IsNullOrEmpty(entry.opponentDisplayName)
                ? entry.opponentDisplayName
                : "Unknown";
            if (entry.opponentIsBot) name += " (Bot)";
            opponentLabel.text = $"vs {name}";
        }

        if (resultLabel)
        {
            resultLabel.text = entry.playerWon ? "WIN" : "LOSS";
            resultLabel.color = entry.playerWon ? winColor : lossColor;
        }

        if (turnsLabel)
            turnsLabel.text = $"{entry.turnCount} turn{(entry.turnCount != 1 ? "s" : "")}";
    }

    private void ClearLabels()
    {
        if (roundLabel)    roundLabel.text = "";
        if (opponentLabel) opponentLabel.text = "";
        if (resultLabel)   resultLabel.text = "";
        if (turnsLabel)    turnsLabel.text = "";
    }

    private void HandleClick()
    {
        if (_data != null)
            _onClick?.Invoke(_data);
    }
}
