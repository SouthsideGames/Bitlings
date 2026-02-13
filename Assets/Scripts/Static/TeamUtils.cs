using System.Linq;

public static class TeamUtils
{
    public static bool HasPlayableTeam(int minRequired = 1)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null) return false;

        int filled = 0;
        for (int i = 0; i < data.team.Count; i++)
        {
            var e = data.team[i];

            if (e != null && !string.IsNullOrEmpty(e.monsterId) && (e.currentHP > 0 || e.currentHP < 0))
            {
                filled++;
                if (filled >= minRequired) return true;
            }
        }
        return false;
    }
}
