using System;

[Flags]
public enum BattleLineTag
{
    None           = 0,
    Result         = 1 << 0, // Always keep in condensed mode
    Flavor         = 1 << 1, // Can be removed in condensed / auto-compress
    Crit           = 1 << 2,
    Shield         = 1 << 3,
    SuperEffective = 1 << 4,
    NotEffective   = 1 << 5
}

[Serializable]
public struct BattleLine
{
    public string text;
    public BattleLineTag tags;

    public BattleLine(string text, BattleLineTag tags = BattleLineTag.None)
    {
        this.text = text;
        this.tags = tags;
    }
}
