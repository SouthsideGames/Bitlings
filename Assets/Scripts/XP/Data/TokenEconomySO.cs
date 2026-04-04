using UnityEngine;

[CreateAssetMenu(menuName = "Data/XP System/Token Economy", fileName = "TokenEconomy")]
public class TokenEconomySO : ScriptableObject
{
    [Header("XP → Growth Core conversion")]
    [Tooltip("How many XP become 1 Growth Core. Example: 10 XP = 1 GC")]
    public int xpPerCore = 10;

    [Header("Growth point per core (for stat bucket spends)")]
    public int hpPerCore  = 3;
    public int atkPerCore = 1;
    public int defPerCore = 1;
    public int spdPerCore = 1;

    public int TokensFromXP(int xp)
    {
        if (xp <= 0 || xpPerCore <= 0) return 0;
        return Mathf.FloorToInt(xp / (float)xpPerCore);
    }

    // Inverse conversions (for refund/reset features)
    public int CoresForHp(int hp)   => hpPerCore  <= 0 ? 0 : Mathf.CeilToInt(hp  / (float)hpPerCore);
    public int CoresForAtk(int atk) => atkPerCore <= 0 ? 0 : Mathf.CeilToInt(atk / (float)atkPerCore);
    public int CoresForDef(int def) => defPerCore <= 0 ? 0 : Mathf.CeilToInt(def / (float)defPerCore);
    public int CoresForSpd(int spd) => spdPerCore <= 0 ? 0 : Mathf.CeilToInt(spd / (float)spdPerCore);

    public static TokenEconomySO Load() => Resources.Load<TokenEconomySO>("TokenEconomy");
}