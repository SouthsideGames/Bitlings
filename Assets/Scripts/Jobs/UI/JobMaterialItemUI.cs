using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using UnityEngine.EventSystems;

public class JobMaterialItemUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI xpText;
    [SerializeField] private Button minusBtn;
    [SerializeField] private Button plusBtn;

    [Header("Hold-to-Repeat (QoL)")]
    [Tooltip("Seconds to wait before auto-repeat begins.")]
    [SerializeField] private float holdInitialDelay = 0.35f;

    [Tooltip("Repeats per second at the start of holding.")]
    [SerializeField] private float holdStartRate = 8f;

    [Tooltip("Repeats per second after ramping up.")]
    [SerializeField] private float holdMaxRate = 28f;

    [Tooltip("Seconds to ramp from start rate to max rate.")]
    [SerializeField] private float holdRampSeconds = 1.25f;

    // Colors for feedback
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color addColor = new Color(0.25f, 1f, 0.25f);  
    [SerializeField] private Color removeColor = new Color(1f, 0.3f, 0.3f); 

    // Local model
    private Sprite _icon;
    private string _jobName;
    private int _level;
    private int _maxLevel;
    private int _baseCurXP;   
    private int _maxXP;
    private int _pending;
    private Func<bool> _canSpendOne;
    private Action<int> _onDeltaChanged;

    private HoldRepeatProxy _minusHold;
    private HoldRepeatProxy _plusHold;

    public void Setup(
        Sprite iconSprite,
        string jobDisplayName,
        int level,
        int maxLevel,
        int currentXP,
        int maxXPForLevel,
        Func<bool> canSpendOneMaterial,
        Action<int> onDeltaChanged,
        Action requestRefresh)
    {
        _icon = iconSprite;
        _jobName = jobDisplayName;
        _level = level;
        _maxLevel = maxLevel;
        _baseCurXP = Mathf.Max(0, currentXP);
        _maxXP = Mathf.Max(1, maxXPForLevel);
        _pending = 0;
        _canSpendOne = canSpendOneMaterial;
        _onDeltaChanged = onDeltaChanged;

        if (icon) icon.sprite = _icon;
        if (nameText) nameText.text = _jobName;

        WireButtons(requestRefresh);
        RefreshVisuals();
    }

    public int Pending => _pending;
    public int PendingCurXP => Mathf.Clamp(_baseCurXP + _pending, 0, _maxXP);

    void WireButtons(Action requestRefresh)
    {
        if (minusBtn)
        {
            minusBtn.onClick.RemoveAllListeners();
            minusBtn.onClick.AddListener(() => TryMinus(requestRefresh, playAudio: true));

            _minusHold = EnsureHoldProxy(minusBtn, () => TryMinus(requestRefresh, playAudio: false));
            ApplyHoldTuning(_minusHold);
        }

        if (plusBtn)
        {
            plusBtn.onClick.RemoveAllListeners();
            plusBtn.onClick.AddListener(() => TryPlus(requestRefresh, playAudio: true));

            _plusHold = EnsureHoldProxy(plusBtn, () => TryPlus(requestRefresh, playAudio: false));
            ApplyHoldTuning(_plusHold);
        }
    }

    private void TryMinus(Action requestRefresh, bool playAudio)
    {
        if (_level >= _maxLevel) return;
        if (PendingCurXP <= 0) return;

        _pending -= 1;
        _onDeltaChanged?.Invoke(-1);
        RefreshVisuals();
        requestRefresh?.Invoke();

        if (playAudio && AudioManager.I) AudioManager.I.PlayClick();
    }

    private void TryPlus(Action requestRefresh, bool playAudio)
    {
        if (_level >= _maxLevel) return;
        if (PendingCurXP >= _maxXP) return;
        if (_canSpendOne != null && !_canSpendOne()) return;

        _pending += 1;
        _onDeltaChanged?.Invoke(+1);
        RefreshVisuals();
        requestRefresh?.Invoke();

        if (playAudio && AudioManager.I) AudioManager.I.PlayClick();
    }

    private HoldRepeatProxy EnsureHoldProxy(Button btn, Action onRepeat)
    {
        if (!btn) return null;

        var go = btn.gameObject;
        var proxy = go.GetComponent<HoldRepeatProxy>();
        if (!proxy)
            proxy = go.AddComponent<HoldRepeatProxy>();

        proxy.SetRepeatAction(onRepeat);
        return proxy;
    }

    private void ApplyHoldTuning(HoldRepeatProxy proxy)
    {
        if (!proxy) return;
        proxy.initialDelay = Mathf.Max(0f, holdInitialDelay);
        proxy.startRatePerSecond = Mathf.Max(0.1f, holdStartRate);
        proxy.maxRatePerSecond = Mathf.Max(proxy.startRatePerSecond, holdMaxRate);
        proxy.rampSeconds = Mathf.Max(0.01f, holdRampSeconds);
    }

    public void RefreshVisuals()
    {
        if (levelText)
            levelText.text = (_level >= _maxLevel) ? "MAX" : $"L{_level}";

        int cur = PendingCurXP;
        if (xpText)
        {
            xpText.text = $"{cur}/{_maxXP} XP";
            if (_pending > 0)      xpText.color = addColor;
            else if (_pending < 0) xpText.color = removeColor;
            else                   xpText.color = normalColor;
        }

        bool atMaxLevel = _level >= _maxLevel;
        if (minusBtn) minusBtn.interactable = !atMaxLevel && (cur > 0);
        if (plusBtn)  plusBtn.interactable  = !atMaxLevel && (cur < _maxXP);
    }


    public void SetLevelAndCaps(int level, int maxXPForLevel)
    {
        _level = level;
        _maxXP = Mathf.Max(1, maxXPForLevel);
        _pending = 0; 
        _baseCurXP = 0;
        RefreshVisuals();
    }

    /// <summary>
    /// Lightweight press-and-hold repeater. Added automatically to plus/minus buttons.
    /// Uses an accelerating repeat rate to make large adjustments fast.
    /// </summary>
    private sealed class HoldRepeatProxy : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [NonSerialized] public float initialDelay = 0.35f;
        [NonSerialized] public float startRatePerSecond = 8f;
        [NonSerialized] public float maxRatePerSecond = 28f;
        [NonSerialized] public float rampSeconds = 1.25f;

        private Action _repeat;
        private Coroutine _co;
        private bool _held;

        public void SetRepeatAction(Action repeat)
        {
            _repeat = repeat;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_held) return;
            _held = true;

            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(RepeatCo());
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _held = false;
            if (_co != null) { StopCoroutine(_co); _co = null; }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // If finger/mouse leaves the button, stop repeating to avoid unintended changes.
            OnPointerUp(eventData);
        }

        private IEnumerator RepeatCo()
        {
            float t0 = Time.unscaledTime;

            // Delay before starting repeat.
            if (initialDelay > 0f)
                yield return new WaitForSecondsRealtime(initialDelay);

            while (_held)
            {
                _repeat?.Invoke();

                float heldFor = Mathf.Max(0f, Time.unscaledTime - t0);
                float lerp = (rampSeconds <= 0.001f) ? 1f : Mathf.Clamp01(heldFor / rampSeconds);
                float rate = Mathf.Lerp(startRatePerSecond, maxRatePerSecond, lerp);
                float interval = 1f / Mathf.Max(0.1f, rate);
                yield return new WaitForSecondsRealtime(interval);
            }
        }
    }

}
