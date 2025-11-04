using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class IdleBattleRewardPanelUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private Button collectButton;

    [Header("Header Text")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text totalBattlesText;
    [SerializeField] private TMP_Text durationText;
    [SerializeField] private TMP_Text energySpentText;

    [Header("Encounter List")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Transform listContent;         
    [SerializeField] private RewardBitlingItem itemPrefab;     

    private MonsterLibrarySO monsterLibrary;

    private Action _onCollected;

    void Awake()
    {
        if (panel) { panel.alpha = 0; panel.interactable = false; panel.blocksRaycasts = false; }
        if (collectButton) collectButton.onClick.AddListener(OnCollect);
        gameObject.SetActive(false);

        monsterLibrary = MonsterLibraryLocator.Lib;


    }

    public void Open(IdleBattleSummary summary, Action onCollected)
    {
        _onCollected = onCollected;

        if (titleText) titleText.text = "While You Were Away…";
        if (totalBattlesText) totalBattlesText.text = $"Bitlings Battled: {summary.totalEncounters}";
        if (durationText) durationText.text = $"Duration: {FormatDuration(summary.durationSeconds)}";
        if (energySpentText) energySpentText.text = $"Energy Spent: {summary.totalEnergySpent}";

        ClearList();

        if (summary.mergedLog != null && summary.mergedLog.Count > 0 && itemPrefab && listContent)
        {
            foreach (var e in summary.mergedLog)
            {
                var def = FindMonster(e.monsterId);
                var icon = def ? def.icon : null;
                var name = def ? def.displayName : e.monsterId;

                var item = Instantiate(itemPrefab, listContent);
                item.Set(icon, name, e.count);
            }

            if (scrollRect) scrollRect.verticalNormalizedPosition = 1f;
        }

        if (panel) { panel.alpha = 1; panel.interactable = true; panel.blocksRaycasts = true; }
        gameObject.SetActive(true);
    }

    private void OnCollect()
    {
        _onCollected?.Invoke();
        Close();
    }

    private void Close()
    {
        if (panel) { panel.alpha = 0; panel.interactable = false; panel.blocksRaycasts = false; }
        gameObject.SetActive(false);
        ClearList();
    }

    private void ClearList()
    {
        if (!listContent) return;
        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);
    }

    private MonsterDataSO FindMonster(string monsterId)
    {
        if (!monsterLibrary || string.IsNullOrEmpty(monsterId) || monsterLibrary.monsters == null) return null;
        for (int i = 0; i < monsterLibrary.monsters.Length; i++)
        {
            var m = monsterLibrary.monsters[i];
            if (m != null && m.id == monsterId) return m;
        }
        return null;
    }

    private static string FormatDuration(float seconds)
    {
        if (seconds < 0.5f) return "0s";
        int s = Mathf.FloorToInt(seconds % 60f);
        int m = Mathf.FloorToInt((seconds / 60f) % 60f);
        int h = Mathf.FloorToInt(seconds / 3600f);

        if (h > 0) return $"{h}h {m}m {s}s";
        if (m > 0) return $"{m}m {s}s";
        return $"{s}s";
    }
}
