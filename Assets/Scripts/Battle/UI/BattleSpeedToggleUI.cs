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

    // NEW: Feature gating
    [Header("Unlock Gating")]
    [SerializeField] private FeatureId speedControlFeatureId = FeatureId.IdleBattle_SpeedControl;

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
        GameEvents.OnRiftAutoModeChanged += HandleAutoModeChanged;


        GameEvents.FeatureUnlocked += HandleFeatureUnlocked;

        HandleAutoModeChanged(); 
        RefreshVisibility();       
    }

    void OnDisable()
    {
        GameEvents.OnRiftAutoModeChanged -= HandleAutoModeChanged;
        GameEvents.FeatureUnlocked -= HandleFeatureUnlocked;
    }

    private void HandleAutoModeChanged()
    {
        bool isAuto = (RiftManager.I != null) && RiftManager.I.IsAutoMode;
        SetAutoMode(isAuto);

        // NEW: auto mode impacts visibility too
        RefreshVisibility();
    }

    // NEW
    private void HandleFeatureUnlocked(FeatureId id)
    {
        if (id != speedControlFeatureId) return;
        RefreshVisibility();
    }

    // NEW: Self-gating method
    private void RefreshVisibility()
    {
        // Use the idle-battle store as the authoritative "are we actually idle battling" check.
        // RiftManager.IsAutoMode can be stale (e.g. still true when the rift panel
        // opens after idle battles ended), causing the button to flash before any battle starts.
        // IdleBattleStore.autoBattling is updated by IdleBattleManager before
        // OnRiftAutoModeChanged fires, so this read is always current.
        bool isIdleBattleRunning = IdleBattleStore.Load()?.autoBattling == true;

        bool unlocked =
            FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(speedControlFeatureId);

        bool shouldShow = unlocked && isIdleBattleRunning;

        if (gameObject.activeSelf != shouldShow)
            gameObject.SetActive(shouldShow);
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

        if (Mathf.Abs(s - 1f) < 0.01f) speedLabel.text = "1x";        else if (Mathf.Abs(s - 2f) < 0.01f) speedLabel.text = "2x";        else if (Mathf.Abs(s - 3f) < 0.01f) speedLabel.text = "3x";        else speedLabel.text = $"{s:0.##}x";
    }
}
