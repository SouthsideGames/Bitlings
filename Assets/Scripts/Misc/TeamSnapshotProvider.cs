using UnityEngine;

public class TeamSnapshotProvider : MonoBehaviour
{
    public int GetAverageTeamLevel()
    {
        var t = SaveManager.Data?.team;
        if (t == null || t.Count == 0) return 1;
        int sum = 0, count = 0;
        for (int i = 0; i < t.Count && i < 3; i++) { sum += Mathf.Max(1, t[i].level); count++; }
        return Mathf.Max(1, Mathf.RoundToInt(sum / Mathf.Max(1f, count)));
    }
}
