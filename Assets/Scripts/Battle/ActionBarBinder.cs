using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ActionBarBinder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BattleManager battle;
    [SerializeField] private BattleFeedbackManager feedback;
    [SerializeField] private Button attackBtn;
    [SerializeField] private Button defendBtn;
    [SerializeField] private Button focusBtn;
    [SerializeField] private Button runBtn;

    [Header("UX")]
    [Tooltip("If true, buttons auto-disable when it isn't the player's turn.")]
    [SerializeField] private bool autoDisableWhenNotPlayerTurn = true;

    [Tooltip("If true, buttons also auto-disable when Encounter auto-mode is on (EncounterManager.I.IsAutoMode).")]
    [SerializeField] private bool alsoDisableDuringEncounterAutoMode = true;

    [Tooltip("If true, we re-wire button listeners in OnEnable (safer when refs are assigned late).")]
    [SerializeField] private bool rewireOnEnable = true;

    private bool _hasLast;
    private bool _lastEnable;

    void Reset()
    {
        // Auto-grab buttons by name
        var btns = GetComponentsInChildren<Button>(true);
        foreach (var b in btns)
        {
            var n = b.name.ToLowerInvariant();
            if (n.Contains("attack")) attackBtn = b;
            else if (n.Contains("defend")) defendBtn = b;
            else if (n.Contains("focus")) focusBtn = b;
            else if (n.Contains("run")) runBtn = b;
        }
    }

    void Awake()
    {
        // Ensure listeners exist even if OnEnable doesn't rewire.
        WireButtons();
    }

    void OnEnable()
    {
        EnsureRefs();

        // Option A: semantic global battle/encounter state events
        GameEvents.OnBattleStateChanged += Refresh;
        GameEvents.OnEncounterAutoModeChanged += Refresh;

        if (rewireOnEnable)
            WireButtons();

        Refresh();
    }

    void OnDisable()
    {
        GameEvents.OnBattleStateChanged -= Refresh;
        GameEvents.OnEncounterAutoModeChanged -= Refresh;
    }

    private void EnsureRefs()
    {
        if (!battle)
        {
            battle = GetComponentInParent<BattleManager>();
            if (!battle) battle = FindFirstObjectByType<BattleManager>();
        }

        if (!feedback)
        {
            // Prefer the feedback manager near the battle UI, but fall back to a scene search.
            feedback = GetComponentInParent<BattleFeedbackManager>();
            if (!feedback) feedback = FindFirstObjectByType<BattleFeedbackManager>();
        }
    }

    private void WireButtons()
    {
        // We intentionally remove listeners and re-add, because:
        // - this binder may be enabled/disabled multiple times
        // - scene reloads can cause duplicate bindings if we don't clear

        if (attackBtn)
        {
            attackBtn.onClick.RemoveAllListeners();
            attackBtn.onClick.AddListener(() =>
            {
                // Immediate tactile feedback
                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Attack);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Attack);
                }

                if (battle) battle.SetPlayerActionAttack();
                if (AudioManager.I != null) AudioManager.I.PlaySfx(SfxType.Attack);

                // Action selection may lock input immediately
                Refresh();
            });
        }

        if (defendBtn)
        {
            defendBtn.onClick.RemoveAllListeners();
            defendBtn.onClick.AddListener(() =>
            {
                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Defend);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Defend);
                }

                if (battle) battle.SetPlayerActionDefend();
                if (AudioManager.I != null) AudioManager.I.PlaySfx(SfxType.Defend);
                Refresh();
            });
        }

        if (focusBtn)
        {
            focusBtn.onClick.RemoveAllListeners();
            focusBtn.onClick.AddListener(() =>
            {
                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Focus);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Focus);
                }

                if (battle) battle.SetPlayerActionFocus();
                if (AudioManager.I != null) AudioManager.I.PlaySfx(SfxType.Focus);
                Refresh();
            });
        }

        if (runBtn)
        {
            runBtn.onClick.RemoveAllListeners();
            runBtn.onClick.AddListener(() =>
            {
                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Run);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Run);
                }

                if (battle) battle.SetPlayerActionRun();
                if (AudioManager.I != null) AudioManager.I.PlaySfx(SfxType.Run);
                Refresh();
            });
        }
    }

    private void Refresh()
    {
        if (!autoDisableWhenNotPlayerTurn)
        {
            ApplyInteractable(true);
            return;
        }

        bool enable = ComputeEnable();

        // Apply only if changed
        if (_hasLast && enable == _lastEnable)
            return;

        _hasLast = true;
        _lastEnable = enable;

        ApplyInteractable(enable);
    }

    private bool ComputeEnable()
    {
        if (battle == null || !battle.isActiveAndEnabled)
            return false;

        bool enable = battle.IsPlayerTurn;

        // Optionally also gate by Encounter auto-mode if present
        if (enable && alsoDisableDuringEncounterAutoMode)
        {
            bool isAuto = false;
            try
            {
                isAuto = (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;
            }
            catch
            {
                // ignore if the API differs
            }

            if (isAuto) enable = false;
        }

        return enable;
    }

    private void ApplyInteractable(bool enable)
    {
        if (attackBtn && attackBtn.interactable != enable) attackBtn.interactable = enable;
        if (defendBtn && defendBtn.interactable != enable) defendBtn.interactable = enable;
        if (focusBtn  && focusBtn.interactable  != enable) focusBtn.interactable  = enable;
        if (runBtn    && runBtn.interactable    != enable) runBtn.interactable    = enable;
    }
}
