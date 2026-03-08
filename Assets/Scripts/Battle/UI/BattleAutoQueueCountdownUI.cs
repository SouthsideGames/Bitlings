using TMPro;
using UnityEngine;

/// <summary>
/// Displays a countdown (10..0) for the manual-turn failsafe that auto-queues an Attack.
///
/// Wiring:
/// - Assign the TMP text that should show the countdown.
/// - Optional: assign a root GameObject to toggle active (recommended).
///
/// Behavior:
/// - Becomes active when BattleManager emits countdown as "show".
/// - Shows integer seconds remaining (ceil), clamped to >= 0.
/// - Hides when countdown ends or when player chooses an action.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleAutoQueueCountdownUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battle;

    [Header("UI")]
    [Tooltip("Optional root to enable/disable when countdown is active.")]
    [SerializeField] private GameObject visualsRoot;

    [SerializeField] private TMP_Text countdownText;

    [Header("Formatting")]
    [Tooltip("If true, shows whole seconds as a plain number. If false, shows 'Auto in: X'.")]
    [SerializeField] private bool numberOnly = true;

    [Tooltip("Label shown while auto-queue timer is paused by review UI (tutorial/log).")]
    [SerializeField] private string pausedLabel = "Reviewing...";

    private bool _visible;
    private bool _countdownActive;

    void Awake()
    {
        EnsureRefs();
        SetVisible(false);
    }

    void OnEnable()
    {
        EnsureRefs();
        if (battle != null)
            battle.OnAutoQueueCountdown += HandleCountdown;

        // Start hidden.
        SetVisible(false);
    }

    void OnDisable()
    {
        if (battle != null)
            battle.OnAutoQueueCountdown -= HandleCountdown;
    }

    private void EnsureRefs()
    {
        if (!battle)
        {
            battle = GetComponentInParent<BattleManager>();
            if (!battle) battle = FindFirstObjectByType<BattleManager>();
        }

        if (!countdownText)
            countdownText = GetComponentInChildren<TMP_Text>(true);
    }

    private void HandleCountdown(float remainingSeconds, bool show)
    {
        if (!show)
        {
            _countdownActive = false;
            if (!ShouldShowPaused())
                SetVisible(false);
            return;
        }

        _countdownActive = true;

        int display = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
        if (countdownText)
            countdownText.text = numberOnly ? display.ToString() : $"Auto in: {display}";

        AudioManager.I?.PlaySfx(SfxType.CountdownBeep);

        SetVisible(true);
    }

    void Update()
    {
        if (!_countdownActive && ShouldShowPaused())
        {
            if (countdownText)
                countdownText.text = string.IsNullOrWhiteSpace(pausedLabel) ? "Paused" : pausedLabel;

            SetVisible(true);
            return;
        }

        if (!_countdownActive)
            SetVisible(false);
    }

    private bool ShouldShowPaused()
    {
        if (battle == null) return false;
        if (!battle.AutoQueueFailsafeEnabled) return false;
        if (!battle.IsPlayerTurn) return false;
        if (battle.HasQueuedPlayerAction) return false;
        return battle.IsAutoQueuePausedByReviewUI;
    }

    private void SetVisible(bool v)
    {
        if (_visible == v) return;
        _visible = v;

        if (visualsRoot) visualsRoot.SetActive(v);
        else if (countdownText) countdownText.gameObject.SetActive(v);
    }
}
