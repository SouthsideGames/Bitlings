using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerRestPanelUI
/// Appears only on wins % 3 == 0.
/// Choices:
/// - Heal party 25%
/// - Buff: +1 level to a random party member
/// </summary>
public sealed class IronCareerRestPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI headerLabel;
    [SerializeField] private Button healButton;
    [SerializeField] private Button buffButton;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();

        if (healButton) healButton.onClick.AddListener(() => manager?.OnRestHeal());
        if (buffButton) buffButton.onClick.AddListener(() => manager?.OnRestBuff());
    }

    private void OnEnable()
    {
        if (headerLabel) headerLabel.text = "Rest";
    }

    private void OnDestroy()
    {
        if (healButton) healButton.onClick.RemoveAllListeners();
        if (buffButton) buffButton.onClick.RemoveAllListeners();
    }
}
