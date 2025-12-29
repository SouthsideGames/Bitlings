using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class BattleTitleStatusBarUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battle;

    [Header("UI")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private Button infoButton;

    [Header("Rules")]
    [SerializeField] private bool hideIfNoTitle = true;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;

    private float _t;
    private string _lastMonsterId;
    private TitleSO _currentTitle;

    void OnEnable()
    {
        if (infoButton)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OpenInfo);
        }

        ForceRefresh();
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (_t < refreshInterval) return;
        _t = 0f;

        if (battle == null || !battle.InBattle || TitleManager.I == null)
        {
            ApplyEmpty();
            return;
        }

        var monsterId = battle.ActivePlayerMonsterId;
        if (monsterId != _lastMonsterId)
            ForceRefresh();
    }

    public void ForceRefresh()
    {
        _t = 0f;

        if (battle == null || !battle.InBattle || TitleManager.I == null)
        {
            ApplyEmpty();
            return;
        }

        _lastMonsterId = battle.ActivePlayerMonsterId;
        RefreshForMonster(_lastMonsterId);
    }

    private void RefreshForMonster(string monsterId)
    {
        _currentTitle = null;

        if (string.IsNullOrEmpty(monsterId))
        {
            ApplyEmpty();
            return;
        }

        // One title max by design
        var states = TitleManager.I.GetActiveTitleUIStates(monsterId);
        if (states == null || states.Count == 0)
        {
            ApplyEmpty();
            return;
        }

        var s = states[0];
        var title = TitleManager.I.GetTitleById(s.titleId);
        if (!title)
        {
            ApplyEmpty();
            return;
        }

        _currentTitle = title;

        if (iconImage)  iconImage.sprite = title.icon;
        if (titleLabel) titleLabel.text = title.displayName;

        gameObject.SetActive(true);
    }

    private void ApplyEmpty()
    {
        _currentTitle = null;

        if (iconImage)  iconImage.sprite = null;
        if (titleLabel) titleLabel.text = "";

        if (hideIfNoTitle)
            gameObject.SetActive(false);
    }

    private void OpenInfo()
    {
        if (_currentTitle == null) return;

        // Match ResourceRowUI behavior exactly
        var id = $"title.{_currentTitle.titleId}";
        InfoRouter.Open(
            id,
            _currentTitle.displayName,
            "Active Title",
            _currentTitle.description
        );

        AudioManager.I?.PlayClick();
    }
}
