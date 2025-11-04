using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Monster Packs/Pack Library", fileName = "MonsterPackLibrary")]
public class MonsterPackLibrarySO : ScriptableObject
{
    public List<MonsterPackSO> packs = new List<MonsterPackSO>();
    public MonsterPackSO Get(string id)
    {
        for (int i = 0; i < packs.Count; i++)
        {
            var p = packs[i];
            if (p != null && p.id == id) return p;
        }
        return null;
    }
}
