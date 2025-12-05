using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PlayerDossierJobPanelUI : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private Image jobIconImage;              // optional
    [SerializeField] private TextMeshProUGUI jobNameText;

    [Header("Lines")]
    [SerializeField] private TextMeshProUGUI hoursText;       // "Hours Supervised: 16"
    [SerializeField] private TextMeshProUGUI materialsText;   // "Materials Smelted: 4,320"
    [SerializeField] private TextMeshProUGUI outputText;      // "Output / hr: 270"
    [SerializeField] private TextMeshProUGUI assignedText;    // "Assigned Fire Bitlings: 3"
    [SerializeField] private TextMeshProUGUI topPerformerText;// "Top Performer: FLAREBYTE (Lv. 14)"

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

        // Lines
        if (hoursText != null)
            hoursText.text = $"Hours Supervised: {row.hoursSupervised}";

        if (materialsText != null)
            materialsText.text = $"Materials Smelted: {row.materialsProcessed:n0}";

        if (outputText != null)
            outputText.text = $"Output / hr: {row.outputPerHour}";

        if (assignedText != null)
        {
            string typeLabel = GetJobPrimaryTypeLabel(row.job);
            assignedText.text = $"Assigned {typeLabel} Bitlings: {row.assignedWorkers}";
        }

        if (topPerformerText != null)
        {
            if (!string.IsNullOrEmpty(row.topPerformerName) && row.topPerformerLevel > 0)
                topPerformerText.text = $"Top Performer: {row.topPerformerName.ToUpperInvariant()} (Lv. {row.topPerformerLevel})";
            else
                topPerformerText.text = "Top Performer: —";
        }

        // jobIconImage can be wired later with a sprite lookup based on JobType
    }

    private string GetJobPrimaryTypeLabel(JobType job)
    {
        // Just for flavor text in "Assigned X Bitlings"
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
}
