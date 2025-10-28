// Scripts/Jobs/BattleTypeResistUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BattleTypeResistUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Effect")]
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
        int count = ResourceBank.Get(ResourceType.Sigils);
        if (boosterCountLabel) boosterCountLabel.text = $"{count}";
    }

    bool CanActivate() => ResourceBank.Get(ResourceType.Sigils) > 0;

    void SetInteractable(bool v)
    {
        if (boosterBtn) boosterBtn.interactable = v;
    }

    void OnPress()
    {
        if (consumeItem)
        {
            if (!ResourceBank.TrySpend(ResourceType.Sigils, 1))
                return;
        }

        BattleTempBuffs.I?.ActivatePlayerTypeResist(durationSeconds);
        RefreshCounts();
        SetInteractable(CanActivate());
    }

    IEnumerator Tick()
    {
        while (enabled)
        {
            if (BattleTempBuffs.I != null)
            {
                float rem = BattleTempBuffs.I.GetTypeResistRemainingSeconds();
                float total = BattleTempBuffs.I.GetTypeResistTotalSecondsIfActive(durationSeconds);

                if (rem > 0.001f && total > 0.001f && boosterRadial)
                    boosterRadial.fillAmount = 1f - (rem / total);
                else if (boosterRadial)
                    boosterRadial.fillAmount = 0f;
            }

            SetInteractable(CanActivate());
            yield return null;
        }
    }
}
