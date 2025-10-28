using UnityEngine;
using UnityEngine.EventSystems;

public class EncounterButtonHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Hold duration to trigger AUTO toggle.")]
    public float holdSeconds = 0.6f;

    [Tooltip("Optional. If not set, the component will search in parents at runtime.")]
    [SerializeField] private EncounterPanelUI panel;

    bool pressed;
    float pressedAt;
    bool firedHold;

    void Awake()
    {
        // Auto-find the panel if not assigned
        if (!panel) panel = GetComponentInParent<EncounterPanelUI>(true);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        firedHold = false;
        pressedAt = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pressed) return;
        pressed = false;

        // If we already fired the hold action, don't also tap-start
        if (firedHold) return;

        // Prefer the panel handler; fallback to manager if panel missing
        if (panel)
        {
            // Call the public handler in EncounterPanelUI
            var method = panel.GetType().GetMethod("OnClickStartEncounter",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (method != null) method.Invoke(panel, null);
        }
        else
        {
            EncounterManager.I?.RequestEncounterTap();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pressed = false;
    }

    void Update()
    {
        if (!pressed || firedHold) return;

        if (Time.unscaledTime - pressedAt >= holdSeconds)
        {
            firedHold = true;
            pressed = false;

            // Prefer panel toggle; fallback to manager
            if (panel)
            {
                var method = panel.GetType().GetMethod("OnClickToggleAuto",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method != null) method.Invoke(panel, null);
            }
            else
            {
                EncounterManager.I?.ToggleAutoMode();
            }
        }
    }
}
