using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles tap-to-rift on the Rift button.
/// Hold-to-toggle-auto is now handled entirely by RiftButtonGuard.
/// </summary>
// CHANGED: removed hold detection and auto-toggle - now owned by RiftButtonGuard to prevent double-trigger
public class AutoBattleButton : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private bool _pressed;
    private bool _firedHold; // kept for compatibility; RiftButtonGuard sets suppressNextClick

    public void OnPointerDown(PointerEventData eventData)
    {
        _pressed = true;
        _firedHold = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // CHANGED: tap-to-rift removed - PanelButtonUI opens the rift panel on tap.
        // Starting a battle is the player's choice inside the panel, not on the home button.
        ResetPressState();
    }

    public void OnPointerExit(PointerEventData eventData) => ResetPressState();
    private void OnDisable() => ResetPressState();

    private void ResetPressState()
    {
        _pressed = false;
        _firedHold = false;
    }

}
