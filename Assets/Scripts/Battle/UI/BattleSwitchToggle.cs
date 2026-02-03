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
    [SerializeField] private GameObject battleTextIcon;
    [SerializeField] private GameObject boosterBarIcon; 

    [Header("Rules")]
    [SerializeField] private bool startShowingText = true;

    [Header("Battle State Source")]
    [SerializeField] private BattleManager battle;

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
            SetGroupVisible(battleTextGroup, true);
            SetGroupVisible(boosterBarGroup, false);

            SetIconState(showBattleTextIcon: false);
        }
        else
        {
            SetGroupVisible(battleTextGroup, false);
            SetGroupVisible(boosterBarGroup, true);

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

    public void ForceShowText()
    {
        _showingText = true;
        ApplyState(immediate: true);
    }
}