// Assets/Scripts/Battle/UI/BattleSwitchToggle.cs
using UnityEngine;
using UnityEngine.UI;

public class BattleSwitchToggle : MonoBehaviour
{
    [Header("Toggle (Text <-> Speed)")]
    [SerializeField] private Toggle switchToggle;

    [Header("Panels")]
    [SerializeField] private GameObject textPanelRoot;
    [SerializeField] private GameObject speedPanelRoot;

    [Header("Optional - Speed UI Controller")]
    [SerializeField] private BattleSpeedToggleUI battleSpeedToggleUI;

    [Header("Auto Mode Behavior")]
    [Tooltip("When auto battle is on, force showing the Text panel (recommended).")]
    [SerializeField] private bool forceTextPanelDuringAuto = true;

    [Tooltip("When auto battle is on, disable the switch toggle so player can't swap panels.")]
    [SerializeField] private bool disableSwitchDuringAuto = true;

    [Tooltip("When auto battle is on, also disable the battle speed button.")]
    [SerializeField] private bool disableSpeedButtonDuringAuto = false;

    private bool _isAutoLocked;
    private bool _suppressCallback;

    private void Awake()
    {
        if (switchToggle != null)
            switchToggle.onValueChanged.AddListener(HandleToggleChanged);
    }

    private void OnEnable()
    {
        // In case something else changed state before we enabled.
        ApplyPanelState(GetToggleValueSafe());
    }

    private void OnDisable()
    {
        // Keep listener (no RemoveAllListeners to preserve inspector wiring),
        // but we do not need to unsubscribe because we added explicitly in Awake.
    }

    /// <summary>
    /// Called by BattleManager to lock/unlock the UI while auto battle resolves.
    /// </summary>
    public void SetAutoBattleMode(bool isAuto)
    {
        _isAutoLocked = isAuto;

        // If auto is on, we generally want the text panel visible.
        if (isAuto && forceTextPanelDuringAuto)
        {
            SetToggleValueSafe(false); // false => Text panel (convention)
        }

        // Disable/enable the switch itself.
        if (switchToggle != null && disableSwitchDuringAuto)
            switchToggle.interactable = !isAuto;

        // Optionally disable the speed button too.
        if (battleSpeedToggleUI != null && disableSpeedButtonDuringAuto)
            battleSpeedToggleUI.SetInteractable(!isAuto);

        // Ensure panels are correct after any forced toggle change.
        ApplyPanelState(GetToggleValueSafe());
    }

    private void HandleToggleChanged(bool isOn)
    {
        if (_suppressCallback) return;

        // If auto is locked, reject user input and snap back.
        if (_isAutoLocked && disableSwitchDuringAuto)
        {
            // Revert to the forced state (usually Text)
            _suppressCallback = true;
            switchToggle.isOn = false;
            _suppressCallback = false;

            ApplyPanelState(false);
            return;
        }

        ApplyPanelState(isOn);
    }

    private void ApplyPanelState(bool showSpeedPanel)
    {
        if (textPanelRoot != null)
            textPanelRoot.SetActive(!showSpeedPanel);

        if (speedPanelRoot != null)
            speedPanelRoot.SetActive(showSpeedPanel);

        // If speed panel is hidden, you may still want speed button usable elsewhere.
        // This script only controls visibility; interactability is controlled by SetAutoBattleMode if desired.
    }

    private bool GetToggleValueSafe()
    {
        return switchToggle != null && switchToggle.isOn;
    }

    private void SetToggleValueSafe(bool value)
    {
        if (switchToggle == null) return;

        _suppressCallback = true;
        switchToggle.isOn = value;
        _suppressCallback = false;
    }
}
