public static class GlobalEffects
{
    public static float pickupRangeBonus; // 0..0.05

    public static void RecalcPremiumSynergy()
    {
        var data = SaveManager.Data;
        pickupRangeBonus = PremiumSystems.GlobalPickupRangeBonus(data?.owned);
    }
}
