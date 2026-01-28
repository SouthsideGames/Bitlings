using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DiagnosticsOverlayUI : MonoBehaviour
{
    public static DiagnosticsOverlayUI I { get; private set; }

    [Header("Wires")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private Button closeButton;

    [Header("Behavior")]
    [SerializeField, Min(0.05f)] private float refreshSeconds = 0.25f;
    [SerializeField] private bool autoScrollToBottom = true;

    float _t;
    bool _visible;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }

        SetVisible(false, instant: true);
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    void Update()
    {
        if (!_visible) return;

        _t += Time.unscaledDeltaTime;
        if (_t >= refreshSeconds)
        {
            _t = 0f;
            Refresh("Tick");
        }
    }

    public void Toggle()
    {
        if (_visible) Hide();
        else Show();
    }

    public void Show()
    {
        SetVisible(true, instant: true);
        Refresh("Show");
    }

    public void Hide()
    {
        SetVisible(false, instant: true);
    }

    public void Refresh(string context = "")
    {
        if (!text) return;

        text.text = DiagnosticsSnapshot.Build(context);

        if (autoScrollToBottom && scrollRect)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    void SetVisible(bool on, bool instant)
    {
        _visible = on;
        _t = 0f;

        if (group)
        {
            group.alpha = on ? 1f : 0f;
            group.interactable = on;
        }
        else
            gameObject.SetActive(on);
    }
}
