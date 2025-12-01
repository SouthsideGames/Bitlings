using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EncounterButtonHold : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Hold duration to trigger AUTO toggle.")]
    public float holdSeconds = 0.6f;

    [Header("Hold Ring (optional)")]
    [Tooltip("Radial image that fills while holding for AUTO.")]
    [SerializeField] private Image holdFillImage;

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

    // ─────────────────────────────────────────────────────────────
    // Pointer events
    // ─────────────────────────────────────────────────────────────
    public void OnPointerDown(PointerEventData eventData)
    {
        pressed   = true;
        firedHold = false;
        pressedAt = Time.unscaledTime;

        if (holdFillImage)
        {
            holdFillImage.gameObject.SetActive(true);
            holdFillImage.fillAmount = 0f;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        bool wasPressed = pressed;
        pressed = false;

        // PointerUp might fire after we already reset state; just clear ring and bail.
        if (!wasPressed)
        {
            ResetFillOnly();
            return;
        }

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

        // Reset state after tap or hold release
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

        // Update radial fill
        if (holdFillImage && holdSeconds > 0.01f)
        {
            float t = Mathf.Clamp01(heldFor / holdSeconds);
            holdFillImage.fillAmount = t;
        }

        if (heldFor >= holdSeconds)
        {
            firedHold = true;
            pressed   = false;

            // HOLD = Toggle Auto Mode **only if unlocked**
            if (!IsIdleBattleUnlocked())
            {
                Debug.Log("[EncounterButtonHold] Auto mode is locked — unlock Idle Battles to enable this.");
                ResetFillOnly();
                return;
            }

            EncounterManager.I?.ToggleAutoMode();
            ResetFillOnly(); // keep firedHold true so this release doesn't fire tap
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
        ResetFillOnly();
    }

    private void ResetFillOnly()
    {
        if (holdFillImage)
        {
            holdFillImage.fillAmount = 0f;
            holdFillImage.gameObject.SetActive(false);
        }
    }
}
