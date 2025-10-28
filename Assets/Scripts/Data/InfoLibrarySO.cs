using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(fileName="InfoLibrary", menuName="Data/Info/Info Library")]
public class InfoLibrarySO : ScriptableObject
{
    public List<InfoContentSO> entries = new();

    private static InfoLibrarySO _cache;
    public static InfoLibrarySO Load()
    {
        if (_cache == null) _cache = Resources.Load<InfoLibrarySO>("InfoLibrary");
        return _cache;
    }

    public static InfoContentSO Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var lib = Load();
        if (!lib) return null;
        return lib.entries.FirstOrDefault(e => e && e.id == id);
    }
}
