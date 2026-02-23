using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: Forced Evolution step.
/// This must block progress, even if no evolution occurs.
/// </summary>
public sealed class IronCareerForcedEvolutionUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI headerLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Button continueButton;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (continueButton) continueButton.onClick.AddListener(() => manager?.OnForcedEvolveContinue());
    }

    private void OnDestroy()
    {
        if (continueButton) continueButton.onClick.RemoveAllListeners();
    }

    public void Bind(bool evolved, string beforeName, string afterName)
    {
        if (headerLabel) headerLabel.text = "Forced Evolution";

        if (bodyLabel)
        {
            if (evolved)
                bodyLabel.text = $"{beforeName} evolved into {afterName}.";
            else
                bodyLabel.text = "No evolution available for your active monster.";
        }
    }
}
