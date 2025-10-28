using UnityEngine;

public class JobXpTracker : MonoBehaviour
{
    [SerializeField, Min(0.25f)] private float tickSeconds = 1f;
    [SerializeField, Min(0.1f)] private float xpPerHour = 100f;

    private float _acc;

    void Update()
    {
        _acc += Time.unscaledDeltaTime;
        if (_acc < tickSeconds) return;
        float dt = _acc; _acc = 0f;

        var jm = JobManager.I;
        if (jm == null || jm.States == null) return;

        float xpGain = xpPerHour * (dt / 3600f);

        foreach (var state in jm.States)
        {
            if (state?.workers == null || state.config == null) continue;

            foreach (var w in state.workers)
            {
                if (w == null || string.IsNullOrEmpty(w.monsterId) || w.def == null) continue;
                MonsterJobProgress.AddJobXp(w.monsterId, xpGain, w.def);
            }
        }
    }
}
