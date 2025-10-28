using UnityEngine;

[System.Serializable]
public class UpgradeCatalogEntry
{
    public UpgradeType type;
    public string displayName;
    public Sprite icon;

    [Tooltip("String id for Info panel, e.g., upg.tap, upg.idle")]
    public string infoId;
}
