// Assets/Scripts/UI/EncounterButtonGuard.cs
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class EncounterButtonGuard : MonoBehaviour
{
    [Header("Requirements")]
    [Tooltip("Minimum number of monsters required on the team to start an encounter.")]
    [SerializeField, Min(1)] private int minRequiredTeamMembers = 1;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        Apply();
    }

    private void OnEnable()
    {
        // Energy signals
        EncounterManager.OnEnergyGained += HandleEnergyGained;
        GameEvents.EnergyChanged += HandleEnergyChanged;

        // Team changes (e.g., captures, heals, benching, defeats)
        GameEvents.OnTeamChanged += HandleTeamChanged;

        // Initial pass
        Apply();
    }

    private void OnDisable()
    {
        EncounterManager.OnEnergyGained -= HandleEnergyGained;
        GameEvents.EnergyChanged -= HandleEnergyChanged;
        GameEvents.OnTeamChanged -= HandleTeamChanged;
    }

    private void HandleEnergyGained(int gained, int newTotal)
    {
        // Only gained events fire here—still just re-apply gate
        Apply();
    }

    private void HandleEnergyChanged()
    {
        Apply();
    }

    private void HandleTeamChanged()
    {
        Apply();
    }

    private void Apply()
    {
        if (_button == null) return;

        bool hasValidTeam = HasMinimumTeam(minRequiredTeamMembers);
        bool hasEnoughEnergy = HasRequiredEnergy();

        _button.interactable = hasValidTeam && hasEnoughEnergy;

    }

    private static bool HasMinimumTeam(int minMembers)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null) return false;

        int count = 0;
        for (int i = 0; i < data.team.Count; i++)
        {
            var entry = data.team[i];
            if (entry != null && !string.IsNullOrEmpty(entry.monsterId))
            {
                count++;
                if (count >= minMembers) return true;
            }
        }
        return false;
    }

    private static bool HasRequiredEnergy()
    {
        int needed = 1;
        int current = 0;

        if (EncounterManager.I != null)
        {
            needed = Mathf.Max(1, EncounterManager.I.GetEncounterCost());
            current = Mathf.Max(0, EncounterManager.I.GetEnergyPoints());
        }
        else
        {
            needed = 1; // conservative default
            current = Mathf.Max(0, ResourceBank.Get(ResourceType.Energy));
        }

        return current >= needed;
    }
}
