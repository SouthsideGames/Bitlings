using System.Collections.Generic;

public static class SanctumHelpers
{
    // Compute final ritual duration with premium reduction.
    public static float ComputeFinalSeconds(float baseSeconds, IEnumerable<OwnedMonsterData> participants)
    {
        float mult = PremiumSystems.SanctumDurationMult(participants); // -3% each, cap -10%
        return baseSeconds * mult;
    }
}
