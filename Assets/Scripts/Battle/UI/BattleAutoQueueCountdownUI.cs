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

    private bool _visible;

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
            SetVisible(false);
            return;
        }

        int display = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
        if (countdownText)
            countdownText.text = numberOnly ? display.ToString() : $"Auto in: {display}";

        SetVisible(true);
    }

    private void SetVisible(bool v)
    {
        if (_visible == v) return;
        _visible = v;

        if (visualsRoot) visualsRoot.SetActive(v);
        else if (countdownText) countdownText.gameObject.SetActive(v);
    }
}
