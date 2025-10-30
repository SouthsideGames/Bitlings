using UnityEngine;
using UnityEngine.EventSystems;

public class EncounterButtonHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Hold duration to trigger AUTO toggle.")]
    public float holdSeconds = 0.6f;

    private bool pressed;
    private float pressedAt;
    private bool firedHold;

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

        // If we already fired the hold action, skip tap-start
        if (firedHold) return;

        // SAFETY CHECK: prevent encounter if no monsters on team
        var data = SaveManager.Data;
        if (data == null || data.team == null || data.team.Count == 0)
        {
            Debug.LogWarning("[EncounterButtonHold] Cannot start encounter — team is empty.");
            return;
        }

        // Count valid monsters only (non-null entries with a monsterId)
        bool hasValidMonster = false;
        for (int i = 0; i < data.team.Count; i++)
        {
            var entry = data.team[i];
            if (entry != null && !string.IsNullOrEmpty(entry.monsterId))
            {
                hasValidMonster = true;
                break;
            }
        }

        if (!hasValidMonster)
        {
            Debug.LogWarning("[EncounterButtonHold] Cannot start encounter — no valid monsters assigned.");
            return;
        }

        // Tap = Start Encounter
        if (EncounterManager.I == null)
        {
            Debug.LogWarning("[EncounterButtonHold] EncounterManager not present.");
            return;
        }
        EncounterManager.I.RequestEncounterTap();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pressed = false;
    }

    private void OnDisable()
    {
        // Reset any partial press when the object is disabled
        pressed = false;
        firedHold = false;
    }

    void Update()
    {
        if (!pressed || firedHold) return;

        if (Time.unscaledTime - pressedAt >= holdSeconds)
        {
            firedHold = true;
            pressed = false;

            // Hold = Toggle Auto Mode
            EncounterManager.I?.ToggleAutoMode();
        }
    }
}
