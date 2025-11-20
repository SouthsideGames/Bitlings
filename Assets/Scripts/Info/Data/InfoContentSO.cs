using UnityEngine;

public enum InfoCategory { Misc = 0, Resource = 1, JobSite = 2, Tag = 3, Monster = 4, Upgrade = 5, Titles = 6 }

[CreateAssetMenu(fileName="Info_", menuName="Data/Info/Generic Info")]
public class InfoContentSO : ScriptableObject
{
    [Header("Lookup")]
    public string id;    
    public InfoCategory category;

    [Header("Display")]
    public string title;
    public string subtitle;
    [TextArea(4, 12)] public string body;
}
