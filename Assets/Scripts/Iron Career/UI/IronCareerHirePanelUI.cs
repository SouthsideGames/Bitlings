using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerHirePanelUI
/// Shows the wild monster (from the just-finished battle) and allows Hire/Skip.
/// In Hardcore, skip should be disabled and hire is forced.
/// </summary>
public sealed class IronCareerHirePanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("UI")]
    [SerializeField] private Image portrait;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI titleLabel;

    [Header("Buttons")]
    [SerializeField] private Button hireButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private TextMeshProUGUI skipLabel;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (hireButton) hireButton.onClick.AddListener(() => manager?.OnHireAccepted());
        if (skipButton) skipButton.onClick.AddListener(() => manager?.OnHireSkipped());
    }

    private void OnDestroy()
    {
        if (hireButton) hireButton.onClick.RemoveAllListeners();
        if (skipButton) skipButton.onClick.RemoveAllListeners();
    }

    public void Bind(IronMonster offer, bool skipAllowed)
    {
        if (portrait) portrait.sprite = offer != null && offer.def ? offer.def.icon : null;
        if (nameLabel) nameLabel.text = offer != null && offer.def ? offer.def.displayName : "-";
        if (levelLabel) levelLabel.text = offer != null ? $"Lv {Mathf.Max(1, offer.level)}" : string.Empty;
        if (titleLabel) titleLabel.text = (offer != null && offer.lockedTitle) ? offer.lockedTitle.displayName : "";

        if (skipButton) skipButton.interactable = skipAllowed;
        if (skipLabel) skipLabel.text = skipAllowed ? "Skip" : "Skip (Hardcore)";
    }
}
