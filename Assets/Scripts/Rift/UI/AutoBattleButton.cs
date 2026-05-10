using UnityEngine;
using UnityEngine.EventSystems;

public class AutoBattleButton : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Hold duration to trigger AUTO toggle.")]
    public float holdSeconds = 0.6f;

    private bool  pressed;
    private float pressedAt;
    private bool  firedHold;

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

            if (!IsIdleBattleUnlocked())
            {
                DevLog.Log("[RiftButtonHold] Auto mode is locked — unlock Idle Battles to enable this.");
                return;
            }

            TriggerHaptic();

            RiftManager.I?.ToggleAutoMode();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    private bool IsIdleBattleUnlocked()
    {
        if (FeatureUnlockManager.I == null) return false; 
        return FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic);
    }

    private void TriggerHaptic()
    {

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
        if (ExecutiveTrialRuntime.IsActive) return;

        if (!pressed) return;

        if (!firedHold)
        {
            var data = SaveManager.Data;
            if (data == null || data.team == null || data.team.Count == 0)
            {
                Debug.LogWarning("[AutoBattleButton] Cannot start rift — team is empty.");
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
                Debug.LogWarning("[AutoBattleButton] Cannot start rift — no valid monsters assigned.");
                ResetPressState();
                return;
            }

            // Tap = Start Rift
            if (RiftManager.I == null)
            {
                Debug.LogWarning("[AutoBattleButton] RiftManager not present.");
                ResetPressState();
                return;
            }

            RiftManager.I.RequestRiftTap();
        }

        ResetPressState();
    }

    public void OnPointerExit(PointerEventData eventData) => ResetPressState();
    private void OnDisable() => ResetPressState();


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
