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
    [SerializeField] private TextMeshProUGUI assignedText;     // e.g. "Assigned Bitlings: 3"
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

        // Lines
        if (hoursText != null)
            hoursText.text = $"Hours Supervised: {row.hoursSupervised}";

        if (materialsText != null)
            materialsText.text = $"{materialsLabel}: {row.materialsProcessed:n0}";

        if (outputText != null)
            outputText.text = $"{outputLabel}: {row.outputPerHour}";

        if (assignedText != null)
            assignedText.text = $"Assigned Bitlings: {row.assignedWorkers}";

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
            case JobType.Power_Plant:   return "Energy Generated";
            case JobType.Grove:        return "Harvests Collected";
            case JobType.Forge:        return "Materials Smelted";
            case JobType.Workshop:     return "Devices Assembled";
            case JobType.Harbor:       return "Shipments Managed";
            case JobType.Cryo_Lab:      return "Cryo Samples Processed";
            case JobType.Observatory:  return "Signals Tracked";
            case JobType.Containment:  return "Anomalies Secured";
            case JobType.Wyrm_Den:      return "Eggs Tended";
            case JobType.Shadow_Market: return "Deals Brokered";
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
            case JobType.Power_Plant:   return "Power / hr";
            case JobType.Grove:        return "Harvests / hr";
            case JobType.Forge:        return "Bars / hr";
            case JobType.Workshop:     return "Devices / hr";
            case JobType.Harbor:       return "Crates / hr";
            case JobType.Cryo_Lab:      return "Samples / hr";
            case JobType.Observatory:  return "Readings / hr";
            case JobType.Containment:  return "Cases / hr";
            case JobType.Wyrm_Den:      return "Eggs / hr";
            case JobType.Shadow_Market: return "Deals / hr";
            case JobType.Sanctum:      return "Auras / hr";
            case JobType.Clinic:       return "Patients / hr";
            default:                   return "Output / hr";
        }
    }

    private Sprite GetIconForJob(JobType job)
    {
        switch (job)
        {
            case JobType.Gym:          return gymIcon;
            case JobType.Quarry:       return quarryIcon;
            case JobType.Mine:         return mineIcon;
            case JobType.Power_Plant:   return powerPlantIcon;
            case JobType.Grove:        return groveIcon;
            case JobType.Forge:        return forgeIcon;
            case JobType.Workshop:     return workshopIcon;
            case JobType.Harbor:       return harborIcon;
            case JobType.Cryo_Lab:      return cryoLabIcon;
            case JobType.Observatory:  return observatoryIcon;
            case JobType.Containment:  return containmentIcon;
            case JobType.Wyrm_Den:      return wyrmDenIcon;
            case JobType.Shadow_Market: return shadowMarketIcon;
            case JobType.Sanctum:      return sanctumIcon;
            case JobType.Clinic:       return clinicIcon;
            default:                   return defaultIcon;
        }
    }
}
