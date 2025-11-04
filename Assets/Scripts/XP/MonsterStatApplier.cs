using UnityEngine;

public static class MonsterStatApplier
{
    // Applies both to live stats AND to trainingBonus for persistence.
    public static void Apply(OwnedMonsterData m, TrainingBonus delta)
    {
        if (m == null) return;

        // Live stats
        m.flatAtkBonus += delta.atk;

        // Persist the training delta
        m.trainingBonus.Add(delta);
    }
}
