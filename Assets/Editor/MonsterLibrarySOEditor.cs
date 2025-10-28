// Assets/Editor/MonsterLibrarySOEditor.cs
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MonsterLibrarySO))]
public class MonsterLibrarySOEditor : Editor
{
    SerializedProperty monstersProp;

    Vector2 _scroll;
    float _normalizeTarget = 100f;
    float _setAllValue = 1f;
    float _minClamp = 0f;
    int _typeFilterIndex = 0;  // 0 = All
    string[] _typeOptions;

    void OnEnable()
    {
        monstersProp = serializedObject.FindProperty("monsters");

        var enumNames = System.Enum.GetNames(typeof(MonsterType));
        _typeOptions = new string[enumNames.Length + 1];
        _typeOptions[0] = "All";
        for (int i = 0; i < enumNames.Length; i++)
            _typeOptions[i + 1] = enumNames[i];
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Monster Library", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(monstersProp, includeChildren: true);

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Weighted Encounter Tools (float)", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _typeFilterIndex = EditorGUILayout.Popup(new GUIContent("Filter by Type"), _typeFilterIndex, _typeOptions);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Sort by % (desc)", GUILayout.Width(130))) SortByPercentage(desc: true);
                if (GUILayout.Button("Sort by Name (A–Z)", GUILayout.Width(140)))  SortByName();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _normalizeTarget = EditorGUILayout.FloatField(new GUIContent("Normalize to total"), Mathf.Max(0.0001f, _normalizeTarget));
                if (GUILayout.Button("Normalize", GUILayout.Width(100))) NormalizeTo(_normalizeTarget);
                if (GUILayout.Button("→ 1", GUILayout.Width(60))) NormalizeTo(1f);
                if (GUILayout.Button("→ 100", GUILayout.Width(70))) NormalizeTo(100f);
                if (GUILayout.Button("→ 1000", GUILayout.Width(70))) NormalizeTo(1000f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _setAllValue = EditorGUILayout.FloatField(new GUIContent("Set All Weights To"), Mathf.Max(0f, _setAllValue));
                if (GUILayout.Button("Apply", GUILayout.Width(80))) SetAll(_setAllValue);

                _minClamp = EditorGUILayout.FloatField(new GUIContent("Clamp Min"), Mathf.Max(0f, _minClamp));
                if (GUILayout.Button("Clamp", GUILayout.Width(80))) ClampMin(_minClamp);
            }

            EditorGUILayout.Space(6);
            DrawPreviewTableAndBars();
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawPreviewTableAndBars()
    {
        var rows = GatherRows(out float totalWeight);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Total Weight (filtered): {totalWeight:0.###}", EditorStyles.miniLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Icon", GUILayout.Width(40));
            GUILayout.Label("Name", GUILayout.Width(180));
            GUILayout.Label("Type", GUILayout.Width(80));
            GUILayout.Label("Weight", GUILayout.Width(80));
            GUILayout.Label("%", GUILayout.Width(70));
            GUILayout.Label("Bar", GUILayout.ExpandWidth(true));
        }

        EditorGUILayout.Space(2);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(320));

        foreach (var r in rows)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // icon
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(40)))
                {
                    Texture tex = null;
                    if (r.icon != null)
                    {
                        tex = AssetPreview.GetAssetPreview(r.icon);
                        if (tex == null && r.icon.texture != null) tex = r.icon.texture;
                    }
                    if (tex == null) tex = Texture2D.grayTexture;
                    GUILayout.Box(tex, GUILayout.Width(36), GUILayout.Height(36));
                }

                // name & type
                EditorGUILayout.LabelField(r.name, GUILayout.Width(180));
                EditorGUILayout.LabelField(r.typeName, GUILayout.Width(80));

                // weight editable (on the MonsterSO asset) — FLOAT
                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.FloatField(r.m.spawnWeight, GUILayout.Width(80));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(r.m, "Edit spawnWeight");
                    r.m.spawnWeight = Mathf.Max(0f, newWeight);
                    EditorUtility.SetDirty(r.m);
                }

                // percent (precomputed)
                EditorGUILayout.LabelField(totalWeight > 0f ? r.pct.ToString("0.00") + "%" : "-", GUILayout.Width(70));

                // bar uses current weight relative to current total
                Rect rBar = GUILayoutUtility.GetRect(50, 16, GUILayout.ExpandWidth(true));
                float p = totalWeight > 0f ? Mathf.Clamp01(r.m.spawnWeight / totalWeight) : 0f;
                EditorGUI.ProgressBar(rBar, p, "");
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox("Tip: Set weight = 0 to exclude a monster from wild encounters.", MessageType.Info);
    }

    // Gather filtered rows from object references (NOT FindPropertyRelative)
    System.Collections.Generic.List<(int idx, MonsterDataSO m, string name, string typeName, Sprite icon, float weight, float pct)>
    GatherRows(out float total)
    {
        total = 0f;
        var list = new System.Collections.Generic.List<(int idx, MonsterDataSO m, string name, string typeName, Sprite icon, float weight, float pct)>();

        if (monstersProp == null) return list;

        for (int i = 0; i < monstersProp.arraySize; i++)
        {
            var element = monstersProp.GetArrayElementAtIndex(i);
            if (element == null) continue;

            var m = element.objectReferenceValue as MonsterDataSO;
            if (m == null) continue; // empty slot in the array

            // Filter by type if requested
            if (_typeFilterIndex > 0)
            {
                int wanted = _typeFilterIndex - 1;
                if ((int)m.type != wanted) continue;
            }

            float w = Mathf.Max(0f, m.spawnWeight);
            total += w;

            string nm = string.IsNullOrEmpty(m.displayName) ? m.id : m.displayName;
            string typeName = System.Enum.GetName(typeof(MonsterType), m.type) ?? "-";
            var icon = m.icon;

            list.Add((idx: i, m: m, name: nm, typeName: typeName, icon: icon, weight: w, pct: 0f));
        }

        // compute percentages
        if (total > 0f)
        {
            for (int j = 0; j < list.Count; j++)
            {
                var row = list[j];
                list[j] = (row.idx, row.m, row.name, row.typeName, row.icon, row.weight, 100f * row.weight / total);
            }
        }

        return list;
    }

    void NormalizeTo(float targetSum)
    {
        if (monstersProp == null || monstersProp.arraySize == 0) return;

        // Read current weights
        float currentTotal = 0f;
        var ms = Enumerable.Range(0, monstersProp.arraySize)
            .Select(i => monstersProp.GetArrayElementAtIndex(i).objectReferenceValue as MonsterDataSO)
            .Where(m => m != null)
            .ToArray();

        var weights = new float[ms.Length];
        for (int i = 0; i < ms.Length; i++)
        {
            float w = Mathf.Max(0f, ms[i].spawnWeight);
            weights[i] = w;
            currentTotal += w;
        }

        if (ms.Length == 0) return;

        Undo.RecordObjects(ms, "Normalize spawn weights");

        if (currentTotal <= 0f)
        {
            // Set first to target, others 0
            if (ms.Length > 0) ms[0].spawnWeight = targetSum;
            for (int i = 1; i < ms.Length; i++) ms[i].spawnWeight = 0f;
            foreach (var m in ms) EditorUtility.SetDirty(m);
            Repaint();
            return;
        }

        // Scale to target (no rounding to preserve float precision)
        float k = targetSum / currentTotal;
        for (int i = 0; i < ms.Length; i++)
        {
            float newW = Mathf.Max(0f, weights[i] * k);
            ms[i].spawnWeight = newW;
        }

        foreach (var m in ms) EditorUtility.SetDirty(m);
        Repaint();
    }

    void SetAll(float value)
    {
        if (monstersProp == null || monstersProp.arraySize == 0) return;

        var ms = Enumerable.Range(0, monstersProp.arraySize)
            .Select(i => monstersProp.GetArrayElementAtIndex(i).objectReferenceValue as MonsterDataSO)
            .Where(m => m != null)
            .ToArray();

        if (ms.Length == 0) return;

        Undo.RecordObjects(ms, "Set all spawn weights");
        foreach (var m in ms) { m.spawnWeight = Mathf.Max(0f, value); EditorUtility.SetDirty(m); }
        Repaint();
    }

    void ClampMin(float min)
    {
        if (monstersProp == null || monstersProp.arraySize == 0) return;

        var ms = Enumerable.Range(0, monstersProp.arraySize)
            .Select(i => monstersProp.GetArrayElementAtIndex(i).objectReferenceValue as MonsterDataSO)
            .Where(m => m != null)
            .ToArray();

        if (ms.Length == 0) return;

        Undo.RecordObjects(ms, "Clamp min spawn weights");
        foreach (var m in ms) { m.spawnWeight = Mathf.Max(min, m.spawnWeight); EditorUtility.SetDirty(m); }
        Repaint();
    }

    void SortByName()
    {
        var entries = Enumerable.Range(0, monstersProp.arraySize)
            .Select(i => monstersProp.GetArrayElementAtIndex(i).objectReferenceValue as MonsterDataSO)
            .Where(m => m != null)
            .OrderBy(m => string.IsNullOrEmpty(m.displayName) ? m.id : m.displayName)
            .ToArray();

        WriteBackSorted(entries, "Sort by name");
    }

    void SortByPercentage(bool desc)
    {
        var entries = Enumerable.Range(0, monstersProp.arraySize)
            .Select(i => monstersProp.GetArrayElementAtIndex(i).objectReferenceValue as MonsterDataSO)
            .Where(m => m != null)
            .OrderBy(m => string.IsNullOrEmpty(m.displayName) ? m.id : m.displayName)
            .ToArray();

        entries = (desc
            ? entries.OrderByDescending(m => Mathf.Max(0f, m.spawnWeight)).ToArray()
            : entries.OrderBy(m => Mathf.Max(0f, m.spawnWeight)).ToArray());

        WriteBackSorted(entries, "Sort by percentage");
    }

    void WriteBackSorted(MonsterDataSO[] entries, string undoLabel)
    {
        Undo.RecordObject(target, undoLabel);
        monstersProp.ClearArray();
        monstersProp.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
            monstersProp.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        Repaint();
    }
}
