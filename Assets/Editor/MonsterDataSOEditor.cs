#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MonsterDataSO))]
[CanEditMultipleObjects]
public class MonsterDataSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the normal inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Generator", EditorStyles.boldLabel);

        if (GUILayout.Button("Auto-Fill From Type & Rarity"))
        {
            foreach (Object t in targets)
            {
                MonsterDataSO data = t as MonsterDataSO;
                if (data == null) continue;

                Undo.RecordObject(data, "Auto-Fill Monster Data");
                MonsterDataAutoFiller.Fill(data);
                EditorUtility.SetDirty(data);
            }

            Debug.Log("Monster auto-fill complete for selected MonsterDataSO asset(s).");
        }
    }
}
#endif
