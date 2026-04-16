
using UnityEngine;
using UnityEngine.UI;

public class BattleSwitchToggle : MonoBehaviour
{
    [Header("Panels (CanvasGroup preferred)")]
    [SerializeField] private CanvasGroup battleTextGroup;
    [SerializeField] private CanvasGroup boosterBarGroup;

    [Header("Toggle Button")]
    [SerializeField] private Button toggleButton;

    [Header("Toggle Icons (show the OTHER mode)")]
    [SerializeField] private GameObject battleTextIcon; // icon representing battle text
    [SerializeField] private GameObject boosterBarIcon; // icon representing boost bar

    [Header("Rules")]
    [SerializeField] private bool startShowingText = true;

    [Header("Battle State Source")]
    [SerializeField] private BattleManager battle;

    [Header("Auto Mode / Visibility")]
    [Tooltip("If true, this entire toggle control hides during AUTO battles.")]
    [SerializeField] private bool hideDuringAutoBattle = true;

    private bool _showingText;

    void Awake()
    {
        _showingText = startShowingText;
        ApplyState(immediate: true);

        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);
    }

    void OnDestroy()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(Toggle);
    }

    void OnEnable()
    {
        GameEvents.OnRiftAutoModeChanged += HandleAutoModeChanged;
        HandleAutoModeChanged();
    }

    void OnDisable()
    {
        GameEvents.OnRiftAutoModeChanged -= HandleAutoModeChanged;
    }

    public void Toggle()
    {
        if (battle != null && battle.NarrationLocked)
            return;

        _showingText = !_showingText;
        ApplyState(immediate: false);
    }

    private void ApplyState(bool immediate)
    {
        if (_showingText)
        {
            // Show battle text panel
            SetGroupVisible(battleTextGroup, true);
            SetGroupVisible(boosterBarGroup, false);

            // Toggle icon shows BOOSTS (what you'll switch to)
            SetIconState(showBattleTextIcon: false);
        }
        else
        {
            // Show boost bar panel
            SetGroupVisible(battleTextGroup, false);
            SetGroupVisible(boosterBarGroup, true);

            // Toggle icon shows BATTLE TEXT (what you'll switch to)
            SetIconState(showBattleTextIcon: true);
        }
    }

    private void SetIconState(bool showBattleTextIcon)
    {
        if (battleTextIcon != null)
            battleTextIcon.SetActive(showBattleTextIcon);

        if (boosterBarIcon != null)
            boosterBarIcon.SetActive(!showBattleTextIcon);
    }

    private static void SetGroupVisible(CanvasGroup cg, bool visible)
    {
        if (cg == null) return;
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }

    // Called by BattleManager to force narration visibility
    public void ForceShowText()
    {
        _showingText = true;
        ApplyState(immediate: true);
    }

    private void HandleAutoModeChanged()
    {
        if (!hideDuringAutoBattle) return;

        bool isAuto = (RiftManager.I != null) && RiftManager.I.IsAutoMode;

        // Hide this switch toggle during auto mode if desired
        if (gameObject.activeSelf == isAuto)
            gameObject.SetActive(!isAuto);
    }

}