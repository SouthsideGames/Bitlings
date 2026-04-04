public static class MonsterStatApplier
{
    public static void Apply(OwnedMonsterData m, TrainingBonus delta)
    {
        if (m == null) return;

        // Persist totals (for display / calc)
        m.trainingBonus.Add(delta);
    }
}