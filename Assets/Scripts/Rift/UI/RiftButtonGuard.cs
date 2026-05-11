using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public sealed class RiftButtonGuard : MonoBehaviour
    , IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Requirements")]
    [SerializeField, Min(1)] private int minRequiredTeamMembers = 1;

    [Header("Feedback")]
    [SerializeField] private RectTransform shakeTarget;
    [SerializeField, Range(0.05f, 0.5f)] private float shakeDuration = 0.2f;
    [SerializeField, Range(1f, 30f)] private float shakeMagnitude = 10f;

    [Header("Hold Action")]
    [Tooltip("How long the player must hold to toggle auto-battle. Shown as a radial fill countdown.")]
    [SerializeField, Min(0.1f)] private float holdToStopIdleSeconds = 3f; // CHANGED: 3-second hold with visible countdown

    [Tooltip("Parent container for the hold progress fill. Shown when hold begins, hidden when hold ends.")]
    [SerializeField] private GameObject autoFillContainer;

    [Tooltip("Radial Image (fillAmount 0-1) that fills while the player holds. Assign in Inspector. Use Image Type = Filled, Fill Method = Radial 360.")]
    [SerializeField] private UnityEngine.UI.Image holdProgressFill; // CHANGED: countdown ring fill

    [Header("Auto-Battle Home Screen UI")]
    [Tooltip("A ring/glow image around the Rift button that pulses while auto is running. Assign in Inspector.")]
    [SerializeField] private GameObject autoPulseRingRoot;

    [Tooltip("Label showing 'Xw - Y⚡' (wins - energy) while auto is running. Assign in Inspector.")]
    [SerializeField] private TMPro.TextMeshProUGUI autoStatusLabel;

    [Tooltip("Overlay panel shown when player taps the Rift button while auto is running. Assign in Inspector.")]
    [SerializeField] private GameObject autoRunningOverlay;

    [Tooltip("How often (seconds) the live counter refreshes while auto is running.")]
    [SerializeField, Min(0.5f)] private float autoCounterRefreshSeconds = 2f;

    private Coroutine _autoCounterCo; // CHANGED: new fields for home-screen auto-battle UX

    private Button _button;
    private Coroutine _shakeRoutine;
    private Coroutine _deferredApply;
    private bool _pressed;
    private bool _holdTriggered;
    private bool _suppressNextClick;
    private float _pressedAt;

    // RiftManager can be created/destroyed across scenes.
    // Hook its OnStateChanged so the button refreshes when battles start/end.
    private RiftManager _hookedRift;

    private void Awake()
    {
        _button = GetComponent<Button>();
        if (!shakeTarget) shakeTarget = GetComponent<RectTransform>();

        // Best-effort early Apply; we also do a deferred Apply to avoid boot-order brittleness.
        Apply();
    }

    private void Start()
    {
        // In case RiftManager comes up after this UI.
        TryHookRiftEvents();

        // Boot-order guard: on some scenes Save/Rift systems may initialize
        // after this button UI. Do one delayed refresh so it never gets stuck.
        if (_deferredApply != null) StopCoroutine(_deferredApply);
        _deferredApply = StartCoroutine(Co_DeferredApply());
    }

    private void OnEnable()
    {
        // Ensure hold UI never appears in a stale state when this view is enabled.
        ResetPressState();

        RiftManager.OnEnergyGained += HandleEnergy;
        GameEvents.EnergyChanged += HandleEnergy;
        GameEvents.OnTeamChanged += HandleTeamChanged;
        GameEvents.OnTeamHealthChanged += HandleTeamChanged;
        GameEvents.AutoBattleModeChanged += HandleAutoModeChanged;
        GameEvents.BattleFinished += HandleBattleFinishedForCounter; // CHANGED: refresh counter on each battle finish

        _button.onClick.AddListener(OnButtonClicked);

        TryHookRiftEvents();

        // Immediate + deferred refresh.
        Apply();

        // CHANGED: sync pulse ring and counter on enable.
        bool autoNow = (RiftManager.I != null && RiftManager.I.IsAutoMode) || IsIdleAutoRunning();
        if (autoPulseRingRoot != null) autoPulseRingRoot.SetActive(autoNow);
        if (autoRunningOverlay != null) autoRunningOverlay.SetActive(autoNow); // CHANGED: sync overlay on enable
        if (autoNow) StartAutoCounter(); else StopAutoCounter();

        if (_deferredApply != null) StopCoroutine(_deferredApply);
        _deferredApply = StartCoroutine(Co_DeferredApply());
    }

    private void OnDisable()
    {
        RiftManager.OnEnergyGained -= HandleEnergy;
        GameEvents.EnergyChanged -= HandleEnergy;
        GameEvents.OnTeamChanged -= HandleTeamChanged;
        GameEvents.OnTeamHealthChanged -= HandleTeamChanged;
        GameEvents.AutoBattleModeChanged -= HandleAutoModeChanged;
        GameEvents.BattleFinished -= HandleBattleFinishedForCounter; // CHANGED
        StopAutoCounter(); // CHANGED

        UnhookRiftEvents();

        if (_deferredApply != null)
        {
            StopCoroutine(_deferredApply);
            _deferredApply = null;
        }

        _button.onClick.RemoveListener(OnButtonClicked);
        if (autoRunningOverlay != null)
            autoRunningOverlay.SetActive(false);
        ResetPressState();
    }

    private void Update()
    {
        // Cheap guard: if RiftManager instance changes across scenes, re-hook.
        // This prevents the button getting stuck disabled after a battle when no
        // energy/team events fire.
        if (_hookedRift != RiftManager.I)
            TryHookRiftEvents();

        // CHANGED: 3-second hold countdown - matches HoldToPurchaseButton pattern
        if (!_pressed || _holdTriggered) return;
        if (!IsIdleBattleUnlocked())
        {
            if (holdProgressFill != null)
                holdProgressFill.fillAmount = 0f; // CHANGED: keep fill hidden for locked players
            return; // CHANGED: holding does nothing until idle battles are unlocked
        }

        float heldFor = Time.unscaledTime - _pressedAt;
        float t = Mathf.Clamp01(heldFor / holdToStopIdleSeconds);

        const float CONTAINER_SHOW_DELAY = 0.5f;
        if (autoFillContainer != null)
            autoFillContainer.SetActive(heldFor >= CONTAINER_SHOW_DELAY);

        // CHANGED: only show fill after a short dead zone so normal taps never see a flash
        if (holdProgressFill != null)
        {
            const float FILL_DEAD_ZONE = 0.15f; // seconds before fill becomes visible
            float visibleT = heldFor < FILL_DEAD_ZONE
                ? 0f
                : Mathf.Clamp01((heldFor - FILL_DEAD_ZONE) / (holdToStopIdleSeconds - FILL_DEAD_ZONE));
            holdProgressFill.fillAmount = visibleT;
        }

        if (heldFor >= holdToStopIdleSeconds)
        {
            _holdTriggered = true;
            _pressed = false;

            // Fill complete - snap to full before action fires.
            if (holdProgressFill != null)
                holdProgressFill.fillAmount = 1f;

            if (TryStopIdleAutoAndShowRewards())
            {
                _suppressNextClick = true;
                Apply();
            }
        }
    }

    private void TryHookRiftEvents()
    {
        var em = RiftManager.I;
        if (em == null)
        {
            UnhookRiftEvents();
            return;
        }

        if (_hookedRift == em) return;

        UnhookRiftEvents();
        _hookedRift = em;
        _hookedRift.OnStateChanged += Apply;
    }

    private void UnhookRiftEvents()
    {
        if (_hookedRift != null)
        {
            _hookedRift.OnStateChanged -= Apply;
            _hookedRift = null;
        }
    }

    private void HandleEnergy(int a, int b) => Apply();
    private void HandleEnergy() => Apply();
    private void HandleTeamChanged() => Apply();
    private void HandleAutoModeChanged(bool isAuto) // CHANGED: driven by both RiftManager.ToggleAutoMode() and IdleBattleManager.EnableAuto/DisableAuto
    {
        Apply();

        if (autoPulseRingRoot != null)
            autoPulseRingRoot.SetActive(isAuto);

        if (autoRunningOverlay != null)
            autoRunningOverlay.SetActive(isAuto); // CHANGED: show overlay when auto starts, hide when it stops

        if (isAuto)
            StartAutoCounter();
        else
            StopAutoCounter();
    }

    private void Apply()
    {
        if (_button == null) return;

        bool idleAutoRunning = IsIdleAutoRunning();
        bool autoRunning = idleAutoRunning || (RiftManager.I != null && RiftManager.I.IsAutoMode);
        // CHANGED: removed - overlay is driven by HandleAutoModeChanged and OnEnable sync

        bool hasIdleTeam = HasIdleTeamConfigured();
        string reason = null;
        bool ok;

        if (idleAutoRunning)
        {
            ok = false;
            reason = "Idle auto-battle running.";
        }
        else
        {
            bool canStartRift = EligibilityRules.CanStartRift(minRequiredTeamMembers, out reason);
            ok = canStartRift || hasIdleTeam;
            if (ok && hasIdleTeam)
                reason = null;
            else if (!ok && !hasIdleTeam && reason == "No healthy team.")
                reason = "No active or idle team.";
        }

        _button.interactable = ok;

        // Helpful diagnostics when something unexpected disables the button.
        if (!ok && !string.IsNullOrEmpty(reason))
            DevLog.Log($"[RiftButtonGuard] Rift disabled: {reason}", this);
    }

    private IEnumerator Co_DeferredApply()
    {
        yield return new WaitForEndOfFrame();
        Apply();
        _deferredApply = null;
    }

    private void OnButtonClicked()
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        // CHANGED: tap while auto is running shows overlay instead of opening battle scene.
        if (IsIdleAutoRunning() || (RiftManager.I != null && RiftManager.I.IsAutoMode))
        {
            if (autoRunningOverlay != null)
                autoRunningOverlay.SetActive(true);
            else
                GameEvents.RaiseToast("Auto-battle is running. Hold the button to stop.");
            return;
        }

        if (!EligibilityRules.CanStartRift(minRequiredTeamMembers, out _))
        {
            StartShake();
            return;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pressed = true;
        _holdTriggered = false;
        _pressedAt = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResetPressState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ResetPressState();
    }

    private void ResetPressState()
    {
        _pressed = false;
        _holdTriggered = false;
        _pressedAt = 0f;
        if (holdProgressFill != null)
            holdProgressFill.fillAmount = 0f; // CHANGED: clear countdown ring when hold is cancelled
        if (autoFillContainer != null) autoFillContainer.SetActive(false);
    }

    private static bool IsIdleAutoRunning()
    {
        try
        {
            var s = IdleBattleStore.Load();
            return s != null && s.autoBattling;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsIdleBattleUnlocked() // CHANGED: mirrors AutoBattleButton.IsIdleBattleUnlocked
    {
        try { return FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic); }
        catch { return false; }
    }

    private static bool HasIdleTeamConfigured()
    {
        try
        {
            return !IdleLoadoutManager.IsIdleTeamEmpty();
        }
        catch
        {
            return false;
        }
    }

    private bool TryStopIdleAutoAndShowRewards()
    {
        bool autoRunning = IsIdleAutoRunning() || (RiftManager.I != null && RiftManager.I.IsAutoMode);

        if (autoRunning)
        {
            // CHANGED: auto is ON, so toggle it OFF
            bool stoppedAnyAuto = false;

            // Stop foreground rift AUTO loop first.
            var em = RiftManager.I;
            if (em != null && em.IsAutoMode)
            {
                // CHANGED: ToggleAutoMode() exits AutoLoop after the current in-progress battle finishes.
                em.ToggleAutoMode();
                stoppedAnyAuto = true;
            }

            // Stop persisted idle-auto session flag.
            if (IsIdleAutoRunning())
            {
                IdleBattleManager.I?.DisableAuto();
                stoppedAnyAuto = true;

                // Safety net in case IdleBattleManager singleton is not available in this scene.
                try
                {
                    var s = IdleBattleStore.Load();
                    if (s != null && s.autoBattling)
                    {
                        s.autoBattling = false;
                        IdleBattleStore.Save(s);
                    }
                }
                catch { }
            }

            if (stoppedAnyAuto)
            {
                IdleBattleManager.I?.TryOpenSummaryIfNeeded();
            }

            return stoppedAnyAuto;
        }
        else
        {
            // CHANGED: auto is OFF, so toggle it ON (start auto from home screen)
            IdleBattleManager.I?.EnableAuto(); // CHANGED: EnableAuto() now fires RaiseAutoBattleModeChanged(true) internally - do not fire it again here
            return true;
        }
    }

    private void HandleBattleFinishedForCounter(BattleResult _) => RefreshAutoCounter(); // CHANGED

    private void RefreshAutoCounter() // CHANGED: shows wins and losses instead of wins and energy
    {
        if (autoStatusLabel == null) return;

        bool running = IsIdleAutoRunning() || (RiftManager.I != null && RiftManager.I.IsAutoMode);
        if (!running)
        {
            autoStatusLabel.gameObject.SetActive(false);
            return;
        }

        var s = IdleBattleStore.Load();

        int wins = 0;
        if (s?.log != null)
            foreach (var e in s.log) wins += e.count;

        int losses = s?.totalLosses ?? 0; // CHANGED: read tracked loss count from session

        autoStatusLabel.text = $"{wins}W · {losses}L";
        autoStatusLabel.gameObject.SetActive(true);
    }

    private void StartAutoCounter() // CHANGED: starts periodic refresh
    {
        StopAutoCounter();
        _autoCounterCo = StartCoroutine(Co_AutoCounter());
    }

    private void StopAutoCounter() // CHANGED
    {
        if (_autoCounterCo != null)
        {
            StopCoroutine(_autoCounterCo);
            _autoCounterCo = null;
        }
        if (autoStatusLabel != null)
            autoStatusLabel.gameObject.SetActive(false);
    }

    private IEnumerator Co_AutoCounter() // CHANGED: periodic live-counter refresh
    {
        while (true)
        {
            RefreshAutoCounter();
            yield return new WaitForSeconds(autoCounterRefreshSeconds);
        }
    }

    private void StartShake()
    {
        if (!shakeTarget) return;

        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);

        _shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        Vector3 originalPos = shakeTarget.localPosition;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float offsetX = Random.Range(-1f, 1f) * shakeMagnitude;
            float offsetY = Random.Range(-1f, 1f) * shakeMagnitude;

            shakeTarget.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        shakeTarget.localPosition = originalPos;
        _shakeRoutine = null;
    }
}
