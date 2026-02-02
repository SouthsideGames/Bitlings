using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class MainMenuSeedLabel : MonoBehaviour, IPointerClickHandler
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI seedLabel;

    [Header("Format")]
    [SerializeField] private string prefix = "SEED:";
    [SerializeField] private bool includeModeTag = false;    
    [SerializeField] private bool copyFullText = true;        

    [Header("Copy Feedback (optional)")]
    [SerializeField] private bool logOnCopy = true;
    [SerializeField] private string copiedMessage = "Seed copied.";

    void OnEnable()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (seedLabel == null)
            return;

        SeedService.ApplyGlobalSeedForSession();

        int seed = SeedService.ActiveSeed;

        if (seed == 0)
        {
            seedLabel.text = $"{prefix} ----";
            return;
        }

        if (includeModeTag)
        {
            string tag = SeedService.ActiveMode.ToString().ToUpperInvariant();
            seedLabel.text = $"{prefix} {seed} ({tag})";
        }
        else
        {
            seedLabel.text = $"{prefix} {seed}";
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        int seed = SeedService.ActiveSeed;
        if (seed == 0)
            return;

        string toCopy = copyFullText ? $"{prefix} {seed}" : seed.ToString();

        GUIUtility.systemCopyBuffer = toCopy;

        if (logOnCopy)
            Debug.Log($"[MainMenuSeedLabel] {copiedMessage} ({toCopy})");

        GameEvents.RaiseToast("Copied seed!");
    }
}
