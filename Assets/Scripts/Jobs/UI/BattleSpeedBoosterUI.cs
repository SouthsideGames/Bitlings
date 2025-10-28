// Scripts/Jobs/BattleSpeedBoosterUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BattleSpeedBoosterUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Effect")]
    [SerializeField, Min(1)]    private int   flatSpeedBonus   = 10;   // flat initiative speed
    [SerializeField, Min(0.5f)] private float durationSeconds  = 10f;  // unscaled
    [SerializeField] private bool consumeItem = true;

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

        if (tick != null) StopCoroutine(tick);
        tick = StartCoroutine(Tick());
    }

    void OnDisable()
    {
        if (tick != null)
        {
            StopCoroutine(tick);
            tick = null;
        }
        SetInteractable(true);
    }

    void RefreshCounts()
    {
        int count = ResourceBank.Get(ResourceType.SpeedBoosters);
        if (boosterCountLabel) boosterCountLabel.text = $"{count}";
    }

    bool CanActivate()
    {
        if (ResourceBank.Get(ResourceType.SpeedBoosters) <= 0) return false;
        return true;
    }

    void SetInteractable(bool v)
    {
        if (boosterBtn) boosterBtn.interactable = v;
    }

    void OnPress()
    {
        // Try to spend (optional)
        if (consumeItem)
        {
            if (!ResourceBank.TrySpend(ResourceType.SpeedBoosters, 1))
                return;
        }

        BattleTempBuffs.I?.ActivatePlayerSpeedBonus(flatSpeedBonus, durationSeconds);
        RefreshCounts();
        SetInteractable(CanActivate());
    }

    IEnumerator Tick()
    {
        while (enabled)
        {
            if (BattleTempBuffs.I != null)
            {
                float rem = BattleTempBuffs.I.GetSpeedBonusRemainingSeconds();
                float total = BattleTempBuffs.I.GetSpeedBonusTotalSecondsIfActive(durationSeconds);

                if (rem > 0.001f && total > 0.001f && boosterRadial)
                {
                    float fill = 1f - (rem / total);
                    boosterRadial.fillAmount = fill;
                }
                else
                {
                    if (boosterRadial) boosterRadial.fillAmount = 0f;
                }
            }

            SetInteractable(CanActivate());
            yield return null;
        }
    }
}
