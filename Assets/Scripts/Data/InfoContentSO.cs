using UnityEngine;

public enum InfoCategory { Misc = 0, Resource, JobSite, Tag, Monster, Upgrade}

[CreateAssetMenu(fileName="Info_", menuName="Data/Info/Generic Info")]
public class InfoContentSO : ScriptableObject
{
    [Header("Lookup")]
    public string id;              // ← e.g., "res.coins", "job.forge", "tag.hunter"
    public InfoCategory category;

    [Header("Display")]
    public string title;
    public string subtitle;
    [TextArea(4, 12)] public string body;  // Put “Comes from / Used for” here
    public Sprite icon;                    // Optional
}
