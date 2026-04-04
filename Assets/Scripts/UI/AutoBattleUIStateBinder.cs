using UnityEngine;

public class AutoBattleUIStateBinder : MonoBehaviour
{
    [Header("UI Roots")]
    [Tooltip("Root GameObject for the BattleSwitchToggle (hide during auto).")]
    [SerializeField] private GameObject battleSwitchToggleRoot;

    [Tooltip("Root GameObject for the battle speed button UI (show during auto).")]
    [SerializeField] private GameObject battleSpeedButtonRoot;

    void OnEnable()
    {
        GameEvents.AutoBattleModeChanged += HandleAutoModeChanged;

        // Initial sync (in case UI is enabled mid-state)
        bool isAuto = (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;
        Apply(isAuto);
    }

    void OnDisable()
    {
        GameEvents.AutoBattleModeChanged -= HandleAutoModeChanged;
    }

    private void HandleAutoModeChanged(bool isAuto)
    {
        Apply(isAuto);
    }

    private void Apply(bool isAuto)
    {
        if (battleSwitchToggleRoot != null)
            battleSwitchToggleRoot.SetActive(!isAuto);

        if (battleSpeedButtonRoot != null)
            battleSpeedButtonRoot.SetActive(isAuto);
    }
}
