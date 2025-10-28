using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BattleHPBoosterUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;                 
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Effect")]
    [SerializeField, Min(1)]    private int   flatHPBonus      = 50;   
    [SerializeField, Min(0.5f)] private float durationSeconds  = 12f;  
    [SerializeField] private bool consumeItem = true;
    [Tooltip("Also add immediate HP (up to new max) on use.")]
    [SerializeField] private bool grantShieldOnCast = true;

    private Coroutine tick;

    void OnEnable()
    {
        if (boosterBtn)
        {
            boosterBtn.onClick.RemoveAllListeners();
            boosterBtn.onClick.AddListener(OnPress);
        }

        if (boosterRadial) boosterRadial.fillAmount = 0f;

        RefreshCounts();
        SetInteractable(CanActivate());

        GameEvents.OnResourcesChanged += OnResChanged;

        if (tick != null) StopCoroutine(tick);
        tick = StartCoroutine(TickUI());
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= OnResChanged;

        if (tick != null) StopCoroutine(tick);
        tick = null;

        if (boosterRadial) boosterRadial.fillAmount = 0f;
        if (boosterBtn) boosterBtn.onClick.RemoveAllListeners();
    }

    void OnResChanged() => RefreshCounts();

    void RefreshCounts()
    {
        int have = ResourceBank.Get(ResourceType.HPBoosters);
        if (boosterCountLabel) boosterCountLabel.text = $"x{have}";
    }

    bool CanActivate()
    {
        if (BattleTempBuffs.I && BattleTempBuffs.I.IsHPBonusActive()) return false;
        if (!consumeItem) return true;
        return ResourceBank.Get(ResourceType.HPBoosters) > 0;
    }

    void SetInteractable(bool v)
    {
        if (!boosterBtn) return;
        boosterBtn.interactable = v;

        // Optional: subtle visual feedback via CanvasGroup if present
        var cg = boosterBtn.GetComponent<CanvasGroup>();
        if (cg) cg.alpha = v ? 1f : 0.5f;
    }

    void OnPress()
    {
        if (!CanActivate()) return;

        if (consumeItem && !ResourceBank.TrySpend(ResourceType.HPBoosters, 1))
        {
            RefreshCounts();
            return;
        }

        if (BattleTempBuffs.I)
        {
            BattleTempBuffs.I.ActivatePlayerHPBonus(flatHPBonus, durationSeconds);
        }

        // Optional shield-on-cast: add immediate HP up to new max
        if (grantShieldOnCast)
        {
            var bm = FindFirstObjectByType<BattleManager>();
            if (bm) bm.TryAddHPToActive(flatHPBonus);
        }

        GameEvents.OnResourcesChanged?.Invoke();
        RefreshCounts();

        SetInteractable(false);
    }

    IEnumerator TickUI()
    {
        while (true)
        {
            bool active = BattleTempBuffs.I && BattleTempBuffs.I.IsHPBonusActive();

            if (boosterRadial)
            {
                if (active)
                {
                    float total = BattleTempBuffs.I.GetHPBonusTotalSecondsIfActive(durationSeconds);
                    float rem   = BattleTempBuffs.I.GetHPBonusRemainingSeconds();

                    total = Mathf.Max(0.001f, total);
                    rem   = Mathf.Clamp(rem, 0f, total);

                    boosterRadial.fillAmount = 1f - (rem / total);
                }
                else
                {
                    boosterRadial.fillAmount = 0f;
                }
            }

            SetInteractable(CanActivate());
            yield return null;
        }
    }
}
