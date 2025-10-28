using System;
using System.Collections.Generic;
using UnityEngine;

public class TagTriggerRuntimeAudit : MonoBehaviour
{
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    [Tooltip("Run the audit automatically on scene start (Development builds/Editor only).")]
    public bool autoRunOnStart = true;

    void Start()
    {
        if (autoRunOnStart) SafeRun();
    }

    [ContextMenu("Run Runtime Audit Now")]
    public void SafeRun()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Debug.LogError("[TagTriggerRuntimeAudit] Exception: " + ex);
        }
    }

    public static void Run()
    {
        // Collect which triggers exist in TagSOs at runtime (build-friendly).
        var used = new HashSet<TagTrigger>();
        var all  = Resources.FindObjectsOfTypeAll<TagSO>(); // requires TagSOs addressable or included; otherwise load via your library
        foreach (var so in all)
        {
            if (so?.effects == null) continue;
            foreach (var e in so.effects) used.Add(e.trigger);
        }

        // Assume code intends to support all enum values (or introspect your dispatcher).
        var supported = new HashSet<TagTrigger>((TagTrigger[])Enum.GetValues(typeof(TagTrigger)));

        var missing = new List<TagTrigger>();
        foreach (var t in used) if (!supported.Contains(t)) missing.Add(t);

        if (missing.Count > 0)
        {
            Debug.LogError("[TagTriggerRuntimeAudit] Content uses triggers that code does not support: " +
                           string.Join(", ", missing));
        }
        else
        {
            Debug.Log("[TagTriggerRuntimeAudit] OK — all content-used triggers are supported by code.");
        }
    }
#endif
}
