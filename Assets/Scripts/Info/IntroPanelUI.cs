using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tap-to-progress intro panel for Bitlings.
/// Attach this to your Intro panel GameObject.
/// </summary>
public class IntroPanelUI : MonoBehaviour
{
    [System.Serializable]
    public class IntroSlide
    {
        public string header;
        [TextArea(3, 6)] public string body;
    }

    [Header("UI Refs")]
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private TextMeshProUGUI pageCounterText;

    [Tooltip("Full-screen/tap area button. OnClick -> NextSlide()")]
    [SerializeField] private Button tapAreaButton;

    [Tooltip("Optional Skip button. OnClick -> SkipIntro()")]
    [SerializeField] private Button skipButton;

    [Header("Slides")]
    [SerializeField] private IntroSlide[] slides;

    [Header("Flow")]
    [Tooltip("Panel to show after the intro is finished (e.g. Starter Select).")]
    [SerializeField] private PanelId nextPanelId;

    [Header("Animation")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.15f;

    private int _currentIndex;
    private bool _isAnimating;

    // Simple PlayerPrefs flag so we only show intro once per profile.
    private const string INTRO_SEEN_KEY = "bitlings_intro_seen_v1";

    private void Awake()
    {
        if (tapAreaButton != null)
            tapAreaButton.onClick.AddListener(OnTapArea);

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkip);
    }

    private void OnEnable()
    {
        StartIntro();
    }

    // ─────────────────────────────────────────────────────────────
    // Public entry point from other scripts (optional)
    // ─────────────────────────────────────────────────────────────
    public static bool HasSeenIntro()
    {
        return PlayerPrefs.GetInt(INTRO_SEEN_KEY, 0) == 1;
    }

    public static void MarkIntroSeen()
    {
        PlayerPrefs.SetInt(INTRO_SEEN_KEY, 1);
        PlayerPrefs.Save();
    }

    // ─────────────────────────────────────────────────────────────
    // Intro flow
    // ─────────────────────────────────────────────────────────────
    public void StartIntro()
    {
        if (slides == null || slides.Length == 0)
        {
            Debug.LogWarning("[IntroPanelUI] No slides configured. Skipping intro.");
            FinishIntro();
            return;
        }

        _currentIndex = 0;
        _isAnimating = false;

        // Make sure panel is visible
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            rootGroup.interactable = true;
            rootGroup.blocksRaycasts = true;
            LeanTween.alphaCanvas(rootGroup, 1f, fadeDuration).setIgnoreTimeScale(true);
        }

        ApplySlideInstant();
    }

    private void OnTapArea()
    {
        if (_isAnimating) return;

        if (_currentIndex < slides.Length - 1)
        {
            _currentIndex++;
            ApplySlideWithFade();
        }
        else
        {
            FinishIntro();
        }
    }

    private void OnSkip()
    {
        if (_isAnimating) return;
        FinishIntro();
    }

    // ─────────────────────────────────────────────────────────────
    // Slide visuals
    // ─────────────────────────────────────────────────────────────
    private void ApplySlideInstant()
    {
        var slide = slides[_currentIndex];

        if (headerText != null)
            headerText.text = slide.header;

        if (bodyText != null)
            bodyText.text = slide.body;

        if (pageCounterText != null)
            pageCounterText.text = $"{_currentIndex + 1}/{slides.Length}";
    }

    private void ApplySlideWithFade()
    {
        if (rootGroup == null)
        {
            ApplySlideInstant();
            return;
        }

        _isAnimating = true;

        // Fade out, swap text, fade in
        LeanTween.alphaCanvas(rootGroup, 0f, fadeDuration)
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                ApplySlideInstant();

                LeanTween.alphaCanvas(rootGroup, 1f, fadeDuration)
                    .setIgnoreTimeScale(true)
                    .setOnComplete(() => _isAnimating = false);
            });
    }

    // ─────────────────────────────────────────────────────────────
    // Finish → go to starter select (or whatever panel is set)
    // ─────────────────────────────────────────────────────────────
    private void FinishIntro()
    {
        MarkIntroSeen();

        // Optionally fade out the intro before switching panels.
        if (rootGroup != null)
        {
            _isAnimating = true;
            LeanTween.alphaCanvas(rootGroup, 0f, fadeDuration)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    _isAnimating = false;
                    GoToNextPanel();
                });
        }
        else
        {
            GoToNextPanel();
        }
    }

    private void GoToNextPanel()
    {
        if (UIManager.I != null)
        {
            UIManager.I.Hide(PanelId.Intro); 
            if (nextPanelId != PanelId.None)
            {
                UIManager.I.Show(nextPanelId);
            }
        }
        else
        {
            Debug.LogWarning("[IntroPanelUI] UIManager.I is null, cannot switch panels.");
        }
    }
}
