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

        // Prefer RiftPanelUI/RiftManager when available (they know max/cost)
        if (RiftPanelUI.I != null)
        {
            int cur = RiftPanelUI.I.GetEnergyPoints();
            int max = RiftPanelUI.I.GetRiftMax();
            int cost = RiftPanelUI.I.GetRiftCost();

            bool has = cur >= cost;
            energyLabel.text = $"{cur} / {max}";
            energyLabel.color = has ? Color.white : NotEnoughColor;
            return;
        }

        if (RiftManager.I != null)
        {
            int cur = RiftManager.I.GetEnergyPoints();
            int max = RiftManager.I.GetRiftMax();
            int cost = RiftManager.I.GetRiftCost();

            bool has = cur >= cost;
            energyLabel.text = $"{cur} / {max}";
            energyLabel.color = has ? Color.white : NotEnoughColor;
            return;
        }

        // Fallback: show stored energy even if RiftManager isn't initialized yet
        int stored = ResourceBank.Get(ResourceType.Energy);
        energyLabel.text = $"{stored}";
        energyLabel.color = Color.white;
    }
}
