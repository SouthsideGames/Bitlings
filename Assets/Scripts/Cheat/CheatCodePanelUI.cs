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

    float _nextTickTime;
    bool _wasLocked;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (submitButton != null)
        {
            submitButton.onClick.RemoveAllListeners();
            submitButton.onClick.AddListener(OnSubmitPressed);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Close);
        }

        if (feedbackLabel != null)
            feedbackLabel.text = string.Empty;
    }

    void OnEnable()
    {
        RefreshLockStateUI(force: true);
    }

    void Update()
    {
        if (!isActiveAndEnabled) return;
        if (CheatCodeManager.I == null) return;

        // Only tick while locked, about 1/sec
        if (Time.unscaledTime >= _nextTickTime)
        {
            _nextTickTime = Time.unscaledTime + 1f;
            if (CheatCodeManager.I.IsLocked(out _))
                RefreshLockStateUI(force: false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Open / Close
    // ─────────────────────────────────────────────────────────────
    public void Open()
    {
        if (UIManager.I != null && selfPanelId != PanelId.None)
            UIManager.I.Show(selfPanelId);

        RefreshLockStateUI(force: true);
    }

    public void Close()
    {
        if (UIManager.I != null && selfPanelId != PanelId.None)
            UIManager.I.Hide(selfPanelId);
    }

    void RefreshLockStateUI(bool force)
    {
        bool locked = (CheatCodeManager.I != null) && CheatCodeManager.I.IsLocked(out _);

        if (!force && locked == _wasLocked && locked == false)
            return;

        _wasLocked = locked;

        if (locked)
        {
            // Locked: show countdown message, disable input + submit
            SetFeedback(CheatCodeManager.I.GetLockedMessage());

            if (inputField != null)
            {
                inputField.text = string.Empty;
                inputField.interactable = false;
            }

            if (submitButton != null)
                submitButton.interactable = false;
        }
        else
        {
            // Unlocked: enable input, clear feedback, focus input
            if (feedbackLabel != null)
                feedbackLabel.text = string.Empty;

            if (inputField != null)
            {
                inputField.interactable = true;
                inputField.text = string.Empty;
                inputField.Select();
                inputField.ActivateInputField();
            }

            if (submitButton != null)
                submitButton.interactable = true;
        }
    }

    // Called by Submit button
    public void OnSubmitPressed()
    {
        if (inputField == null)
            return;

        if (CheatCodeManager.I == null)
        {
            SetFeedback("Cheat manager missing.");
            return;
        }

        // If locked, ignore submit
        if (CheatCodeManager.I.IsLocked(out _))
        {
            RefreshLockStateUI(force: true);
            return;
        }

        string raw = inputField.text;

        bool ok = CheatCodeManager.I.TryApplyCheat(raw, out string msg);
        SetFeedback(msg);

        // If the attempt caused a lock, close the panel (your requirement)
        if (CheatCodeManager.I.IsLocked(out _))
        {
            // Ensure UI shows final lockdown message briefly (optional).
            // If you want it to close instantly, you can remove this line.
            RefreshLockStateUI(force: true);

            Close();
            return;
        }

        if (ok)
        {
            inputField.text = string.Empty;
            inputField.Select();
            inputField.ActivateInputField();
        }
        else
        {
            // On invalid (but not locked yet), keep input enabled for next try
            inputField.Select();
            inputField.ActivateInputField();
        }
    }

    void SetFeedback(string msg)
    {
        if (feedbackLabel != null)
            feedbackLabel.text = msg ?? string.Empty;
    }
}
