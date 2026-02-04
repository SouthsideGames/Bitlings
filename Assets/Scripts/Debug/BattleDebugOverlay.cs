using System.Text;
using UnityEngine;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// Debug-only overlay to inspect battle/auto/summary state at runtime.
/// Toggle with F3.
/// </summary>
[DefaultExecutionOrder(-500)]
public sealed class BattleDebugOverlay : MonoBehaviour
{
    [Header("Overlay")]
    [SerializeField] private bool showOnStart = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.F3;
    [SerializeField] private int fontSize = 14;
    [SerializeField] private Vector2 margin = new Vector2(12f, 12f);

    private bool _visible;
    private float _fpsSma;
    private float _fpsTimer;

    void Awake()
    {
        if (FindObjectsOfType<BattleDebugOverlay>(true).Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        _visible = showOnStart;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            _visible = !_visible;

        // Simple FPS smoothing
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f)
        {
            float fps = 1f / dt;
            _fpsSma = Mathf.Lerp(_fpsSma <= 0f ? fps : _fpsSma, fps, 0.1f);
        }

        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer > 10f) _fpsTimer = 0f; // keep value bounded
    }

    void OnGUI()
    {
        if (!_visible) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Clamp(fontSize, 10, 22),
            richText = false
        };

        var sb = new StringBuilder(512);

        sb.AppendLine("=== BATTLE DEBUG (DEV) ===");
        sb.AppendLine($"FPS: {_fpsSma:0}");

        var em = EncounterManager.I;
        if (em != null)
        {
            sb.AppendLine("");
            sb.AppendLine("[EncounterManager]");
            sb.AppendLine($"InBattle: {em.IsInBattle}");
            sb.AppendLine($"AutoMode: {em.IsAutoMode}");
            sb.AppendLine($"NextEncounterFree: {em.NextEncounterIsFree}");
            sb.AppendLine($"WinStreak: {em.CurrentWinStreak}");
        }
        else sb.AppendLine("\n[EncounterManager] (null)");

        var pbs = PostBattleSummaryManager.I;
        if (pbs != null)
        {
            sb.AppendLine("");
            sb.AppendLine("[PostBattleSummary]");
            sb.AppendLine($"Pending: {pbs.Debug_PendingCount}");
            sb.AppendLine($"AutoHold: {pbs.Debug_AutoHold}");
            sb.AppendLine($"BattleInProgress: {pbs.Debug_BattleInProgress}");
            sb.AppendLine($"PanelOpen: {pbs.Debug_PanelOpen}");
        }
        else sb.AppendLine("\n[PostBattleSummary] (null)");

        sb.AppendLine("");
        sb.AppendLine("[IdleBattleStore]");
        var s = IdleBattleStore.Load();
        if (s != null)
        {
            sb.AppendLine($"autoBattling(flag): {s.autoBattling}");
            sb.AppendLine($"log entries: {(s.log != null ? s.log.Count : 0)}");
            sb.AppendLine($"capturedLog entries: {(s.capturedLog != null ? s.capturedLog.Count : 0)}");
            sb.AppendLine($"energyAtStart: {s.energyAtStart}");
            sb.AppendLine($"totalEnergySpent: {s.totalEnergySpent}");
            sb.AppendLine($"sessionStartUnix: {s.sessionStartUnix}");
            sb.AppendLine($"lastTickUnix: {s.lastTickUnix}");
        }

        sb.AppendLine("");
        sb.AppendLine("[Resources]");
        sb.AppendLine($"Energy: {ResourceBank.Get(ResourceType.Energy)}");
        sb.AppendLine($"Credits: {ResourceBank.Get(ResourceType.Credits)}");

        float width = 520f;
        float height = 999f;

        var rect = new Rect(margin.x, margin.y, width, height);
        GUI.Box(new Rect(rect.x - 6, rect.y - 6, rect.width + 12, 320f), GUIContent.none);
        GUI.Label(rect, sb.ToString(), style);
    }
}
#endif
