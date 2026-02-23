using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerRulesPopupUI
/// "Quit = forfeit" confirmation.
/// </summary>
public sealed class IronCareerRulesPopupUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Button confirmQuitButton;
    [SerializeField] private Button cancelButton;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (confirmQuitButton) confirmQuitButton.onClick.AddListener(() => manager?.ConfirmQuitForfeit());
        if (cancelButton) cancelButton.onClick.AddListener(() => manager?.CancelQuit());
    }

    private void OnEnable()
    {
        if (bodyLabel)
            bodyLabel.text = "Iron Career is sealed mode. Quitting or app pause forfeits the run.";
    }

    private void OnDestroy()
    {
        if (confirmQuitButton) confirmQuitButton.onClick.RemoveAllListeners();
        if (cancelButton) cancelButton.onClick.RemoveAllListeners();
    }
}
