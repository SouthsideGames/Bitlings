using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Tags/Tag Library", fileName = "TagLibrary")]
public class TagLibrarySO : ScriptableObject
{
    [SerializeField] private List<TagSO> tags = new();
    private Dictionary<string, TagSO> _map;

    public static TagLibrarySO I;

    void OnEnable()
    {
        I = this;
        _map = new Dictionary<string, TagSO>(tags.Count);
        foreach (var t in tags)
        {
            if (t == null || string.IsNullOrEmpty(t.id)) continue;
            _map[t.id] = t;
        }
    }

    public TagSO GetById(string id)
    {
        if (string.IsNullOrEmpty(id) || _map == null) return null;
        return _map.TryGetValue(id, out var t) ? t : null;
    }

    public IEnumerable<TagSO> All() => tags;
}
