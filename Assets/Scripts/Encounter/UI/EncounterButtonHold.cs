using UnityEngine;
using UnityEngine.EventSystems;

public class EncounterButtonHold : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Hold duration to trigger AUTO toggle.")]
    public float holdSeconds = 0.6f;

    private bool  pressed;
    private float pressedAt;
    private bool  firedHold;

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    private bool IsIdleBattleUnlocked()
    {
        if (FeatureUnlockManager.I == null) return true; 
        // fail-safe in editor or if unlock system not loaded
        return FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic);
    }

    private void TriggerHaptic()
    {
        // Simple built-in mobile vibration.
        // Does nothing in Editor / PC.
    #if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
    #endif
    }

    // ─────────────────────────────────────────────────────────────
    // Pointer events
    // ─────────────────────────────────────────────────────────────
    public void OnPointerDown(PointerEventData eventData)
    {
        pressed   = true;
        firedHold = false;
        pressedAt = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pressed) return;

        // If we already fired the hold action, skip tap-start
        if (!firedHold)
        {
            // SAFETY CHECK: prevent encounter if no monsters on team
            var data = SaveManager.Data;
            if (data == null || data.team == null || data.team.Count == 0)
            {
                Debug.LogWarning("[EncounterButtonHold] Cannot start encounter — team is empty.");
                ResetPressState();
                return;
            }

            // Only allow valid monsters
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
                ResetPressState();
                return;
            }

            // Tap = Start Encounter
            if (EncounterManager.I == null)
            {
                Debug.LogWarning("[EncounterButtonHold] EncounterManager not present.");
                ResetPressState();
                return;
            }

            EncounterManager.I.RequestEncounterTap();
        }

        ResetPressState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ResetPressState();
    }

    private void OnDisable()
    {
        // Reset any partial press when the object is disabled
        ResetPressState();
    }

    // ─────────────────────────────────────────────────────────────
    // Update loop for long-press detection
    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (!pressed || firedHold) return;

        float heldFor = Time.unscaledTime - pressedAt;

        if (heldFor >= holdSeconds)
        {
            firedHold = true;
            pressed   = false;

            // HOLD = Toggle Auto Mode **only if unlocked**
            if (!IsIdleBattleUnlocked())
            {
                Debug.Log("[EncounterButtonHold] Auto mode is locked — unlock Idle Battles to enable this.");
                return;
            }

            // Haptic bump on successful auto toggle
            TriggerHaptic();

            EncounterManager.I?.ToggleAutoMode();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Internal resets
    // ─────────────────────────────────────────────────────────────
    private void ResetPressState()
    {
        pressed   = false;
        firedHold = false;
        pressedAt = 0f;
    }
}
