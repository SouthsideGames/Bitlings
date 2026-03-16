using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class JobMonsterEntryUI : MonoBehaviour
{
    [Header("Wiring")]
    public Button button;
    public Image icon;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI fatigueInfoText;
    public Image typeIcon;

    [Header("Assignment UI (Optional)")]
    [SerializeField] private GameObject assignedRoot;
    public TextMeshProUGUI assignedText;

    [Header("Rest / Cooldown UI (Optional)")]
    [Tooltip("Optional root shown while resting/cooling down.")]
    [SerializeField] private GameObject restRoot;
    [Tooltip("Optional small text shown while resting/cooling down (e.g., '3h 46m').")]
    [SerializeField] private TextMeshProUGUI restTimeText;

    [Header("Tooltip (Optional)")]
    [Tooltip("If assigned, long-press will show details (rest/cooldown/assignment).")]
    [SerializeField] private TooltipTrigger tooltip;

    void Awake()
    {
        // Hide once at creation so prefabs don't default-show it.
        ClearAssignmentUI();
    }

    public void SetFatigued(bool isFatigued, string etaText = null)
    {
        if (button) button.interactable = !isFatigued;

        // Also drive the optional rest UI (some screens treat "fatigued" as "resting").
        SetRestingUI(isFatigued, etaText);
    }

    /// <summary>
    /// Preferred API for the JobAssign panel: show resting root + timer.
    /// </summary>
    public void SetResting(bool isResting, string etaText, float progress01)
    {
        if (button) button.interactable = !isResting;

        SetRestingUI(isResting, etaText);
    }

    public void SetTooltip(string title, string subtitle)
    {
        if (!tooltip) return;
        tooltip.message = title;
        tooltip.subtitle = subtitle;
    }

    public void SetFatigueInfo(float cooldownHours)
    {
        if (!fatigueInfoText) return;

        if (cooldownHours <= 0f)
        {
            fatigueInfoText.text = "";
            fatigueInfoText.gameObject.SetActive(false);
            return;
        }

        fatigueInfoText.gameObject.SetActive(true);
        fatigueInfoText.text = $"Fatigue: rests for {FormatHoursAndMinutes(cooldownHours)}";
    }

    private void SetRestingUI(bool isResting, string etaText)
    {
        if (restRoot) restRoot.SetActive(isResting);

        if (restTimeText)
        {
            restTimeText.gameObject.SetActive(isResting);
            restTimeText.text = isResting
                ? (string.IsNullOrEmpty(etaText) ? "Resting.." : $"Resting.. {etaText}")
                : "";
        }
    }

    public void SetAssignment(JobType job, int slotIndex, bool hide)
    {
        if (hide || job == JobType.None)
        {
            ClearAssignmentUI();
            return;
        }

        if (assignedRoot) assignedRoot.SetActive(true);

        if (!assignedText) return;

        assignedText.gameObject.SetActive(true);
        assignedText.text = $"Assigned: {job} (Slot {slotIndex + 1})";

        if (tooltip)
        {
            tooltip.message = "Assigned";
            tooltip.subtitle = assignedText.text;
        }
    }

    private void ClearAssignmentUI()
    {
        if (assignedRoot) assignedRoot.SetActive(false);

        if (!assignedText) return;
        assignedText.text = "";
        assignedText.gameObject.SetActive(false);
    }

    private string FormatHoursAndMinutes(float hours)
    {
        int totalMinutes = Mathf.Max(1, Mathf.RoundToInt(hours * 60f));
        int wholeHours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        if (wholeHours <= 0) return $"{minutes}m";
        if (minutes <= 0) return wholeHours == 1 ? "1h" : $"{wholeHours}h";
        return $"{wholeHours}h {minutes}m";
    }
}
