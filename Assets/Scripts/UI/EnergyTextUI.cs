using UnityEngine;
using TMPro;

public class EnergyTextUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI energyLabel;

    private static readonly Color NotEnoughColor = new Color(1f, 0.5f, 0.5f);

    private void Awake()
    {
        if (!energyLabel)
            energyLabel = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        GameEvents.EnergyChanged += Refresh;
        GameEvents.OnResourcesChanged += Refresh; // required for reset/new-account init
        Refresh();
    }

    private void OnDisable()
    {
        GameEvents.EnergyChanged -= Refresh;
        GameEvents.OnResourcesChanged -= Refresh;
    }

    private void Refresh()
    {
        if (!energyLabel) return;

        // Prefer EncounterPanelUI/EncounterManager when available (they know max/cost)
        if (EncounterPanelUI.I != null)
        {
            int cur = EncounterPanelUI.I.GetEnergyPoints();
            int max = EncounterPanelUI.I.GetEncounterMax();
            int cost = EncounterPanelUI.I.GetEncounterCost();

            bool has = cur >= cost;
            energyLabel.text = $"{cur} / {max}";
            energyLabel.color = has ? Color.white : NotEnoughColor;
            return;
        }

        if (EncounterManager.I != null)
        {
            int cur = EncounterManager.I.GetEnergyPoints();
            int max = EncounterManager.I.GetEncounterMax();
            int cost = EncounterManager.I.GetEncounterCost();

            bool has = cur >= cost;
            energyLabel.text = $"{cur} / {max}";
            energyLabel.color = has ? Color.white : NotEnoughColor;
            return;
        }

        // Fallback: show stored energy even if EncounterManager isn't initialized yet
        int stored = ResourceBank.Get(ResourceType.Energy);
        energyLabel.text = $"{stored}";
        energyLabel.color = Color.white;
    }
}
