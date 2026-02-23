using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerGameOverPanelUI
/// </summary>
public sealed class IronCareerGameOverPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Button returnToMenuButton;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (returnToMenuButton) returnToMenuButton.onClick.AddListener(() => manager?.ReturnToMenuFromGameOver());
    }

    private void OnDestroy()
    {
        if (returnToMenuButton) returnToMenuButton.onClick.RemoveAllListeners();
    }

    public void Bind(int wins, bool forfeited)
    {
        if (titleLabel) titleLabel.text = forfeited ? "Forfeit" : "Game Over";
        if (bodyLabel) bodyLabel.text = $"Wins: {Mathf.Max(0, wins)}";
    }
}
