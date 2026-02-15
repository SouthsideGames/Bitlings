using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/World Events/World Event Library", fileName = "WorldEventLibrary")]
public class WorldEventLibrarySO : ScriptableObject
{
    public List<WorldEventSO> events = new();
}
