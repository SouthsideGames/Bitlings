using UnityEngine;
using UnityEngine.UI;
using TMPro;

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


    [Header("Queued UI (Optional)")]
    [Tooltip("Optional label that will show the currently queued action (e.g., 'Queued: Attack').")]
    [SerializeField] private TMP_Text queuedText;

    [Tooltip("If set, selected/queued action button gets a subtle highlight tint.")]
    [SerializeField] private Color queuedHighlightTint = new Color(1f, 1f, 1f, 1f);

    [SerializeField] private bool disableButtonsOnceQueued = true;

    private ColorBlock _attackBaseColors;
    private ColorBlock _defendBaseColors;
    private ColorBlock _focusBaseColors;
    private ColorBlock _runBaseColors;
    private bool _cachedBaseColors;

    [Header("UX")]
    [Tooltip("If true, buttons auto-disable when it isn't the player's turn.")]
    [SerializeField] private bool autoDisableWhenNotPlayerTurn = true;

    [Tooltip("If true, buttons also auto-disable when Rift auto-mode is on (RiftManager.I.IsAutoMode).")]
    [SerializeField] private bool alsoDisableDuringRiftAutoMode = true;

    [Tooltip("If true, we re-wire button listeners in OnEnable (safer when refs are assigned late).")]
    [SerializeField] private bool rewireOnEnable = true;

    private bool _hasLast;
    private bool _lastEnable;

    void Reset()
    {
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
        EnsureRefs();
        CacheBaseColors();
        WireButtons();
    }

    void OnEnable()
    {
        EnsureRefs();
        CacheBaseColors();

        GameEvents.OnBattleStateChanged += Refresh;
        GameEvents.OnRiftAutoModeChanged += Refresh;

        if (rewireOnEnable)
            WireButtons();

        Refresh();
    }

    void OnDisable()
    {
        GameEvents.OnBattleStateChanged -= Refresh;
        GameEvents.OnRiftAutoModeChanged -= Refresh;
    }

    private void EnsureRefs()
    {
        if (!battle || !battle.isActiveAndEnabled)
        {
            battle = GetComponentInParent<BattleManager>();
            if (!battle) battle = FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);
        }

        if (!feedback)
        {
            feedback = GetComponentInParent<BattleFeedbackManager>();
            if (!feedback) feedback = FindFirstObjectByType<BattleFeedbackManager>(FindObjectsInactive.Include);
        }
    }

    public void BindTo(BattleManager targetBattle, BattleFeedbackManager targetFeedback = null)
    {
        battle = targetBattle;
        if (targetFeedback)
            feedback = targetFeedback;

        _hasLast = false;

        if (rewireOnEnable || isActiveAndEnabled)
            WireButtons();

        if (isActiveAndEnabled)
            Refresh();
    }


    private void CacheBaseColors()
    {
        if (_cachedBaseColors) return;
        _cachedBaseColors = true;

        if (attackBtn) _attackBaseColors = attackBtn.colors;
        if (defendBtn) _defendBaseColors = defendBtn.colors;
        if (focusBtn)  _focusBaseColors  = focusBtn.colors;
        if (runBtn)    _runBaseColors    = runBtn.colors;
    }

    private void ClearQueuedHighlights()
    {
        if (attackBtn) attackBtn.colors = _attackBaseColors;
        if (defendBtn) defendBtn.colors = _defendBaseColors;
        if (focusBtn)  focusBtn.colors  = _focusBaseColors;
        if (runBtn)    runBtn.colors    = _runBaseColors;
    }

    private void ApplyQueuedHighlight(int actionCode)
    {
        // 0=None, 1=Attack, 2=Defend, 3=Focus, 4=Swap, 5=Run
        ClearQueuedHighlights();

        if (actionCode == 0) return;

        Button b = null;
        ColorBlock baseCb = default;

        switch (actionCode)
        {
            case 1: b = attackBtn; baseCb = _attackBaseColors; break;
            case 2: b = defendBtn; baseCb = _defendBaseColors; break;
            case 3: b = focusBtn;  baseCb = _focusBaseColors;  break;
            case 5: b = runBtn;    baseCb = _runBaseColors;    break;
            default: return; // Swap highlight is handled elsewhere (bench UI)
        }

        if (!b) return;

        // Subtle highlight: brighten normal/highlighted states only.
        var cb = baseCb;
        cb.normalColor = queuedHighlightTint;
        cb.highlightedColor = queuedHighlightTint;
        b.colors = cb;
    }

    private void WireButtons()
    {

        if (attackBtn)
        {
            attackBtn.onClick.RemoveAllListeners();
            attackBtn.onClick.AddListener(() =>
            {
                EnsureRefs();

                // Immediate tactile feedback
                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Attack);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Attack);
                }

                if (battle) battle.SetPlayerActionAttack();
                AudioManager.I?.PlaySfx(SfxType.Attack);

                // Action selection may lock input immediately
                Refresh();
            });
        }

        if (defendBtn)
        {
            defendBtn.onClick.RemoveAllListeners();
            defendBtn.onClick.AddListener(() =>
            {
                EnsureRefs();

                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Defend);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Defend);
                }

                if (battle) battle.SetPlayerActionDefend();
                AudioManager.I?.PlaySfx(SfxType.Defend);
                Refresh();
            });
        }

        if (focusBtn)
        {
            focusBtn.onClick.RemoveAllListeners();
            focusBtn.onClick.AddListener(() =>
            {
                EnsureRefs();

                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Focus);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Focus);
                }

                if (battle) battle.SetPlayerActionFocus();
                AudioManager.I?.PlaySfx(SfxType.Focus);
                Refresh();
            });
        }

        if (runBtn)
        {
            runBtn.onClick.RemoveAllListeners();
            runBtn.onClick.AddListener(() =>
            {
                EnsureRefs();

                if (feedback)
                {
                    feedback.PlayButtonPress(BattleFeedbackManager.BattleFeedbackAction.Run);
                    feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, BattleFeedbackManager.BattleFeedbackAction.Run);
                }

                if (battle) battle.SetPlayerActionRun();
                AudioManager.I?.PlaySfx(SfxType.Run);
                Refresh();
            });
        }
    }

    private void Refresh()
    {
        EnsureRefs();

        if (!autoDisableWhenNotPlayerTurn)
        {
            ApplyInteractable(true);
            return;
        }

        bool enable = ComputeEnable();

        // Queued action UI (instant feedback)
        int queuedCode = (battle != null) ? battle.QueuedPlayerActionCode : 0;
        bool hasQueued = (battle != null) && battle.HasQueuedPlayerAction;

        if (queuedText)
        {
            queuedText.gameObject.SetActive(hasQueued);
            if (hasQueued)
                queuedText.text = queuedCode == 0 ? "Queued" : $"Queued: {ActionCodeToLabel(queuedCode)}";
        }

        ApplyQueuedHighlight(queuedCode);

        // Optional: disable inputs once queued to prevent 'dead clicks'
        if (disableButtonsOnceQueued && hasQueued)
            enable = false;

        // Apply only if changed
        if (_hasLast && enable == _lastEnable)
            return;

        _hasLast = true;
        _lastEnable = enable;

        ApplyInteractable(enable);

        // Per-action gating (statuses). We still keep a global enable/disable, but some
        // statuses restrict specific actions while allowing others.
        if (enable && battle != null)
        {
            bool sundered = false;
            bool wyrmFury = false;

            try { sundered = battle.IsActivePlayerSundered(); } catch { /* ignore if API differs */ }
            try { wyrmFury = battle.IsActivePlayerWyrmFury(); } catch { /* ignore if API differs */ }

            if (sundered)
            {
                if (defendBtn) defendBtn.interactable = false;
                if (runBtn) runBtn.interactable = false;
            }

            if (wyrmFury)
            {
                if (focusBtn) focusBtn.interactable = false;
            }
        }
    }

    private bool ComputeEnable()
    {
        EnsureRefs();

        if (battle == null || !battle.isActiveAndEnabled)
            return false;

        bool enable = battle.IsPlayerTurn;

        // Hard-gate input when the active player is Frozen (status system).
        if (enable)
        {
            try
            {
                if (battle != null && battle.IsActivePlayerFrozen())
                    enable = false;
            }
            catch
            {
                // ignore if API differs
            }
        }

        // Optionally also gate by Rift auto-mode if present
        if (enable && alsoDisableDuringRiftAutoMode)
        {
            bool isAuto = false;
            try
            {
                isAuto = !ExecutiveTrialRuntime.IsActive && (RiftManager.I != null) && RiftManager.I.IsAutoMode;
            }
            catch
            {
                // ignore if the API differs
            }

            if (isAuto) enable = false;
        }

        return enable;
    }


    private static string ActionCodeToLabel(int code)
    {
        switch (code)
        {
            case 1: return "Attack";
            case 2: return "Defend";
            case 3: return "Focus";
            case 4: return "Swap";
            case 5: return "Run";
            default: return "Action";
        }
    }

    
    public void SetInteractable(bool on)
    {
        _hasLast = false;
        _lastEnable = on;
        ApplyInteractable(on);
    }

    private void ApplyInteractable(bool enable)
    {
        if (attackBtn && attackBtn.interactable != enable) attackBtn.interactable = enable;
        if (defendBtn && defendBtn.interactable != enable) defendBtn.interactable = enable;
        if (focusBtn  && focusBtn.interactable  != enable) focusBtn.interactable  = enable;
        if (runBtn    && runBtn.interactable    != enable) runBtn.interactable    = enable;
    }
}
