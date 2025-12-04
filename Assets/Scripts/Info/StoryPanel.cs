using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StoryPanel : MonoBehaviour
{
    [System.Serializable]
    public class StoryPage
    {
        public string header;
        [TextArea(3, 6)] public string body;
    }

    [Header("UI")]
    [SerializeField] private CanvasGroup storyGroup;
    [SerializeField] private Button tapButton;            // full-screen tap
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private TextMeshProUGUI pageCounterText;

    [Header("Pages")]
    [SerializeField] private StoryPage[] pages;

    private int _index;

    void OnEnable()
    {
        _index = 0;
        ApplyPage();

        if (storyGroup != null)
        {
            storyGroup.alpha = 1f;
            storyGroup.interactable = true;
            storyGroup.blocksRaycasts = true; // blocks clicks from reaching Intro
        }

        if (tapButton != null)
            tapButton.onClick.AddListener(OnTap);
    }

    void OnDisable()
    {
        if (tapButton != null)
            tapButton.onClick.RemoveListener(OnTap);
    }

    void ApplyPage()
    {
        if (pages == null || pages.Length == 0)
        {
            if (headerText)      headerText.text      = "";
            if (bodyText)        bodyText.text        = "";
            if (pageCounterText) pageCounterText.text = "";
            return;
        }

        _index = Mathf.Clamp(_index, 0, pages.Length - 1);
        var page = pages[_index];

        if (headerText) headerText.text = page.header;
        if (bodyText)   bodyText.text   = page.body;
        if (pageCounterText) pageCounterText.text = $"{_index + 1}/{pages.Length}";
    }

    void OnTap()
    {
        if (pages != null && _index < pages.Length - 1)
        {
            _index++;
            ApplyPage();
            return;
        }

        // Finished story
        if (SaveManager.Data != null)
        {
            SaveManager.Data.hasSeenStory = true;
            SaveManager.Save();
        }

        if (storyGroup != null)
        {
            storyGroup.alpha = 0f;
            storyGroup.interactable = false;
            storyGroup.blocksRaycasts = false;
        }

        gameObject.SetActive(false);
    }
}
