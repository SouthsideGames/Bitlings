using UnityEngine;
using UnityEngine.UI;

public class JobSlotUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Image icon;
    [SerializeField] private Button button;
    [SerializeField] private JobAssignPanelUI jobAssignPanelUI;

    [HideInInspector] public JobType job;
    [HideInInspector] public int slotIndex;

    // Optional: cached state (useful if you ever want to refresh visuals)
    private MonsterDataSO _cachedDef;
    private bool _cachedIsShiny;
    private bool _hasCachedWorker;

    public void SetEmpty(Sprite emptySprite, Color emptyColor)
    {
        if (!icon) return;

        _hasCachedWorker = false;
        _cachedDef = null;
        _cachedIsShiny = false;

        icon.sprite = emptySprite;
        icon.color = emptyColor;
        icon.preserveAspect = true;
    }

    /// <summary>
    /// Legacy path: caller provides a sprite directly. Kept for compatibility.
    /// </summary>
    public void SetWorker(Sprite workerSprite, Color filledColor)
    {
        if (!icon) return;

        // Kept as-is for backwards compatibility.
        _hasCachedWorker = false;
        _cachedDef = null;
        _cachedIsShiny = false;

        icon.sprite = workerSprite;
        icon.color = filledColor;
        icon.preserveAspect = true;
    }

    /// <summary>
    /// Preferred path for jobs:
    /// Always uses MonsterDataSO FRONT icons:
    /// - shiny => def.shinyIcon
    /// - normal => def.icon
    /// </summary>
    public void SetWorker(MonsterDataSO def, bool isShiny, Color filledColor)
    {
        if (!icon) return;

        _hasCachedWorker = true;
        _cachedDef = def;
        _cachedIsShiny = isShiny;

        icon.sprite = ResolveFrontIcon(def, isShiny);
        icon.color = filledColor;
        icon.preserveAspect = true;
    }

    /// <summary>
    /// Re-apply cached worker icon after any external UI refresh.
    /// Safe no-op if nothing cached.
    /// </summary>
    public void RefreshWorkerIconIfCached()
    {
        if (!icon) return;
        if (!_hasCachedWorker || _cachedDef == null) return;

        icon.sprite = ResolveFrontIcon(_cachedDef, _cachedIsShiny);
        icon.preserveAspect = true;
    }

    /// <summary>
    /// Explicitly clear cached worker (without needing to provide an empty sprite).
    /// </summary>
    public void ClearCachedWorker()
    {
        _hasCachedWorker = false;
        _cachedDef = null;
        _cachedIsShiny = false;
    }

    private static Sprite ResolveFrontIcon(MonsterDataSO def, bool isShiny)
    {
        if (def == null) return null;

        // Force FRONT icons per requirement (MonsterDataSO.shinyIcon / MonsterDataSO.icon)
        if (isShiny)
        {
            if (def.shinyIcon != null) return def.shinyIcon;

            // If shiny is requested but missing, fall back to normal icon
            if (def.icon != null) return def.icon;

            return null;
        }

        if (def.icon != null) return def.icon;

        // Last-resort fallback (helps if a def is missing normal icon but has shiny icon)
        return def.shinyIcon;
    }

    public void WireToPicker()
    {
        if (!button) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            if (!jobAssignPanelUI) return;

            // If the slot is empty and currently resting/exhausted, don't open the picker.
            // (If a worker is present, we still allow opening so the player can remove/swap.)
            if (!_hasCachedWorker)
            {
                if (!EligibilityRules.CanUseJobSlot(job, slotIndex, out string reason, out _))
                {
                    AudioManager.I?.PlayDenied();
                    if (!string.IsNullOrEmpty(reason)) GameEvents.RaiseToast(reason);
                    return;
                }
            }

            jobAssignPanelUI.Open(job, slotIndex);
        });
    }

    /// <summary>
    /// Enables/disables interaction on this slot button (safe even if button is null).
    /// </summary>
    public void SetInteractable(bool on)
    {
        if (!button) return;

        button.interactable = on;

        // Preserve existing behavior: when disabling, remove listeners so we can't open the picker.
        if (!on) button.onClick.RemoveAllListeners();
    }
}
