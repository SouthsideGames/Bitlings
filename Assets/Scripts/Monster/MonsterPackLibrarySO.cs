using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Monster Packs/Pack Library", fileName = "MonsterPackLibrary")]
public class MonsterPackLibrarySO : ScriptableObject
{
    // Keep serialized for Inspector editing
    [SerializeField] private List<MonsterPackSO> packs = new List<MonsterPackSO>();

    // ✅ Safe public read-only accessor (fixes CS0122)
    public List<MonsterPackSO> Packs => packs;

    private Dictionary<string, MonsterPackSO> _byId;

    // Optional read-only wrapper (for enumerations)
    public IReadOnlyList<MonsterPackSO> PacksReadOnly => packs;

    public void Warmup()
    {
        if (_byId != null) return;
        RebuildCache();
    }

    private void OnEnable() => RebuildCache();

    private void RebuildCache()
    {
        _byId = new Dictionary<string, MonsterPackSO>(packs.Count);
        for (int i = 0; i < packs.Count; i++)
        {
            var p = packs[i];
            if (!p || string.IsNullOrEmpty(p.id)) continue;
            if (!_byId.ContainsKey(p.id))
                _byId.Add(p.id, p);
        }
    }

    public MonsterPackSO Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) RebuildCache();
        _byId.TryGetValue(id, out var found);
        return found;
    }

#if UNITY_EDITOR
    [ContextMenu("Validate & Rebuild Cache")]
    private void Editor_Validate()
    {
        var seen = new HashSet<string>();
        for (int i = packs.Count - 1; i >= 0; i--)
        {
            var p = packs[i];
            if (!p || string.IsNullOrEmpty(p.id) || seen.Contains(p.id))
                continue;
            seen.Add(p.id);
        }
        RebuildCache();
    }
#endif
}
