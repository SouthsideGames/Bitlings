using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleTitleStatusBarUI : MonoBehaviour
{
    public enum Side { Player, Wild }

    [Header("Refs")]
    [SerializeField] private BattleManager battle;
    [SerializeField] private Side side = Side.Player;

    [Header("UI")]
    [SerializeField] private Transform iconRoot;
    [SerializeField] private BattleTitleStatusIconUI iconPrefab;

    [Header("Behavior")]
    [SerializeField] private bool hideIfNone = true;
    [SerializeField] private float refreshInterval = 0.25f;

    private readonly List<BattleTitleStatusIconUI> _spawned = new();
    private float _t;
    private string _lastKey = "";

    void OnEnable()
    {
        ForceRefresh();
    }

    void OnDisable()
    {
        ClearIcons();
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (_t < refreshInterval) return;
        _t = 0f;

        if (battle == null || !battle.InBattle)
        {
            if (hideIfNone) gameObject.SetActive(false);
            return;
        }

        string key = BuildStateKey();
        if (key == _lastKey) return;

        ForceRefresh();
    }

    private string BuildStateKey()
    {
        if (battle == null || !battle.InBattle) return "";

        if (side == Side.Player)
            return battle.ActivePlayerMonsterId ?? "";
        else
            return (battle.WildDef != null ? battle.WildDef.id : "WILD_NULL") + ":" + battle.WildLevel;
    }

    public void ForceRefresh()
    {
        _t = 0f;

        if (battle == null || !battle.InBattle)
        {
            ClearIcons();
            if (hideIfNone) gameObject.SetActive(false);
            _lastKey = "";
            return;
        }

        _lastKey = BuildStateKey();

        List<TitleSO> titles = GetTitlesForSide();
        if (titles == null) titles = new List<TitleSO>();

        // Filter nulls + respect UI flag
        var filtered = new List<TitleSO>(titles.Count);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;
            if (!t.showInBattleStatusBar) continue;
            filtered.Add(t);
        }

        ClearIcons();

        for (int i = 0; i < filtered.Count; i++)
        {
            var so = filtered[i];
            var ui = Instantiate(iconPrefab, iconRoot);
            ui.Bind(so, stackText: "");
            _spawned.Add(ui);
        }

        bool any = filtered.Count > 0;
        if (hideIfNone) gameObject.SetActive(any);
    }

    private List<TitleSO> GetTitlesForSide()
    {
        if (TitleManager.I == null) return new List<TitleSO>();

        if (side == Side.Player)
        {
            var id = battle.ActivePlayerMonsterId;
            if (string.IsNullOrEmpty(id)) return new List<TitleSO>();

            return TitleManager.I.GetTitlesForMonster(id);
        }
        else
        {
            // Wild monster: no saved equips; but you may have always-on titles.
            // If you *do* want wild titles, implement a wild-title policy.
            // For now: return empty.
            return new List<TitleSO>();
        }
    }

    private void ClearIcons()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i]) Destroy(_spawned[i].gameObject);
        }
        _spawned.Clear();
    }
}

public sealed class BattleTitleStatusIconUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI stackText;

    public void Bind(TitleSO so, string stackText = "")
    {
        if (icon) icon.sprite = so ? so.icon : null;

        if (this.stackText)
        {
            this.stackText.text = stackText ?? "";
            this.stackText.gameObject.SetActive(!string.IsNullOrEmpty(stackText));
        }
    }
}
