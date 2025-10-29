using UnityEngine;
using UnityEngine.UI;

public class JobSlotUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Image icon;
    [SerializeField] private Button button;  
    [SerializeField] private JobAssignPanelUI jobAssignPanelUI;
    [HideInInspector] public JobType job;
    [HideInInspector] public int slotIndex;

    public void SetEmpty(Sprite emptySprite, Color emptyColor)
    {
        if (!icon) return;
        icon.sprite = emptySprite;
        icon.color  = emptyColor;
        icon.preserveAspect = true;
    }

    public void SetWorker(Sprite workerSprite, Color filledColor)
    {
        if (!icon) return;
        icon.sprite = workerSprite;
        icon.color  = filledColor;
        icon.preserveAspect = true;
    }

    public void WireToPicker()
    {
        if (!button) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            // Open the job assignment panel
            if (jobAssignPanelUI)
                jobAssignPanelUI.Open(job, slotIndex);

        });
    }
    
}
