using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleSpeedToggleUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button speedButton;
    [SerializeField] private TextMeshProUGUI speedLabel;

    [Header("Bindings")]
    [SerializeField] private BattleManager battleManager;

    [Header("Behavior")]
    [Tooltip("If enabled, speed cannot be changed during auto-battle.")]
    [SerializeField] private bool disableDuringAuto = false;

    private bool _autoMode;

    void Awake()
    {
        if (speedButton != null)
            speedButton.onClick.AddListener(OnSpeedClicked);

        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        RefreshLabel();
    }

    void OnEnable()
    {
        GameEvents.OnEncounterAutoModeChanged += HandleAutoModeChanged;
        HandleAutoModeChanged(); // sync initial
    }

    void OnDisable()
    {
        GameEvents.OnEncounterAutoModeChanged -= HandleAutoModeChanged;
    }

    private void HandleAutoModeChanged()
    {
        bool isAuto = (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;
        SetAutoMode(isAuto);
    }

    public void SetAutoMode(bool isAuto)
    {
        _autoMode = isAuto;

        if (speedButton != null)
            speedButton.interactable = !(disableDuringAuto && _autoMode);

        RefreshLabel();
    }

    private void OnSpeedClicked()
    {
        if (battleManager == null) return;
        if (disableDuringAuto && _autoMode) return;

        battleManager.CycleBattleSpeed();
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (speedLabel == null || battleManager == null) return;

        float s = battleManager.BattleSpeed;

        if (Mathf.Abs(s - 1f) < 0.01f) speedLabel.text = "1x";
        else if (Mathf.Abs(s - 2f) < 0.01f) speedLabel.text = "2x";
        else if (Mathf.Abs(s - 3f) < 0.01f) speedLabel.text = "3x";
        else speedLabel.text = $"{s:0.##}x";
    }
}
