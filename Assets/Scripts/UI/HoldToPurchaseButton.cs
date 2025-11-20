using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HoldToPurchaseButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Config")]
    [SerializeField] private float holdDuration = 1.5f; // seconds to confirm
    [SerializeField] private Image progressFill;        // optional radial/rect fill (0-1)

    [Header("Events")]
    public UnityEvent onHoldComplete;

    private bool _holding;
    private float _heldTime;

    void Update()
    {
        if (!_holding) return;

        _heldTime += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_heldTime / holdDuration);

        if (progressFill)
            progressFill.fillAmount = t;

        if (_heldTime >= holdDuration)
        {
            _holding = false;
            if (progressFill)
                progressFill.fillAmount = 1f;

            onHoldComplete?.Invoke();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _holding = true;
        _heldTime = 0f;
        if (progressFill)
            progressFill.fillAmount = 0f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        CancelHold();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        CancelHold();
    }

    private void CancelHold()
    {
        if (!_holding) return;
        _holding = false;
        _heldTime = 0f;
        if (progressFill)
            progressFill.fillAmount = 0f;
    }
}
