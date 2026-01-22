using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class AchievementToastUI : MonoBehaviour
{
    public static AchievementToastUI I { get; private set; }

    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Timing")]
    [SerializeField] private float showSeconds = 2.25f;

    [Header("Text")]
    [SerializeField] private string prefixText = "Achievement Unlocked:";

    private readonly Queue<AchievementEntrySO> _queue = new Queue<AchievementEntrySO>();
    private bool _playing;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;


        DontDestroyOnLoad(gameObject);

        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// Queue a toast for an unlocked achievement. Safe to call even if the toast is inactive.
    /// </summary>
    public void QueueUnlocked(AchievementEntrySO entry)
    {
        if (entry == null) return;

        _queue.Enqueue(entry);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (!_playing)
            PlayNext();
    }

    private void PlayNext()
    {
        if (_queue.Count == 0)
        {
            _playing = false;

            if (canvasGroup) canvasGroup.alpha = 0f;
            return;
        }

        _playing = true;

        var entry = _queue.Dequeue();

        if (iconImage) iconImage.sprite = entry.icon;
        if (label) label.text = $"{prefixText} {entry.displayName}";

        if (canvasGroup)
        {
            LeanTween.cancel(canvasGroup.gameObject);
            canvasGroup.alpha = 0f;

            LeanTween.alphaCanvas(canvasGroup, 1f, 0.18f).setEaseOutCubic();

            LeanTween.delayedCall(gameObject, showSeconds, () =>
            {
                if (canvasGroup == null)
                {
                    PlayNext();
                    return;
                }

                LeanTween.alphaCanvas(canvasGroup, 0f, 0.18f).setEaseInCubic()
                    .setOnComplete(() => PlayNext());
            });
        }
        else
        {
            LeanTween.delayedCall(gameObject, showSeconds, () => PlayNext());
        }
    }
}
