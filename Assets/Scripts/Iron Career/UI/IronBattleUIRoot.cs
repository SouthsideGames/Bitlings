using UnityEngine;

/// <summary>
/// Routes Iron Career battle UI bindings into BattleManager.
/// Iron mode does not support party switching.
/// Only handles binding overrides.
/// </summary>
public class IronBattleUIRoot : MonoBehaviour
{
    [Header("Iron UI Bindings")]
    [SerializeField] private BattleManager.BattleUIBindings ironBindings;

    [Header("Optional Iron Components (auto-find if null)")]
    [SerializeField] private BattleFeedbackManager ironFeedback;
    [SerializeField] private BattleTextBoxUI ironBattleTextBox;

    [SerializeField] private bool autoFindInChildren = true;

    private void Awake()
    {
        if (!autoFindInChildren) return;

        if (!ironFeedback)
            ironFeedback = GetComponentInChildren<BattleFeedbackManager>(true);

        if (!ironBattleTextBox)
            ironBattleTextBox = GetComponentInChildren<BattleTextBoxUI>(true);
    }

    public void ApplyTo(BattleManager battle)
    {
        if (!battle) return;

        battle.SetUIBindingsOverride(ironBindings);

        // No bottom toggle override — Iron does not allow switching
        battle.SetUIOverride(
            ironFeedback,
            ironBattleTextBox,
            null
        );
    }

    public void RestoreBattleManagerDefaults()
    {
        var battle = FindFirstObjectByType<BattleManager>();
        if (!battle) return;

        battle.ClearUIBindingsOverride();
        battle.ClearUIOverride();
    }
}