using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RewardBitlingItem : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text countText;

    [Header("Optional Credits")]
    [SerializeField] private TMP_Text creditsText;

    [Header("Badges")]
    [SerializeField] private GameObject newBadge;

    // Keep your existing API
    public void Bind(string displayName, int count, bool isNew)
    {
        if (nameText) nameText.text = displayName;
        if (countText) countText.text = $"x{count}";
        if (newBadge) newBadge.SetActive(isNew);

        if (creditsText)
            creditsText.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // Overloads used by IdleBattleRewardPanelUI
    // ─────────────────────────────────────────────────────────────

    public void Set(Sprite icon, string displayName, int count)
    {
        ApplyIcon(icon);
        Bind(displayName, count, false);
    }

    public void Set(Sprite icon, string displayName, int count, bool isNew)
    {
        ApplyIcon(icon);
        Bind(displayName, count, isNew);
    }

    public void Set(Sprite icon, string displayName, int count, int creditsEarnedForThatMonster)
    {
        ApplyIcon(icon);
        Bind(displayName, count, false);
        ApplyCredits(creditsEarnedForThatMonster);
    }

    public void Set(Sprite icon, string displayName, int count, bool isNew, int creditsEarnedForThatMonster)
    {
        ApplyIcon(icon);
        Bind(displayName, count, isNew);
        ApplyCredits(creditsEarnedForThatMonster);
    }

    // Keep your string-only variant (if anything else uses it)
    public void Set(string displayName, int count, bool isNew, int creditsEarnedForThatMonster)
    {
        ApplyIcon(null);
        Bind(displayName, count, isNew);
        ApplyCredits(creditsEarnedForThatMonster);
    }

    private void ApplyIcon(Sprite icon)
    {
        if (!iconImage) return;

        iconImage.sprite = icon;
        iconImage.enabled = icon != null;
    }

    private void ApplyCredits(int creditsEarnedForThatMonster)
    {
        if (!creditsText) return;

        // Hide if 0 to reduce noise (change if you want "+0")
        if (creditsEarnedForThatMonster <= 0)
        {
            creditsText.gameObject.SetActive(false);
            return;
        }

        creditsText.gameObject.SetActive(true);
        creditsText.text = $"+{creditsEarnedForThatMonster:N0}";
    }
}
