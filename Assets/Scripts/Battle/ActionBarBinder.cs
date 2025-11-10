using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ActionBarBinder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BattleManager battle;
    [SerializeField] private Button attackBtn;
    [SerializeField] private Button defendBtn;
    [SerializeField] private Button focusBtn;
    [SerializeField] private Button runBtn;

    [Header("UX")]
    [Tooltip("If true, buttons auto-disable when it isn't the player's turn.")]
    [SerializeField] private bool autoDisableWhenNotPlayerTurn = true;

    [Tooltip("If true, buttons also auto-disable when Encounter auto-mode is on (EncounterManager.I.IsAutoMode).")]
    [SerializeField] private bool alsoDisableDuringEncounterAutoMode = true;

    void Reset()
    {
        // Try to find BattleManager up the hierarchy or in scene
        if (!battle)
        {
            battle = GetComponentInParent<BattleManager>();
            if (!battle) battle = FindFirstObjectByType<BattleManager>();
        }

        // Auto-grab buttons by name
        var btns = GetComponentsInChildren<Button>(true);
        foreach (var b in btns)
        {
            var n = b.name.ToLowerInvariant();
            if (n.Contains("attack")) attackBtn = b;
            else if (n.Contains("defend")) defendBtn = b;
            else if (n.Contains("focus"))  focusBtn  = b;
            else if (n.Contains("run"))    runBtn    = b;
        }
    }

    void Awake()
    {
        // Best-effort fallback if not wired in Inspector
        if (!battle)
        {
            battle = GetComponentInParent<BattleManager>();
            if (!battle) battle = FindFirstObjectByType<BattleManager>();
        }

        // Bind the clicks (clear old listeners just in case)
        if (attackBtn)
        {
            attackBtn.onClick.RemoveAllListeners();
            attackBtn.onClick.AddListener(() => { if (battle) battle.SetPlayerActionAttack(); });
        }
        if (defendBtn)
        {
            defendBtn.onClick.RemoveAllListeners();
            defendBtn.onClick.AddListener(() => { if (battle) battle.SetPlayerActionDefend(); });
        }
        if (focusBtn)
        {
            focusBtn.onClick.RemoveAllListeners();
            focusBtn.onClick.AddListener(() => { if (battle) battle.SetPlayerActionFocus();  });
        }
        if (runBtn)
        {
            runBtn.onClick.RemoveAllListeners();
            runBtn.onClick.AddListener(() => { if (battle) battle.SetPlayerActionRun();    });
        }
    }

    void Update()
    {
        if (!autoDisableWhenNotPlayerTurn)
            return;

        bool enable = false;

        if (battle && battle.isActiveAndEnabled)
        {
            enable = battle.IsPlayerTurn;

            // Optionally also gate by Encounter auto-mode if present
            if (alsoDisableDuringEncounterAutoMode)
            {
                bool isAuto = false;
                try
                {
                    // This is safe even if EncounterManager.I is null
                    isAuto = (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;
                }
                catch { /* ignore if the API differs; we just won't gate by auto-mode */ }

                if (isAuto) enable = false;
            }
        }

        if (attackBtn && attackBtn.interactable != enable) attackBtn.interactable = enable;
        if (defendBtn && defendBtn.interactable != enable) defendBtn.interactable = enable;
        if (focusBtn  && focusBtn.interactable  != enable) focusBtn.interactable  = enable;
        if (runBtn    && runBtn.interactable    != enable) runBtn.interactable    = enable;
    }
}
