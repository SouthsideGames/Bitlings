using UnityEngine;

public abstract class TitleSO : ScriptableObject
{
    [Header("Identity")]
    public string titleId;          // unique key
    public string displayName;      // shown in UI
    [TextArea] public string description;
}
