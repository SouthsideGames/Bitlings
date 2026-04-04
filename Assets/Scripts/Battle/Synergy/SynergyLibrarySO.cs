using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Synergy/Synergy Library", fileName = "SynergyLibrary")]
public sealed class SynergyLibrarySO : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public MonsterType type;
        public SynergyTier tier;
        public StatusType status;
        public SynergyTargetScope scope = SynergyTargetScope.EnemySingle;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();
    private Dictionary<(MonsterType, SynergyTier), Entry> _map;

    public Entry Get(MonsterType type, SynergyTier tier)
    {
        EnsureMap();
        _map.TryGetValue((type, tier), out var e);
        return e;
    }

    private void EnsureMap()
    {
        if (_map != null) return;
        _map = new Dictionary<(MonsterType, SynergyTier), Entry>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            _map[(e.type, e.tier)] = e;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _map = null;
    }
#endif
}