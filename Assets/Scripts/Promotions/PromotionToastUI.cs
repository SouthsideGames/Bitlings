using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Toast that appears when the player's promotion rank increases.
/// Two buttons: Dismiss (closes the toast) and View Ranks (opens
/// PlayerDossier on page 7).
/// </summary>
public sealed class PromotionToastUI : MonoBehaviour
{
    public static PromotionToastUI I { get; private set; }

    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button dismissButton;
    [SerializeField] private Button viewRanksButton;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float fadeInSeconds  = 0.18f;
    [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.14f;

    private bool _visible;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();

        if (dismissButton)   dismissButton.onClick.AddListener(OnDismissClicked);
        if (viewRanksButton) viewRanksButton.onClick.AddListener(OnViewRanksClicked);

        GameEvents.PromotionRankChanged += HandleRankChanged;

        HideImmediate();
    }

    private void OnDestroy()
    {
        GameEvents.PromotionRankChanged -= HandleRankChanged;
        if (dismissButton)   dismissButton.onClick.RemoveListener(OnDismissClicked);
        if (viewRanksButton) viewRanksButton.onClick.RemoveListener(OnViewRanksClicked);
        if (I == this) I = null;
    }

    private void HandleRankChanged(int oldRank, int newRank)
    {
        Show($"Promoted to Rank {newRank}!");
    }

    public void Show(string message)
    {
        if (_visible) return;
        _visible = true;

        if (messageText) messageText.text = message;

        gameObject.SetActive(true);

        if (canvasGroup)
        {
            LeanTween.cancel(canvasGroup.gameObject);
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            LeanTween.alphaCanvas(canvasGroup, 1f, fadeInSeconds)
                     .setEaseOutCubic()
                     .setIgnoreTimeScale(true)
                     .setOnComplete(() =>
                     {
                         if (canvasGroup)
                         {
                             canvasGroup.interactable = true;
                             canvasGroup.blocksRaycasts = true;
                         }
                     });
        }
    }

    private void OnDismissClicked()
    {
        FadeOut();
    }

    private void OnViewRanksClicked()
    {
        FadeOut();

        // Set pending page before the panel is shown (page 7 = Ranks)
        PlayerDossierPanelUI.SetPendingPage(7);

        if (UIManager.I != null)
        {
            UIManager.I.CloseAll();
            UIManager.I.Show(PanelId.PlayerDossier);
        }
    }

    private void FadeOut()
    {
        if (!_visible) return;

        if (canvasGroup)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            LeanTween.cancel(canvasGroup.gameObject);
            LeanTween.alphaCanvas(canvasGroup, 0f, fadeOutSeconds)
                     .setEaseInCubic()
                     .setIgnoreTimeScale(true)
                     .setOnComplete(() =>
                     {
                         _visible = false;
                         gameObject.SetActive(false);
                     });
        }
        else
        {
            _visible = false;
            gameObject.SetActive(false);
        }
    }

    private void HideImmediate()
    {
        _visible = false;
        if (canvasGroup)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }
}
