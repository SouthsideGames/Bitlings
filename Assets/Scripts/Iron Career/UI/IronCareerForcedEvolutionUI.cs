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

    public void Bind(bool evolved, int evolvedCount)
    {
        if (headerLabel) headerLabel.text = "Forced Evolution";

        if (bodyLabel)
        {
            if (evolved)
                bodyLabel.text = evolvedCount > 1
                    ? $"{evolvedCount} party members evolved."
                    : "1 party member evolved.";
            else
                bodyLabel.text = "No evolution available in your party.";
        }
    }
}
