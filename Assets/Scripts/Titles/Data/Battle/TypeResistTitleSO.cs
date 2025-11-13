using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Battle/Type Resist", fileName = "TypeResistTitle")]
[Tooltip("Used to define Titles that reduce damage taken from specific monster types.")]
public sealed class TypeResistTitleSO : TitleSO
{
    [Header("Type Resistance Settings")]
    [Tooltip("Incoming damage from these types will be modified by the 'incomingMultiplier' value.")]
    public MonsterType[] resistTypes = new MonsterType[] { MonsterType.None };

    [Tooltip("Damage multiplier applied to resisted types. Example: 0.75 = 25% less damage.")]
    [Range(0f, 1.5f)]
    public float incomingMultiplier = 0.75f;
}
