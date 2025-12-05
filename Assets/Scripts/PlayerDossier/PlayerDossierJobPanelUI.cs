using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One job report card on Page 2 of the Player Dossier.
/// Handles job-specific flavor text + icon selection.
/// </summary>
public class PlayerDossierJobPanelUI : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private Image jobIconImage;
    [SerializeField] private TextMeshProUGUI jobNameText;

    [Header("Lines")]
    [SerializeField] private TextMeshProUGUI hoursText;        // e.g. "Hours Supervised: 16"
    [SerializeField] private TextMeshProUGUI materialsText;    // e.g. "Materials Smelted: 4,320"
    [SerializeField] private TextMeshProUGUI outputText;       // e.g. "Output / hr: 270"
    [SerializeField] private TextMeshProUGUI assignedText;     // e.g. "Assigned Fire Bitlings: 3"
    [SerializeField] private TextMeshProUGUI topPerformerText; // e.g. "Top Performer: FLAREBYTE (Lv. 14)"

    [Header("Job Icons")]
    [SerializeField] private Sprite defaultIcon;
    [SerializeField] private Sprite gymIcon;
    [SerializeField] private Sprite quarryIcon;
    [SerializeField] private Sprite mineIcon;
    [SerializeField] private Sprite powerPlantIcon;
    [SerializeField] private Sprite groveIcon;
    [SerializeField] private Sprite forgeIcon;
    [SerializeField] private Sprite workshopIcon;
    [SerializeField] private Sprite harborIcon;
    [SerializeField] private Sprite cryoLabIcon;
    [SerializeField] private Sprite observatoryIcon;
    [SerializeField] private Sprite containmentIcon;
    [SerializeField] private Sprite wyrmDenIcon;
    [SerializeField] private Sprite shadowMarketIcon;
    [SerializeField] private Sprite sanctumIcon;
    [SerializeField] private Sprite clinicIcon;

    /// <summary>
    /// Bind this panel to a job snapshot.
    /// </summary>
    public void Bind(JobSiteRowSnapshot row)
    {
        if (row == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        // Header
        if (jobNameText != null)
            jobNameText.text = row.displayName.ToUpperInvariant();

        if (jobIconImage != null)
            jobIconImage.sprite = GetIconForJob(row.job) ?? defaultIcon;

        // Flavor labels
        string materialsLabel = GetMaterialsLabel(row.job);
        string outputLabel    = GetOutputLabel(row.job);
        string typeLabel      = GetPrimaryTypeLabel(row.job);

        // Lines
        if (hoursText != null)
            hoursText.text = $"Hours Supervised: {row.hoursSupervised}";

        if (materialsText != null)
            materialsText.text = $"{materialsLabel}: {row.materialsProcessed:n0}";

        if (outputText != null)
            outputText.text = $"{outputLabel}: {row.outputPerHour}";

        if (assignedText != null)
            assignedText.text = $"Assigned {typeLabel} Bitlings: {row.assignedWorkers}";

        if (topPerformerText != null)
        {
            if (!string.IsNullOrEmpty(row.topPerformerName) && row.topPerformerLevel > 0)
                topPerformerText.text = $"Top Performer: {row.topPerformerName.ToUpperInvariant()} (Lv. {row.topPerformerLevel})";
            else
                topPerformerText.text = "Top Performer: —";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Flavor helpers
    // ─────────────────────────────────────────────────────────────

    private string GetMaterialsLabel(JobType job)
    {
        switch (job)
        {
            case JobType.Gym:          return "Sessions Overseen";
            case JobType.Quarry:       return "Stone Extracted";
            case JobType.Mine:         return "Ore Extracted";
            case JobType.PowerPlant:   return "Energy Generated";
            case JobType.Grove:        return "Harvests Collected";
            case JobType.Forge:        return "Materials Smelted";
            case JobType.Workshop:     return "Devices Assembled";
            case JobType.Harbor:       return "Shipments Managed";
            case JobType.CryoLab:      return "Cryo Samples Processed";
            case JobType.Observatory:  return "Signals Tracked";
            case JobType.Containment:  return "Anomalies Secured";
            case JobType.WyrmDen:      return "Eggs Tended";
            case JobType.ShadowMarket: return "Deals Brokered";
            case JobType.Sanctum:      return "Blessings Granted";
            case JobType.Clinic:       return "Cases Treated";
            default:                   return "Units Processed";
        }
    }

    private string GetOutputLabel(JobType job)
    {
        switch (job)
        {
            case JobType.Gym:          return "Sessions / hr";
            case JobType.Quarry:       return "Blocks / hr";
            case JobType.Mine:         return "Ore / hr";
            case JobType.PowerPlant:   return "Power / hr";
            case JobType.Grove:        return "Harvests / hr";
            case JobType.Forge:        return "Bars / hr";
            case JobType.Workshop:     return "Devices / hr";
            case JobType.Harbor:       return "Crates / hr";
            case JobType.CryoLab:      return "Samples / hr";
            case JobType.Observatory:  return "Readings / hr";
            case JobType.Containment:  return "Cases / hr";
            case JobType.WyrmDen:      return "Eggs / hr";
            case JobType.ShadowMarket: return "Deals / hr";
            case JobType.Sanctum:      return "Auras / hr";
            case JobType.Clinic:       return "Patients / hr";
            default:                   return "Output / hr";
        }
    }

    private string GetPrimaryTypeLabel(JobType job)
    {
        // This just controls the text in "Assigned X Bitlings"
        switch (job)
        {
            case JobType.Forge:       return "Fire";
            case JobType.Harbor:      return "Water";
            case JobType.CryoLab:     return "Ice";
            case JobType.Grove:       return "Grass";
            case JobType.PowerPlant:  return "Electric";
            case JobType.Quarry:
            case JobType.Mine:        return "Rock";
            case JobType.WyrmDen:     return "Wyrm";
            case JobType.ShadowMarket:return "Umbral";
            case JobType.Workshop:    return "Alloy";
            case JobType.Sanctum:     return "Oracle";
            case JobType.Clinic:      return "Support";
            case JobType.Containment: return "Corrupt";
            case JobType.Observatory: return "Sky";
            case JobType.Gym:         return "Clash";
            default:                  return "Bitling";
        }
    }

    private Sprite GetIconForJob(JobType job)
    {
        switch (job)
        {
            case JobType.Gym:          return gymIcon;
            case JobType.Quarry:       return quarryIcon;
            case JobType.Mine:         return mineIcon;
            case JobType.PowerPlant:   return powerPlantIcon;
            case JobType.Grove:        return groveIcon;
            case JobType.Forge:        return forgeIcon;
            case JobType.Workshop:     return workshopIcon;
            case JobType.Harbor:       return harborIcon;
            case JobType.CryoLab:      return cryoLabIcon;
            case JobType.Observatory:  return observatoryIcon;
            case JobType.Containment:  return containmentIcon;
            case JobType.WyrmDen:      return wyrmDenIcon;
            case JobType.ShadowMarket: return shadowMarketIcon;
            case JobType.Sanctum:      return sanctumIcon;
            case JobType.Clinic:       return clinicIcon;
            default:                   return defaultIcon;
        }
    }
}
