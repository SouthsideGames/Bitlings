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

    [Header("Panel Routing")]
    [SerializeField] private PanelId selfPanelId = PanelId.Story;
    [SerializeField] private PanelId introPanelId = PanelId.Intro;
    [SerializeField] private PanelId homePanelId = PanelId.Home;

    [Header("Token Replacement")]
    [Tooltip("If enabled, replaces placeholders like [INSERT ID DESIGNATION] with live data (e.g., from PlayerDossier).")]
    [SerializeField] private bool replaceTokens = true;

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

        string header = page.header;
        string body = page.body;

        if (replaceTokens)
        {
            header = ReplaceStoryTokens(header);
            body = ReplaceStoryTokens(body);
        }

        if (headerText) headerText.text = header;
        if (bodyText)   bodyText.text   = body;
        if (pageCounterText) pageCounterText.text = $"{_index + 1}/{pages.Length}";
    }

    private string ReplaceStoryTokens(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Supported placeholders (keep the bracketed ones for designer readability):
        //  - [INSERT ID DESIGNATION]
        //  - [INSERT JOB TITLE]
        // Also supports: {OP_ID} and {JOB_TITLE}

        string opId = ResolveOperationIdDesignation();
        string jobTitle = "BRN Intern";

        return input
            .Replace("[INSERT ID DESIGNATION]", opId)
            .Replace("{OP_ID}", opId)
            .Replace("[INSERT JOB TITLE]", jobTitle)
            .Replace("{JOB_TITLE}", jobTitle);
    }

    private string ResolveOperationIdDesignation()
    {
        // Prefer the PlayerDossier snapshot (authoritative formatting), fall back to SaveManager.
        string raw = null;

        if (PlayerDossierManager.I != null)
        {
            var snap = PlayerDossierManager.I.CurrentSnapshot;
            if (snap != null)
                raw = snap.operationId; // commonly "Operation ID: BRN-..."
        }

        if (string.IsNullOrEmpty(raw) && SaveManager.Data != null)
        {
            // Minimal local fallback; if dossier isn't initialized yet we still want a stable ID.
            raw = SaveManager.Data.playerId;
        }

        if (string.IsNullOrEmpty(raw))
            return "BRN-0000-XXXX";

        // If the dossier includes a label prefix, strip it so story reads cleanly.
        const string prefix = "Operation ID:";
        if (raw.StartsWith(prefix))
            raw = raw.Substring(prefix.Length).Trim();

        return raw;
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
        SaveManager.MarkStorySeen();

        if (storyGroup != null)
        {
            storyGroup.alpha = 0f;
            storyGroup.interactable = false;
            storyGroup.blocksRaycasts = false;
        }

        RouteAfterStory();
    }

    private void RouteAfterStory()
    {
        var ui = UIManager.I;
        if (ui != null)
        {
            if (selfPanelId != PanelId.None) ui.Hide(selfPanelId);

            bool hasStarter = SaveManager.HasStarter();
            if (hasStarter)
            {
                if (homePanelId != PanelId.None) ui.Show(homePanelId);
            }
            else
            {
                if (introPanelId != PanelId.None) ui.Show(introPanelId);
            }

            return;
        }

        gameObject.SetActive(false);
    }
}
