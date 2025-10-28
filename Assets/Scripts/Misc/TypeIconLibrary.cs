using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "TypeIconLibrary", menuName = "Data/Type Icon Library", order = 0)]
public class TypeIconLibrary : ScriptableObject
{
    [System.Serializable]
    public struct TypeIconEntry
    {
        public MonsterType type;
        public Sprite icon;
    }

    [Header("Type Icons")]
    [SerializeField] private List<TypeIconEntry> icons = new List<TypeIconEntry>();

    private Dictionary<MonsterType, Sprite> lookup;

    void OnEnable()
    {
        // Build dictionary for quick lookup
        lookup = new Dictionary<MonsterType, Sprite>();
        foreach (var entry in icons)
        {
            if (!lookup.ContainsKey(entry.type) && entry.icon != null)
                lookup.Add(entry.type, entry.icon);
        }
    }


    public Sprite GetIcon(MonsterType type)
    {
        if (lookup == null || lookup.Count == 0) OnEnable();
        return lookup.TryGetValue(type, out var sprite) ? sprite : null;
    }

#if UNITY_EDITOR
    // Optional helper for testing in editor
    [ContextMenu("Rebuild Lookup")]
    void RebuildLookup() => OnEnable();
#endif
}