// Assets/Scripts/Arena/UI/ArenaHistoryCardUI.cs
// BRN Arena v1 — List item for the tournament history scroll in ArenaMainPanelUI.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a single completed tournament summary in the history list.
/// Instantiated from a prefab by <see cref="ArenaMainPanelUI"/>.
/// </summary>
public class ArenaHistoryCardUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI weekLabel;
    [SerializeField] private TextMeshProUGUI placementLabel;
    [SerializeField] private TextMeshProUGUI rewardLabel;
    [SerializeField] private Button openButton;

    private ArenaTournamentHistoryEntry _data;
    private Action<ArenaTournamentHistoryEntry> _onClick;

    void Awake()
    {
        if (openButton == null)
            openButton = GetComponent<Button>();

        if (openButton)
        {
            openButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(HandleClick);
        }
    }

    void OnDestroy()
    {
        if (openButton) openButton.onClick.RemoveListener(HandleClick);
    }

    /// <summary>
    /// Populates the card with history data.
    /// </summary>
    public void Setup(ArenaTournamentHistoryEntry entry, Action<ArenaTournamentHistoryEntry> onClick)
    {
        _data = entry;
        _onClick = onClick;

        if (entry == null)
        {
            if (weekLabel) weekLabel.text = "";
            if (placementLabel) placementLabel.text = "";
            if (rewardLabel) rewardLabel.text = "";
            return;
        }

        if (weekLabel)
        {
            try
            {
                var tz = ArenaConstants.EasternTimeZone;
                var start = TimeZoneInfo.ConvertTime(
                    DateTimeOffset.FromUnixTimeSeconds(entry.weekStartUtc), tz);
                weekLabel.text = $"Week of {start:MMM d}";
            }
            catch
            {
                weekLabel.text = "Past Tournament";
            }
        }

        if (placementLabel)
            placementLabel.text = entry.finalPlacement > 0 ? GetOrdinal(entry.finalPlacement) : "—";

        if (rewardLabel)
        {
            var rw = entry.rewardResult;
            if (rw != null && rw.wasGranted && rw.creditsAwarded > 0)
                rewardLabel.text = $"+{rw.creditsAwarded} Credits";
            else
                rewardLabel.text = "";
        }
    }

    private void HandleClick()
    {
        if (_data != null)
            _onClick?.Invoke(_data);
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
