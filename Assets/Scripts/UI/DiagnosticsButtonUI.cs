using UnityEngine;
using UnityEngine.UI;

public class DiagnosticsButtonUI : MonoBehaviour
{
    public static DiagnosticsButtonUI I { get; private set; }

    [Header("Wires")]
    [Tooltip("The button GameObject that should be inactive until unlocked.")]
    [SerializeField] private GameObject diagnosticsButtonGO;

    [Tooltip("Optional: the Button component (for interactable toggles).")]
    [SerializeField] private Button diagnosticsButton;

    [Header("Behavior")]
    [SerializeField] private bool hideUntilUnlocked = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (diagnosticsButton == null && diagnosticsButtonGO != null)
            diagnosticsButton = diagnosticsButtonGO.GetComponent<Button>();

        ApplyFromSave("Awake");
    }

    void OnEnable() => ApplyFromSave("OnEnable");

    public void ApplyFromSave(string context = "")
    {
        bool unlocked = (SaveManager.Data != null) && SaveManager.Data.diagnosticsUnlocked;
        ApplyUnlockedState(unlocked);
    }

   public void ApplyUnlockedState(bool unlocked)
    {
      
        if (hideUntilUnlocked)
            diagnosticsButtonGO.SetActive(unlocked);

        if (diagnosticsButton != null)
            diagnosticsButton.interactable = unlocked;
    }

}
