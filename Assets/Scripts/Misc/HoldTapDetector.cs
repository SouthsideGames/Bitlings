using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class HoldTapDetector : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IDragHandler
{
    [Header("Timing")]
    public float holdThreshold = 0.4f;

    public bool useUnscaledTime = true;

    public bool triggerHoldOnThreshold = false;

    [Header("Input")]
    public float maxTapMovement = 10f;

    [Header("Events (optional)")]
    public UnityEvent onTap;
    public UnityEvent onHold;

    private Action _onTapCb;
    private Action _onHoldCb;

    private bool _isDown;
    private bool _holdFired;
    private float _downTime;
    private Vector2 _downPos;

    public void SetCallbacks(Action onTap, Action onHold)
    {
        _onTapCb = onTap;
        _onHoldCb = onHold;
    }

    private void Update()
    {
        if (!_isDown) return;

        _downTime += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (triggerHoldOnThreshold && !_holdFired && _downTime >= holdThreshold)
        {
            _holdFired = true;
            FireHold();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isDown = true;
        _holdFired = false;
        _downTime = 0f;
        _downPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData) { }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_isDown) return;

        bool isHold = _downTime >= holdThreshold || _holdFired;
        bool withinTapMove = Vector2.Distance(_downPos, eventData.position) <= maxTapMovement;

        _isDown = false;

        if (isHold)
        {
            if (!_holdFired) FireHold();
        }
        else
        {
            if (withinTapMove) FireTap();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ResetState();
    }

    private void ResetState()
    {
        _isDown = false;
        _holdFired = false;
        _downTime = 0f;
    }

    private void FireTap()
    {
        onTap?.Invoke();
        _onTapCb?.Invoke();
    }

    private void FireHold()
    {
        onHold?.Invoke();
        _onHoldCb?.Invoke();
    }

    private void OnDisable()
    {
        ResetState();
    }
}