using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RewardPopupUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private TextMeshProUGUI creditsText;
    [SerializeField] private TextMeshProUGUI medkitsText;
    [SerializeField] private Button okButton;

    void Awake() { Hide(); }

    void OnEnable()
    {
        GameEvents.ShowRewardPopup += OnShow;
        if (okButton) okButton.onClick.AddListener(Hide);
    }

    void OnDisable()
    {
        GameEvents.ShowRewardPopup -= OnShow;
        if (okButton) okButton.onClick.RemoveListener(Hide);
    }

    private void OnShow(string title, string body, int credits, int medkits)
    {
        if (root) root.SetActive(true);
        if (titleText)  titleText.text  = string.IsNullOrEmpty(title) ? "Rewards" : title;
        if (bodyText)   bodyText.text   = body ?? "";
        if (creditsText)  creditsText.text  = credits   > 0 ? $"+{credits}"   : "";
        if (medkitsText)medkitsText.text= medkits > 0 ? $"+{medkits}" : "";
    }

    private void Hide()
    {
        if (root) root.SetActive(false);
    }
}
