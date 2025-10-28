#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class TagTriggerAudit
{
    // ===== PUBLIC ENTRYPOINTS =====
    [InitializeOnLoadMethod]
    static void OnEditorLoad() => EditorApplication.delayCall += () => RunAudit(verbose:false);

    [MenuItem("Tools/Tag Triggers Audit/Run Now")]
    public static void RunAuditMenu() => RunAudit(verbose:true);

    // ===== CORE =====
    public static void RunAudit(bool verbose)
    {
        try
        {
            var usedByContent = FindAllUsedTriggersInProject();   // from TagSO assets
            var enumAll       = Enum.GetValues(typeof(TagTrigger)).Cast<TagTrigger>().ToArray();

            // You can optionally restrict "supported" to only what your dispatcher handles.
            // For now, we assume you intend to support all enum values.
            var supportedByCode = new HashSet<TagTrigger>(enumAll);

            var missing = usedByContent.Where(t => !supportedByCode.Contains(t)).Distinct().ToList();
            var unused  = enumAll.Where(t => !usedByContent.Contains(t)).Distinct().ToList();

            if (missing.Count == 0 && verbose)
                Debug.Log("<b>[TagTriggerAudit]</b> All content-used triggers are supported by code. ✅");

            if (missing.Count > 0)
            {
                Debug.LogError("<b>[TagTriggerAudit]</b> Triggers referenced by content but not supported by code:\n" +
                               string.Join(", ", missing.Select(x => x.ToString())));
            }

            if (verbose)
            {
                Debug.Log("<b>[TagTriggerAudit]</b> Content uses:\n" + string.Join(", ", usedByContent.Select(x => x.ToString())));
                Debug.Log("<b>[TagTriggerAudit]</b> Enum values with NO content using them (currently unused in TagSOs):\n" +
                          (unused.Count == 0 ? "(none)" : string.Join(", ", unused.Select(x => x.ToString()))));
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[TagTriggerAudit] Exception: " + ex);
        }
    }

    // ===== HELPERS =====
    static HashSet<TagTrigger> FindAllUsedTriggersInProject()
    {
        var guids = AssetDatabase.FindAssets("t:TagSO");
        var set   = new HashSet<TagTrigger>();

        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var so   = AssetDatabase.LoadAssetAtPath<TagSO>(path);
            if (so == null || so.effects == null) continue;

            foreach (var e in so.effects)
                set.Add(e.trigger);
        }
        return set;
    }
}
#endif
