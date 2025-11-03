using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Capacity +Flat", fileName = "JobCapacityBoosterTitle")]
public sealed class JobCapacityBoosterTitleSO : TitleSO
{
    [Header("Increase site storage capacity by a flat amount")]
    public int capacityBonusFlat = 0;
}
