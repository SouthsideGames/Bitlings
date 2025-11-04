using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Monster/Monster Pack", fileName = "MonsterPack_")]
public class MonsterPackSO : ScriptableObject
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    public int tokenCost = 5;
    public List<MonsterDataSO> monsters = new List<MonsterDataSO>();
}
