using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [TextArea]
    public string message;

    [TextArea]
    [Tooltip("Optional smaller secondary line shown under the main message.")]
    public string subtitle;

    [Header("Long Press Settings")]
    [Tooltip("Hold this long (in seconds) before showing the tooltip.")]
    public float holdTime = 0.45f;

    private bool _isPointerDown = false;
    private float _pointerDownTimer = 0f;

    void Update()
    {
        if (_isPointerDown)
        {
            _pointerDownTimer += Time.unscaledDeltaTime;

            if (_pointerDownTimer >= holdTime)
            {
                ShowTooltip();
                Reset();
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPointerDown = true;
        _pointerDownTimer = 0f;
    }

    public void OnPointerUp(PointerEventData eventData) => Reset();
    public void OnPointerExit(PointerEventData eventData) => Reset();

    private void Reset()
    {
        _isPointerDown = false;
        _pointerDownTimer = 0f;
    }

    private void ShowTooltip()
    {
        if (TooltipUI.I == null)
            return;

        if (string.IsNullOrEmpty(message))
            return;

        // Build the final combined tooltip text
        string finalText = string.IsNullOrEmpty(subtitle)
            ? message
            : $"{message}\n<color=#CCCCCC>{subtitle}</color>";

        TooltipUI.I.Show(finalText);
    }
}
