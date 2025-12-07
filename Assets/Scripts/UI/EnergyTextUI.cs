using UnityEngine;
using TMPro;

public class EnergyTextUI : MonoBehaviour
{  
    [SerializeField] private TextMeshProUGUI energyLabel;

    private void Awake()
    {
        if (!energyLabel)
            energyLabel = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        GameEvents.EnergyChanged += Refresh;
        GameEvents.OnResourcesChanged += Refresh; // safety only
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

        // If EncounterManager isn't ready, show nothing
        if (EncounterManager.I == null)
        {
            energyLabel.text = "0 / 0";
            energyLabel.color = Color.white;
            return;
        }

        int cur = EncounterPanelUI.I != null ? 
            EncounterPanelUI.I.GetEnergyPoints() : 
            EncounterManager.I.GetEnergyPoints();

        int max = EncounterPanelUI.I != null ?
            EncounterPanelUI.I.GetEncounterMax() :
            EncounterManager.I.GetEncounterMax();

        int cost = EncounterPanelUI.I != null ?
            EncounterPanelUI.I.GetEncounterCost() :
            EncounterManager.I.GetEncounterCost();

        bool has = cur >= cost;

        energyLabel.text = $"{cur} / {max}";
        energyLabel.color = has ? Color.white : new Color(1f, 0.5f, 0.5f);
    }
}
