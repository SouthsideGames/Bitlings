using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class StatBreakdownHoldTrigger : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum Side { Player, Wild }

    [SerializeField] private Side side = Side.Player;

    [Header("Long Press Settings")]
    [Tooltip("Hold this long (in seconds) before showing the breakdown.")]
    [SerializeField] private float holdTime = 0.45f;

    private bool _isPointerDown;
    private float _pointerDownTimer;

    void Update()
    {
        if (!_isPointerDown) return;

        _pointerDownTimer += Time.unscaledDeltaTime;
        if (_pointerDownTimer >= holdTime)
        {
            ShowBreakdown();
            Reset();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPointerDown = true;
        _pointerDownTimer = 0f;
    }

    public void OnPointerUp(PointerEventData eventData) => Reset();
    public void OnPointerExit(PointerEventData eventData) => Reset();

    private void Reset()
    {
        _isPointerDown = false;
        _pointerDownTimer = 0f;
    }

    private void ShowBreakdown()
    {
        var bm = FindBattleManager();
        if (bm == null || bm.Stats == null || !bm.InBattle) return;

        // Find panel even if its GameObject is still inactive (Awake hasn't run yet).
        var panel = StatBreakdownPanelUI.I;
        if (panel == null)
            panel = FindFirstObjectByType<StatBreakdownPanelUI>(FindObjectsInactive.Include);
        if (panel == null) return;

        if (side == Side.Player)
            ShowPlayerBreakdown(bm, panel);
        else
            ShowWildBreakdown(bm, panel);
    }

    private void ShowPlayerBreakdown(BattleManager bm, StatBreakdownPanelUI panel)
    {
        int idx = bm.ActiveIndex;
        var def = bm.GetTeamDefSafe(idx);
        string name = def != null ? def.displayName : "Ally";

        var baseStats = bm.Stats.GetAdjustedPlayer(idx);
        var finalStats = bm.Stats.GetEffectivePlayer(idx);
        var statLines = bm.Stats.GetPlayerStatLines(idx);
        var jctx = bm.GetJobCtxSafe(idx);
        var effectLines = CombatEffectLineBuilder.Build(jctx);

        string jobName = (jctx != null && jctx.job != JobType.None)
            ? JobStrings.SiteName(jctx.job)
            : null;

        panel.Show(name, baseStats, finalStats, statLines, effectLines, jobName);
    }

    private void ShowWildBreakdown(BattleManager bm, StatBreakdownPanelUI panel)
    {
        var wildDef = bm.WildDef;
        string name = wildDef != null ? wildDef.displayName : "Wild";

        var baseStats = bm.Stats.GetAdjustedWild();
        var finalStats = bm.Stats.GetEffectiveWild();
        var statLines = bm.Stats.GetWildStatLines();
        List<CombatEffectLineBuilder.CombatEffectLine> noEffects = null;

        panel.Show(name, baseStats, finalStats, statLines, noEffects, null);
    }

    private BattleManager FindBattleManager()
    {
        // Walk up the hierarchy first (most likely on the battle HUD)
        var bm = GetComponentInParent<BattleManager>();
        if (bm != null) return bm;

        return FindFirstObjectByType<BattleManager>();
    }
}
