using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
[DefaultExecutionOrder(-800)]
public sealed class PerfTimingProbe : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float logEverySeconds = 5f;
    [SerializeField, Range(10f, 120f)] private float lowFpsThreshold = 40f;

    private float _emaFps;
    private float _accum;
    private bool _warnedSlowTimeScale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<PerfTimingProbe>() != null)
            return;

        var go = new GameObject("PerfTimingProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<PerfTimingProbe>();
    }

    private void Awake()
    {
        if (FindObjectsByType<PerfTimingProbe>(FindObjectsSortMode.None).Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        DevLog.Log("[PerfTimingProbe] Active (dev-only logging enabled).", this);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f)
        {
            float fps = 1f / dt;
            _emaFps = Mathf.Lerp(_emaFps <= 0f ? fps : _emaFps, fps, 0.12f);
        }

        _accum += Time.unscaledDeltaTime;
        if (_accum >= logEverySeconds)
        {
            EmitSnapshot();
            _accum = 0f;
        }

        if (Time.timeScale < 0.99f)
        {
            if (!_warnedSlowTimeScale)
            {
                DevLog.Log($"[PerfTimingProbe] timeScale below 1 detected: {Time.timeScale:0.###}", this);
                _warnedSlowTimeScale = true;
            }
        }
        else if (_warnedSlowTimeScale)
        {
            DevLog.Log("[PerfTimingProbe] timeScale returned to 1.", this);
            _warnedSlowTimeScale = false;
        }
    }

    private void EmitSnapshot()
    {
        float fps = _emaFps <= 0f ? 0f : _emaFps;
        float ms = fps > 0f ? 1000f / fps : 0f;
        string scene = SceneManager.GetActiveScene().name;
        int targetFps = Application.targetFrameRate;
        int vSync = QualitySettings.vSyncCount;

        string perfTag = fps > 0f && fps < lowFpsThreshold ? "LOW_FPS" : "OK";

        DevLog.Log(
            $"[PerfTimingProbe][{perfTag}] scene={scene} fps={fps:0.0} frameMs={ms:0.0} " +
            $"timeScale={Time.timeScale:0.###} fixedDelta={Time.fixedDeltaTime:0.#####} " +
            $"targetFps={targetFps} vSync={vSync}",
            this
        );
    }

    private void OnApplicationPause(bool paused)
    {
        DevLog.Log($"[PerfTimingProbe] OnApplicationPause paused={paused}", this);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        DevLog.Log($"[PerfTimingProbe] OnApplicationFocus hasFocus={hasFocus}", this);
    }
}
#endif
