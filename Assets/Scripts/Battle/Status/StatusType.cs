public enum StatusType
{
    None = 0,

    // Offensive
    Burn = 1, //Fire
    Soaked = 11, //Water, speed reduced. not implemented yet
    Regen = 6, //Grass, Heal at the start of turn. not implemented yet
    Shock = 3, // Electric
    Freeze = 2, //Ice
    Rally = 16, //Clash , Allies gain minor Attack boost, not implemented yet    
    Corrupt = 4, //Corrupt
    Sundering = 8, //Ground
    Tailwind = 5, //Sky, First attack during effect deals bonus damage. not implemented yet 
    Foresight = 12, //Oracle, If they repeat same action twice → stunned next turn.not implemented yet
    Leeching = 10, //Bug, Heal a portion of damage dealt. not implemented yet
    Shielded = 9, //Rocks, working but need to fix the ui
    Phantasmal = 14, //Specter, Lose HP when attacking. not implemented yet
    WyrmFury = 15, //Wyrm
    ShadowVeil = 13, // Umbral, immune to damage for 1 turn. not implemented yet
    Reinforce = 17, //Alloy


}
