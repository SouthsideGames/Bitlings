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
    [SerializeField, Min(0.1f)] private float holdToStopIdleSeconds = 0.5f;

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
        RiftManager.OnEnergyGained += HandleEnergy;
        GameEvents.EnergyChanged += HandleEnergy;
        GameEvents.OnTeamChanged += HandleTeamChanged;
        GameEvents.OnTeamHealthChanged += HandleTeamChanged;
        GameEvents.AutoBattleModeChanged += HandleAutoModeChanged;

        _button.onClick.AddListener(OnButtonClicked);

        TryHookRiftEvents();

        // Immediate + deferred refresh.
        Apply();
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

        UnhookRiftEvents();

        if (_deferredApply != null)
        {
            StopCoroutine(_deferredApply);
            _deferredApply = null;
        }

        _button.onClick.RemoveListener(OnButtonClicked);
        ResetPressState();
    }

    private void Update()
    {
        // Cheap guard: if RiftManager instance changes across scenes, re-hook.
        // This prevents the button getting stuck disabled after a battle when no
        // energy/team events fire.
        if (_hookedRift != RiftManager.I)
            TryHookRiftEvents();

        if (!_pressed || _holdTriggered) return;

        if (Time.unscaledTime - _pressedAt >= holdToStopIdleSeconds)
        {
            _holdTriggered = true;
            _pressed = false;

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
    private void HandleAutoModeChanged(bool _) => Apply();

    private void Apply()
    {
        if (_button == null) return;

        bool idleAutoRunning = IsIdleAutoRunning();
        string reason = null;
        bool ok;

        if (idleAutoRunning)
        {
            ok = false;
            reason = "Idle auto-battle running.";
        }
        else
        {
            ok = EligibilityRules.CanStartRift(minRequiredTeamMembers, out reason);
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

        // If the button was force-enabled by some other script, still guard click feedback.
        if (IsIdleAutoRunning() || !EligibilityRules.CanStartRift(minRequiredTeamMembers, out _))
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

    private bool TryStopIdleAutoAndShowRewards()
    {
        bool stoppedAnyAuto = false;

        // Stop foreground rift AUTO loop first.
        var em = RiftManager.I;
        if (em != null && em.IsAutoMode)
        {
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

        if (!stoppedAnyAuto)
            return false;

        IdleBattleManager.I?.TryOpenSummaryIfNeeded();
        return true;
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
