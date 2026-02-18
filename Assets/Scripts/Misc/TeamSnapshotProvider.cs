using UnityEngine;

public class TeamSnapshotProvider : MonoBehaviour
{
    public int GetAverageTeamLevel()
    {
        var t = SaveManager.Data?.team;
        if (t == null || t.Count == 0) return 1;
        int sum = 0, count = 0;
        for (int i = 0; i < t.Count && count < 3; i++)
        {
            var e = t[i];
            if (e == null) continue;
            if (string.IsNullOrEmpty(e.monsterId)) continue;
            if (e.currentHP <= 0) continue;
            sum += Mathf.Max(1, e.level);
            count++;
        }
        return Mathf.Max(1, Mathf.RoundToInt(sum / Mathf.Max(1f, count)));
    }
}
