// Assets/Scripts/Jobs/BattleAttackBoosterUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BattleAttackBoosterUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Effect")]
    [SerializeField, Min(1)]    private int   flatBonus       = 10;   
    [SerializeField, Min(0.5f)] private float durationSeconds = 10f;  
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
        int count = ResourceBank.Get(ResourceType.AttackBoosters);
        if (boosterCountLabel) boosterCountLabel.text = $"{count}";
    }

    bool CanActivate()
    {
        // allow pressing if you have at least 1 in inventory
        return ResourceBank.Get(ResourceType.AttackBoosters) > 0;
    }

    void SetInteractable(bool v)
    {
        if (boosterBtn) boosterBtn.interactable = v;
    }

    void OnPress()
    {
        if (consumeItem)
        {
            if (!ResourceBank.TrySpend(ResourceType.AttackBoosters, 1))
                return;
        }

        BattleTempBuffs.I?.ActivatePlayerAtkBonus(flatBonus, durationSeconds);

        RefreshCounts();
        SetInteractable(CanActivate());
    }

    IEnumerator Tick()
    {
        while (enabled)
        {
            if (BattleTempBuffs.I != null)
            {
                float rem   = BattleTempBuffs.I.GetAtkBonusRemainingSeconds();
                float total = BattleTempBuffs.I.GetAtkBonusTotalSecondsIfActive(durationSeconds);

                if (rem > 0.001f && total > 0.001f && boosterRadial)
                {
                    boosterRadial.fillAmount = 1f - (rem / total);
                }
                else if (boosterRadial)
                {
                    boosterRadial.fillAmount = 0f;
                }
            }

            SetInteractable(CanActivate());
            yield return null;
        }
    }
}
