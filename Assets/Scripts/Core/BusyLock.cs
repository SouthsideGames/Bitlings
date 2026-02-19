using System;
using System.Collections.Generic;
using UnityEngine;


public static class BusyLock
{
    private static readonly Dictionary<string, float> _untilByKey = new Dictionary<string, float>(StringComparer.Ordinal);

    public static bool TryEnter(string key, float holdSeconds)
    {
        if (string.IsNullOrEmpty(key)) return true;

        float now = Time.unscaledTime;
        if (_untilByKey.TryGetValue(key, out var until) && now < until)
            return false;

        _untilByKey[key] = now + Mathf.Max(0.01f, holdSeconds);
        return true;
    }

    public static void ClearAll() => _untilByKey.Clear();
}
