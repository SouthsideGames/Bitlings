using UnityEngine;

public readonly struct TagSignal
{
    public readonly int TurnIndex;
    public readonly MonsterType AttackerType;
    public readonly MonsterType DefenderType;
    public readonly bool EnemyIsBoss;
    public readonly float Damage;
    public readonly JobType JobSite;
    public readonly bool ActedFirstThisRound;
    public readonly int AttacksThisTurn;

    public TagSignal(
        int turnIndex,
        MonsterType atk,
        MonsterType def,
        bool enemyBoss,
        float damage,
        JobType jobSite,
        bool actedFirst,
        int attacksThisTurn)
    {
        TurnIndex = turnIndex;
        AttackerType = atk;
        DefenderType = def;
        EnemyIsBoss = enemyBoss;
        Damage = damage;
        JobSite = jobSite;
        ActedFirstThisRound = actedFirst;
        AttacksThisTurn = attacksThisTurn;
    }
}
