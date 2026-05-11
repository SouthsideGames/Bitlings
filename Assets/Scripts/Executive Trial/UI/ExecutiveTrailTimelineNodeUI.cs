using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class IronRunTimelineNodeUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Image           circleImage;
    [SerializeField] private Image           monsterIconImage;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private GameObject      skullOverlay;
    [SerializeField] private GameObject      forfeitOverlay;
    [SerializeField] private Button          tapButton;

    [Header("Colors")]
    [SerializeField] private Color victoryColor = new Color(0.24f, 0.87f, 0.45f, 1f);
    [SerializeField] private Color defeatColor  = new Color(1.00f, 0.33f, 0.33f, 1f);
    [SerializeField] private Color escapedColor = new Color(0.70f, 0.70f, 0.70f, 1f);
    [SerializeField] private Color forfeitColor = new Color(1.00f, 1.00f, 1.00f, 1f);

    private IronBattleLogEntry _entry;

    public void Bind(IronBattleLogEntry entry, bool isFinalNode)
    {
        _entry = entry;

        // Set circle color
        if (entry.isForfeit)
            circleImage.color = forfeitColor;
        else if (entry.victory)
            circleImage.color = victoryColor;
        else if (entry.wildEscaped || entry.playerEscaped)
            circleImage.color = escapedColor;
        else
            circleImage.color = defeatColor;

        // Monster icon
        var def = !string.IsNullOrEmpty(entry.wildId) ? MonsterLibraryLocator.GetById(entry.wildId) : null;
        monsterIconImage.sprite = def?.icon;
        monsterIconImage.gameObject.SetActive(def != null && !entry.isForfeit);

        // Level label
        levelLabel.text = entry.isForfeit ? string.Empty : $"Lv {entry.wildLevel}";

        // Overlays
        skullOverlay.SetActive(!entry.isForfeit && entry.deathsThisBattle > 0);
        forfeitOverlay.SetActive(entry.isForfeit);

        // Final node size
        if (isFinalNode && !entry.isForfeit)
            transform.localScale = Vector3.one * 0.85f;

        // Wire button
        if (tapButton)
        {
            tapButton.onClick.RemoveAllListeners();
            tapButton.onClick.AddListener(OnTapped);
        }
    }

    private void OnTapped()
    {
        if (TooltipUI.I == null) return;

        if (_entry.isForfeit)
        {
            TooltipUI.I.Show("Run forfeited.");
            return;
        }

        string outcome = _entry.victory      ? "Victory"
                       : _entry.wildEscaped  ? "Wild Fled"
                       : _entry.playerEscaped? "Escaped"
                       :                       "Defeat";

        string text = $"Floor {_entry.winsBeforeBattle + 1}\n"
                    + $"Outcome: {outcome}\n"
                    + $"Turns: {_entry.turnsSurvived}\n"
                    + $"Dealt: {_entry.damageDealt:N0}   Taken: {_entry.damageTaken:N0}";

        if (_entry.deathsThisBattle > 0)
            text += $"\nDeaths: {_entry.deathsThisBattle}";

        TooltipUI.I.Show(text);
    }

    private void OnDestroy()
    {
        LeanTween.cancel(gameObject);
    }
}
