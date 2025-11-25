// Assets/Scripts/Misc/UpgradeCatalogEntry.cs
using UnityEngine;

[System.Serializable]
public class UpgradeCatalogEntry
{
    [Header("Identity")]
    public FeatureId featureId;
    public string displayName;

    [Header("Info Panel")]
    [Tooltip("String id for Info panel, e.g., feat.idle.basic")]
    public string infoId;

    [Header("Cost")]
    [Min(0)]
    public int coinCost = 50;
}
