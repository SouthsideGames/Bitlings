using UnityEngine;

public sealed class ExecutiveTrailInvariantGuard : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Tuning")]
    [Tooltip("Seconds between invariant checks while Executive Trial is active.")]
    [SerializeField] private float checkIntervalSeconds = 0.75f;

    [Tooltip("If true, also check on every frame while Executive Trial is active (more noise, more immediate).")]
    [SerializeField] private bool checkEveryFrame = false;

    private bool _prevActive;
    private float _t;
    private ExecutiveTrialManager _executiveTrialManager;

    private void Awake()
    {
        _executiveTrialManager = FindFirstObjectByType<ExecutiveTrialManager>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        bool active = ExecutiveTrialRuntime.IsActive;

        // Rising edge
        if (!_prevActive && active)
        {
            ExecutiveTrailNoBleedHarness.Arm("RuntimeEnter");
            ExecutiveTrailNoBleedHarness.AssertUnchanged("RuntimeEnter");
            _t = 0f;
        }

        // Falling edge
        if (_prevActive && !active)
        {
            ExecutiveTrailNoBleedHarness.AssertUnchanged("RuntimeExit");
            ExecutiveTrailNoBleedHarness.Disarm();
            _t = 0f;
        }

        _prevActive = active;

        if (!active) return;

        if (checkEveryFrame)
        {
            ExecutiveTrailNoBleedHarness.AssertUnchanged(Context("Tick"));
            return;
        }

        _t += Time.unscaledDeltaTime;
        if (_t >= Mathf.Max(0.05f, checkIntervalSeconds))
        {
            _t = 0f;
            ExecutiveTrailNoBleedHarness.AssertUnchanged(Context("Tick"));
        }
    }

    private string Context(string label)
    {
        int wins = 0;
        if (_executiveTrialManager == null) _executiveTrialManager = FindFirstObjectByType<ExecutiveTrialManager>(FindObjectsInactive.Include);
        if (_executiveTrialManager != null) wins = _executiveTrialManager.Wins;
        return $"{label} wins={wins}";
    }
#else
    private void Update() { }
#endif
}
