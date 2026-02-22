using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Status/Status Library", fileName = "StatusLibrary")]
public sealed class StatusLibrarySO : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public StatusType type = StatusType.None;
        public Sprite icon;

        [Header("Tooltip")]
        [Tooltip("Optional display name used for tooltips/UI. If empty, the StatusType name is used.")]
        public string displayName;

        [TextArea(2, 6)]
        public string descriptionText;

        [Tooltip("Default duration in turns for timed statuses. Ignored for persistent statuses.")]
        public int defaultTurns = 3;

        [Tooltip("If true, status lasts the entire battle and does not show a turn counter.")]
        public bool persistent = false;

        [Header("Tier Magnitudes (optional)")]
        public float tier1Value = 0f;
        public float tier2Value = 0f;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    private Dictionary<StatusType, Entry> _map;

    public Entry Get(StatusType type)
    {
        if (type == StatusType.None) return null;
        EnsureMap();
        _map.TryGetValue(type, out var e);
        return e;
    }

    public string GetDisplayName(StatusType type)
    {
        if (type == StatusType.None) return string.Empty;
        var e = Get(type);
        if (e != null && !string.IsNullOrEmpty(e.displayName))
            return e.displayName;
        return type.ToString();
    }

    public string GetDescription(StatusType type)
    {
        if (type == StatusType.None) return string.Empty;
        var e = Get(type);
        return e != null ? (e.descriptionText ?? string.Empty) : string.Empty;
    }

    public Sprite GetIcon(StatusType type)
    {
        var e = Get(type);
        return e != null ? e.icon : null;
    }

    private void EnsureMap()
    {
        if (_map != null) return;
        _map = new Dictionary<StatusType, Entry>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            if (e.type == StatusType.None) continue;
            _map[e.type] = e;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keep map fresh in editor.
        _map = null;
    }
#endif
}
