using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CheatCodePanelUI : MonoBehaviour
{
    public static CheatCodePanelUI I { get; private set; }

    [Header("Routing")]
    [SerializeField] private PanelId selfPanelId = PanelId.CheatCodes;

    [Header("Refs")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TextMeshProUGUI feedbackLabel;
    [SerializeField] private Button submitButton;
    [SerializeField] private Button closeButton;

    const float FADE_TIME = 0.12f;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (submitButton != null)
            submitButton.onClick.AddListener(OnSubmitPressed);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (feedbackLabel != null)
            feedbackLabel.text = string.Empty;
    }

    void OnEnable()
    {
        // Clear field and auto-focus so the keyboard pops up on mobile.
        if (inputField != null)
        {
            inputField.text = string.Empty;
            inputField.Select();
            inputField.ActivateInputField();
        }

        if (feedbackLabel != null)
            feedbackLabel.text = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────
    // Open / Close
    // ─────────────────────────────────────────────────────────────
    public void Open()
    {
        // UIManager-aware open
        if (UIManager.I != null && selfPanelId != PanelId.None)
        {
            UIManager.I.Show(selfPanelId);
        }
        

        if (inputField != null)
        {
            inputField.text = string.Empty;
            inputField.Select();
            inputField.ActivateInputField();
        }

        if (feedbackLabel != null)
            feedbackLabel.text = string.Empty;
    }

    public void Close()
    {
        if (UIManager.I != null && selfPanelId != PanelId.None)
        {
            UIManager.I.Hide(selfPanelId);
        }
    }

    // Called by Submit button or via OnEndEdit (Enter key)
    public void OnSubmitPressed()
    {
        if (inputField == null)
            return;

        string raw = inputField.text;

        if (CheatCodeManager.I == null)
        {
            SetFeedback("Cheat manager missing.");
            return;
        }

        if (CheatCodeManager.I.TryApplyCheat(raw, out string msg))
        {
            SetFeedback(msg);
            inputField.text = string.Empty;
            inputField.Select();
            inputField.ActivateInputField();
        }
        else
        {
            SetFeedback(msg);
        }
    }

    void SetFeedback(string msg)
    {
        if (feedbackLabel != null)
            feedbackLabel.text = msg ?? string.Empty;
    }
}
