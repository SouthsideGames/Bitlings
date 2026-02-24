using UnityEngine;


public sealed class IronBattleUIRoot : MonoBehaviour
{
    [Header("Iron HUD Root")]
    [Tooltip("Root GameObject for the Iron battle HUD (usually: Panel_IronCareerEncounter/IronCareerBattle).")]
    [SerializeField] private GameObject battleHudRoot;

    [Header("Iron HUD Components")]
    [Tooltip("BattleFeedbackManager wired to IronCareerBattle UI.")]
    [SerializeField] private BattleFeedbackManager ironFeedback;

    [Tooltip("Optional: BattleTextBoxUI under IronCareerBattle (if you duplicated it).")]
    [SerializeField] private BattleTextBoxUI ironBattleTextBox;

    [Tooltip("Optional: Bottom toggle under IronCareerBattle (if you duplicated it).")]
    [SerializeField] private BattleSwitchToggle ironBottomToggle;

    [Header("Auto-find (optional)")]
    [SerializeField] private bool autoFindInChildren = true;

    private void Awake()
    {
        if (!autoFindInChildren) return;

        if (!battleHudRoot) battleHudRoot = gameObject;

        if (!ironFeedback) ironFeedback = GetComponentInChildren<BattleFeedbackManager>(includeInactive: true);
        if (!ironBattleTextBox) ironBattleTextBox = GetComponentInChildren<BattleTextBoxUI>(includeInactive: true);
        if (!ironBottomToggle) ironBottomToggle = GetComponentInChildren<BattleSwitchToggle>(includeInactive: true);
    }

    public void ShowBattleHud()
    {
        if (battleHudRoot) battleHudRoot.SetActive(true);
    }

    public void HideBattleHud()
    {
        if (battleHudRoot) battleHudRoot.SetActive(false);
    }

    /// <summary>
    /// Routes BattleManager UI references to the Iron HUD.
    /// Safe to call multiple times.
    /// </summary>
    public void ApplyTo(BattleManager battle)
    {
        if (!battle) return;
        battle.SetUIOverride(ironFeedback, ironBattleTextBox, ironBottomToggle);
    }

    /// <summary>
    /// Restores BattleManager UI references back to what they were before Iron.
    /// </summary>
    public void RestoreBattleManagerDefaults()
    {
        var battle = FindFirstObjectByType<BattleManager>();
        if (!battle) return;
        battle.ClearUIOverride();
    }
}
