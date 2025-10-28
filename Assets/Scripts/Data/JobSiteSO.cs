using UnityEngine;

[CreateAssetMenu(menuName = "Data/Job Site", fileName = "JobSite_")]
public class JobSiteSO : ScriptableObject
{
    public JobType jobType;
    public ResourceType produces;

    [Header("Production")]
    public float baseRatePerHour = 100f;
    public int storageCap = 1000;

    [Header("Slots")]
    [Range(1, 3)] public int maxWorkers = 3;

    [Header("Eligibility")]
    public MonsterType[] eligibleTypes;

    // 👇 New fields
    [Header("Visuals")]
    public Sprite icon;              
    public string displayName;          
}
