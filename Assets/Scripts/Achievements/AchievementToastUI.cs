using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class AchievementToastUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Timing")]
    [SerializeField] private float showSeconds = 2.25f;

    private readonly Queue<AchievementEntrySO> _queue = new Queue<AchievementEntrySO>();
    private bool _playing;

    private void Awake()
    {
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        if (AchievementManager.I != null)
            AchievementManager.I.OnUnlocked += HandleUnlocked;
    }

    private void OnDisable()
    {
        if (AchievementManager.I != null)
            AchievementManager.I.OnUnlocked -= HandleUnlocked;
    }

    private void HandleUnlocked(AchievementEntrySO entry)
    {
        if (entry == null) return;
        _queue.Enqueue(entry);

        if (!_playing)
            PlayNext();
    }

    private void PlayNext()
    {
        if (_queue.Count == 0)
        {
            _playing = false;
            return;
        }

        _playing = true;

        var entry = _queue.Dequeue();

        if (iconImage) iconImage.sprite = entry.icon;
        if (label) label.text = $"Achievement Unlocked: {entry.displayName}";

        gameObject.SetActive(true);

        if (canvasGroup)
        {
            LeanTween.cancel(canvasGroup.gameObject);
            canvasGroup.alpha = 0f;
            LeanTween.alphaCanvas(canvasGroup, 1f, 0.18f).setEaseOutCubic();

            LeanTween.delayedCall(gameObject, showSeconds, () =>
            {
                LeanTween.alphaCanvas(canvasGroup, 0f, 0.18f).setEaseInCubic()
                    .setOnComplete(() => PlayNext());
            });
        }
        else
        {
            // No CanvasGroup fallback
            LeanTween.delayedCall(gameObject, showSeconds, () => PlayNext());
        }
    }
}
