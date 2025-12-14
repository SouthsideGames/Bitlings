using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HoldToConfirm : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerExitHandler
{
    [Header("Hold Settings")]
    [SerializeField] private float holdDuration = 1.25f;

    [Header("Animation")]
    [SerializeField] private float scaleUp = 1.12f;
    [SerializeField] private float pulseSpeed = 0.25f;

    [Header("Wiring")]
    [SerializeField] private Button button;
    [SerializeField] private PackDetailPanelUI packPanel;

    private int _holdTweenId = -1;
    private int _pulseTweenId = -1;
    private bool _completed;

    private Vector3 _startScale;

    private void Awake()
    {
        if (!button)
            button = GetComponent<Button>();

        _startScale = transform.localScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_completed || !button.interactable)
            return;

        StartHold();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        CancelHold();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        CancelHold();
    }

    private void StartHold()
    {
        _completed = false;

        // Subtle pulse while holding
        _pulseTweenId = LeanTween.scale(gameObject, _startScale * scaleUp, pulseSpeed)
            .setEaseInOutSine()
            .setLoopPingPong()
            .id;

        // Hold timer
        _holdTweenId = LeanTween.delayedCall(gameObject, holdDuration, OnHoldComplete).id;
    }

    private void CancelHold()
    {
        if (_completed)
            return;

        LeanTween.cancel(_holdTweenId);
        LeanTween.cancel(_pulseTweenId);

        transform.localScale = _startScale;
    }

    private void OnHoldComplete()
    {
        _completed = true;

        LeanTween.cancel(_pulseTweenId);
        transform.localScale = _startScale;

        if (packPanel != null)
            packPanel.PurchaseCurrentPack();
    }
}
