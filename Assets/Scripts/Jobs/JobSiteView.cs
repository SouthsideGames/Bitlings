using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JobSiteView : MonoBehaviour
{
    [SerializeField] private JobType site;
    [SerializeField] private GameObject rootToToggle;

    [SerializeField] private Toggle allowReliefToggle;
    [SerializeField] private TextMeshProUGUI fatigueText;

    void Awake()
    {
        if (!rootToToggle) rootToToggle = gameObject;
    }

    void Start()
    {
        if (allowReliefToggle)
        {
            allowReliefToggle.onValueChanged.AddListener(v =>
            {
                var st = GetRuntimeState(site);
                if (st != null) st.allowClinicRelief = v;
            });
        }
        Refresh();
    }

    void OnEnable()
    {
        GameEvents.OnJobsChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        GameEvents.OnJobsChanged -= Refresh;
    }

    private JobSiteState GetRuntimeState(JobType job)
    {
        if (JobManager.I == null) return null;
        foreach (var st in JobManager.I.States)
            if (st != null && st.config != null && st.config.jobType == job) return st;
        return null;
    }

    public void Refresh()
    {
        bool unlocked = SaveManager.Data != null
                     && SaveManager.Data.unlockedJobSites != null
                     && SaveManager.Data.unlockedJobSites.Contains(site);
        rootToToggle.SetActive(unlocked);

        var st = GetRuntimeState(site);
        if (allowReliefToggle && st != null) allowReliefToggle.isOn = st.allowClinicRelief;

        if (fatigueText)
            fatigueText.text = $"{Mathf.RoundToInt((st?.fatigue01 ?? 0f) * 100f)}%";
    }

    
}
