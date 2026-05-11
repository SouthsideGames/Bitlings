using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles the Rift button on the home screen.
/// - Tap  -> does nothing (PanelButtonUI opens the Rift panel via Button.onClick).
/// - Hold -> toggles idle auto-battle ON/OFF with a 3-second radial fill countdown.
///           Does nothing if Idle Battles are not unlocked.
/// </summary>
public class AutoBattleButton : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Hold Settings")]
    [Tooltip("Seconds the player must hold to toggle auto-battle.")]
    public float holdSeconds = 3f;

    [Tooltip("Radial Image (fillAmount 0-1) that fills while holding. Assign in Inspector.")]
    [SerializeField] private Image holdProgressFill;

    [Tooltip("Parent GameObject of the fill image. Hidden immediately if idle battles are locked.")]
    [SerializeField] private GameObject holdFillContainer;

    private bool _pressed;
    private float _pressedAt;
    private bool _firedHold;

    private const float FILL_DEAD_ZONE = 0.15f;

    private void Awake()
    {
        HideContainer(); // CHANGED: ensure container starts hidden regardless of scene state
    }

    private void Update()
    {
        if (!_pressed || _firedHold) return;

        if (!IsIdleBattleUnlocked())
        {
            SetFill(0f);
            return;
        }

        float heldFor = Time.unscaledTime - _pressedAt;

        float visibleT = heldFor < FILL_DEAD_ZONE
            ? 0f
            : Mathf.Clamp01((heldFor - FILL_DEAD_ZONE) / (holdSeconds - FILL_DEAD_ZONE));
        SetFill(visibleT);

        if (heldFor >= holdSeconds)
        {
            _firedHold = true;
            _pressed = false;
            SetFill(1f);

            TriggerHaptic();
            ToggleIdleAuto();
            HideContainer(); // CHANGED: hide container after hold completes
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pressed = true;
        _firedHold = false;
        _pressedAt = Time.unscaledTime;

        // CHANGED: only reveal the container if the feature is unlocked - locked players never see it
        if (IsIdleBattleUnlocked())
            ShowContainer();
        else
            HideContainer();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResetPressState();
    }

    public void OnPointerExit(PointerEventData eventData) => ResetPressState();

    private void OnDisable() => ResetPressState();

    private void ToggleIdleAuto()
    {
        if (IsIdleAutoRunning())
        {
            IdleBattleManager.I?.DisableAuto();
        }
        else
        {
            IdleBattleManager.I?.EnableAuto();
        }
    }

    private void SetFill(float amount)
    {
        if (holdProgressFill != null)
            holdProgressFill.fillAmount = amount;
    }

    private void ShowContainer() // CHANGED: reveal fill container only during an active valid hold
    {
        if (holdFillContainer != null)
            holdFillContainer.SetActive(true);
    }

    private void HideContainer() // CHANGED: hide fill container when not holding
    {
        if (holdFillContainer != null)
            holdFillContainer.SetActive(false);
        SetFill(0f);
    }

    private void ResetPressState()
    {
        _pressed = false;
        _firedHold = false;
        _pressedAt = 0f;
        HideContainer(); // CHANGED: always hide on release, exit, or disable
    }

    private static bool IsIdleBattleUnlocked()
    {
        try { return FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic); }
        catch { return false; }
    }

    private static bool IsIdleAutoRunning()
    {
        try { var s = IdleBattleStore.Load(); return s != null && s.autoBattling; }
        catch { return false; }
    }

    private static void TriggerHaptic()
    {
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }
}
