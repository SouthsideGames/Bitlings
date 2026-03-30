using System;
using System.Collections.Generic;
using UnityEngine;


public sealed class WorldEventManager : MonoBehaviour
{
    public static WorldEventManager I { get; private set; }

    public event Action Changed;

    [Serializable]
    public sealed class Item
    {
        public string id;
        public string message;
        public long expiresUnix;
        public bool hasEffect;
    }

    private readonly List<Item> _items = new();
    private int _serial;

    public IReadOnlyList<Item> Items => _items;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    private void Update()
    {
        if (_items.Count == 0) return;

        long now = SaveManager.NowUnix();
        bool changed = false;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it == null) { _items.RemoveAt(i); changed = true; continue; }
            if (it.expiresUnix > 0 && now >= it.expiresUnix)
            {
                _items.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    public string Add(string message, float ttlSeconds = 0f, bool hasEffect = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        _serial++;
        string id = $"WE_FEED::{_serial}";
        long expires = 0;
        if (ttlSeconds > 0f)
            expires = SaveManager.NowUnix() + Mathf.Max(1, Mathf.RoundToInt(ttlSeconds));

        _items.Add(new Item { id = id, message = message.Trim(), expiresUnix = expires, hasEffect = hasEffect });
        Changed?.Invoke();
        return id;
    }

    public void Remove(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] != null && string.Equals(_items[i].id, id, StringComparison.Ordinal))
            {
                _items.RemoveAt(i);
                Changed?.Invoke();
                return;
            }
        }
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }
}
