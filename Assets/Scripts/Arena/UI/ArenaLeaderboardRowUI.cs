// Assets/Scripts/Arena/UI/ArenaLeaderboardRowUI.cs
// BRN Arena v1 — A single row in the leaderboard list.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays one leaderboard entry (rank, name, score).
/// Instantiated from a prefab by <see cref="ArenaLeaderboardPanelUI"/>.
/// </summary>
public class ArenaLeaderboardRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI rankLabel;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI scoreLabel;
    [SerializeField] private TextMeshProUGUI tierLabel;
    [SerializeField] private Image backgroundImage;

    [Header("Styling")]
    [SerializeField] private Color normalColor = new Color(0.15f, 0.15f, 0.2f, 0.6f);
    [SerializeField] private Color highlightColor = new Color(0.3f, 0.6f, 0.9f, 0.4f);
    [SerializeField] private Color goldColor = new Color(1f, 0.84f, 0f, 1f);
    [SerializeField] private Color silverColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    [SerializeField] private Color bronzeColor = new Color(0.8f, 0.5f, 0.2f, 1f);

    public void Setup(int rank, string playerName, string scoreText, bool isLocalPlayer, string tierId = null)
    {
        if (rankLabel)
        {
            rankLabel.text = rank.ToString();
            if (rank == 1) rankLabel.color = goldColor;
            else if (rank == 2) rankLabel.color = silverColor;
            else if (rank == 3) rankLabel.color = bronzeColor;
            else rankLabel.color = Color.white;
        }

        if (nameLabel) nameLabel.text = playerName ?? "???";
        if (scoreLabel) scoreLabel.text = scoreText;
        if (backgroundImage) backgroundImage.color = isLocalPlayer ? highlightColor : normalColor;

        if (tierLabel)
        {
            if (!string.IsNullOrEmpty(tierId))
            {
                tierLabel.gameObject.SetActive(true);
                tierLabel.text = FormatTierName(tierId);
                tierLabel.color = GetTierColor(tierId);
            }
            else
            {
                tierLabel.gameObject.SetActive(false);
            }
        }
    }

    private static string FormatTierName(string tierId)
    {
        switch (tierId)
        {
            case "champion":    return "Champion";
            case "master":      return "Master";
            case "contender":   return "Contender";
            case "competitor":  return "Competitor";
            case "participant": return "Participant";
            case "legend":      return "Legend";
            case "veteran":     return "Veteran";
            case "rising_star": return "Rising Star";
            case "newcomer":    return "Newcomer";
            default:            return tierId;
        }
    }

    private static Color GetTierColor(string tierId)
    {
        switch (tierId)
        {
            case "champion":
            case "legend":      return new Color(1f, 0.84f, 0f);       // Gold
            case "master":
            case "veteran":     return new Color(0.7f, 0.45f, 1f);     // Purple
            case "contender":
            case "rising_star": return new Color(0.3f, 0.7f, 1f);      // Blue
            case "competitor":
            case "newcomer":    return new Color(0.5f, 0.9f, 0.5f);    // Green
            default:            return Color.white;
        }
    }
}
