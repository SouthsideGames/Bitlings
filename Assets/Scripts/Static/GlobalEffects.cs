public static class GlobalEffects
{
    public static float pickupRangeBonus; // 0..0.05

    public static void RecalcShinySynergy()
    {
        var data = SaveManager.Data;
        pickupRangeBonus = ShinySystems.GlobalPickupRangeBonus(data?.owned);
    }
}
