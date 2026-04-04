/// <summary>
/// Small allocation-saver utility for commonly repeated log strings.
/// </summary>
public sealed class BattleLogBuffer
{
    private readonly string[] _roundLineCache = new string[256];

    public string GetRoundLine(int round)
    {
        if (round < 0) round = 0;

        if (round < _roundLineCache.Length)
        {
            var s = _roundLineCache[round];
            if (s == null)
            {
                s = "— Round " + round.ToString() + " —";
                _roundLineCache[round] = s;
            }
            return s;
        }

        return "— Round " + round.ToString() + " —";
    }
}
