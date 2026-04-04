using UnityEngine;

public sealed class IronInvariantGuard : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Tuning")]
    [Tooltip("Seconds between invariant checks while Iron is active.")]
    [SerializeField] private float checkIntervalSeconds = 0.75f;

    [Tooltip("If true, also check on every frame while Iron is active (more noise, more immediate).")]
    [SerializeField] private bool checkEveryFrame = false;

    private bool _prevActive;
    private float _t;
    private IronCareerManager _iron;

    private void Awake()
    {
        _iron = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        bool active = IronCareerRuntime.IsActive;

        // Rising edge
        if (!_prevActive && active)
        {
            IronNoBleedHarness.Arm("RuntimeEnter");
            IronNoBleedHarness.AssertUnchanged("RuntimeEnter");
            _t = 0f;
        }

        // Falling edge
        if (_prevActive && !active)
        {
            IronNoBleedHarness.AssertUnchanged("RuntimeExit");
            IronNoBleedHarness.Disarm();
            _t = 0f;
        }

        _prevActive = active;

        if (!active) return;

        if (checkEveryFrame)
        {
            IronNoBleedHarness.AssertUnchanged(Context("Tick"));
            return;
        }

        _t += Time.unscaledDeltaTime;
        if (_t >= Mathf.Max(0.05f, checkIntervalSeconds))
        {
            _t = 0f;
            IronNoBleedHarness.AssertUnchanged(Context("Tick"));
        }
    }

    private string Context(string label)
    {
        int wins = 0;
        if (_iron == null) _iron = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);
        if (_iron != null) wins = _iron.Wins;
        return $"{label} wins={wins}";
    }
#else
    private void Update() { }
#endif
}
