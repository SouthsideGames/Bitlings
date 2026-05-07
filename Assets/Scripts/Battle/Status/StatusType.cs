public enum StatusType
{
    None = 0,

    // Offensive
    Burn = 1,        // Fire
    Soaked = 11,     // Water — speed reduced
    Regen = 6,       // Grass - heals the afflicted monster at the start of each turn.
    Shock = 3,       // Electric
    Freeze = 2,      // Ice
    Rally = 16,      // Clash — allies gain minor Attack boost
    Corrupt = 4,     // Corrupt
    Sundering = 8,   // Ground
    Tailwind = 5,    // Sky — first attack during effect deals bonus damage
    Foresight = 12,  // Oracle — repeat same action twice → stunned next turn
    Leeching = 10,   // Bug — heal a portion of damage dealt
    Shielded = 9,    // Rocks — grants shield on application
    Phantasmal = 14, // Specter — lose HP when attacking
    WyrmFury = 15,   // Wyrm
    ShadowVeil = 13, // Umbral — immune to damage for 1 turn
    Reinforce = 17,  // Alloy
}
