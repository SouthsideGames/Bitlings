using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleSwitchToggle : MonoBehaviour
{
    public enum BattleMode
    {
        Text = 0,
        Switch = 1
    }

    [Header("UI")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private TextMeshProUGUI modeLabel;

    [Header("Optional")]
    [Tooltip("If present, we can also update battle speed UI state when auto mode changes.")]
    [SerializeField] private BattleSpeedToggleUI battleSpeedToggleUI;

    [Header("State")]
    [SerializeField] private BattleMode startMode = BattleMode.Text;

    private BattleMode _mode;
    private bool _autoLocked;

    public BattleMode Mode => _mode;

    void Awake()
    {
        _mode = startMode;

        if (toggleButton != null)
            toggleButton.onClick.AddListener(OnToggleClicked);

        RefreshVisuals();
    }

    void OnEnable()
    {
        GameEvents.OnEncounterAutoModeChanged += HandleAutoModeChanged;
        RefreshVisuals();
    }

    void OnDisable()
    {
        GameEvents.OnEncounterAutoModeChanged -= HandleAutoModeChanged;
    }

    private void HandleAutoModeChanged()
    {
        bool isAuto = (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;
        SetAutoBattleMode(isAuto);
    }

    private void OnToggleClicked()
    {
        if (_autoLocked) return;

        _mode = (_mode == BattleMode.Text) ? BattleMode.Switch : BattleMode.Text;
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        if (modeLabel != null)
        {
            modeLabel.text = (_mode == BattleMode.Text) ? "TEXT" : "SWITCH";
        }

        if (toggleButton != null)
            toggleButton.interactable = !_autoLocked;

        if (battleSpeedToggleUI != null)
            battleSpeedToggleUI.SetAutoMode(_autoLocked);
    }


    public void SetAutoBattleMode(bool isAuto)
    {
        _autoLocked = isAuto;

        if (_autoLocked)
            _mode = BattleMode.Text;

        RefreshVisuals();
    }

    // Convenience for other systems
    public void SetMode(BattleMode mode)
    {
        if (_autoLocked) return;
        _mode = mode;
        RefreshVisuals();
    }
}
