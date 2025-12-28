using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleTitleProcToastUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI text;

    [Header("Motion")]
    [SerializeField] private RectTransform moveRoot;
    [SerializeField] private Vector2 shownPos = new Vector2(0, 0);
    [SerializeField] private Vector2 hiddenPos = new Vector2(0, -60);

    [Header("Timing")]
    [SerializeField] private float fadeIn = 0.12f;
    [SerializeField] private float hold = 1.25f;
    [SerializeField] private float fadeOut = 0.20f;

    private int _ltFade = -1;
    private int _ltMove = -1;

    void Awake()
    {
        if (group) group.alpha = 0f;
        if (group) group.blocksRaycasts = false;
        if (moveRoot) moveRoot.anchoredPosition = hiddenPos;
    }

    void OnEnable()
    {
        BattleLogger.OnTitleProc += OnTitleProc;
    }

    void OnDisable()
    {
        BattleLogger.OnTitleProc -= OnTitleProc;
    }

    private void OnTitleProc(TitleProcEvent e)
    {
        // Optional: try to resolve an icon by matching displayName → TitleSO
        Sprite s = null;
        if (TitleManager.I != null)
            s = TitleManager.I.TryGetIconByTitleName(e.titleName);

        if (icon) icon.sprite = s;
        if (icon) icon.gameObject.SetActive(s != null);

        if (text) text.text = $"{e.ownerName}: {e.titleName} — {e.summary}";

        Show();
    }

    private void Show()
    {
        CancelTweens();

        if (group)
        {
            group.alpha = 0f;
            group.gameObject.SetActive(true);
        }

        if (moveRoot)
            moveRoot.anchoredPosition = hiddenPos;

        if (group)
        {
            _ltFade = LeanTween.alphaCanvas(group, 1f, fadeIn).setIgnoreTimeScale(true).id;
        }

        if (moveRoot)
        {
            _ltMove = LeanTween.move(moveRoot, shownPos, fadeIn)
                .setEaseOutQuad()
                .setIgnoreTimeScale(true).id;
        }

        // Hold then hide
        LeanTween.delayedCall(gameObject, hold, () =>
        {
            Hide();
        }).setIgnoreTimeScale(true);
    }

    private void Hide()
    {
        CancelTweens();

        if (group)
        {
            _ltFade = LeanTween.alphaCanvas(group, 0f, fadeOut)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (group) group.gameObject.SetActive(false);
                }).id;
        }

        if (moveRoot)
        {
            _ltMove = LeanTween.move(moveRoot, hiddenPos, fadeOut)
                .setEaseInQuad()
                .setIgnoreTimeScale(true).id;
        }
    }

    private void CancelTweens()
    {
        if (_ltFade != -1) { LeanTween.cancel(_ltFade); _ltFade = -1; }
        if (_ltMove != -1) { LeanTween.cancel(_ltMove); _ltMove = -1; }
    }
}
