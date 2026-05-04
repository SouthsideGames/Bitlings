using UnityEngine;
using TMPro;
using System;

// ─────────────────────────────────────────────────────────────────────────────
// NoteLogRowUI
//
// Renders one LogEntry in the scrollable battle log.
//
// MULTILINE ROWS (damage breakdowns):
//   FormatDamageLine now returns strings with embedded \n for sub-lines.
//   To make rows auto-size to their content height, ensure the prefab has:
//     • TextMeshProUGUI  →  Overflow = Overflow, VerticalOverflow = Overflow
//     • RectTransform    →  Height driven by a ContentSizeFitter
//                           (Vertical Fit = Preferred Size)
//   The NoteLogPanelUI ScrollRect content should use a VerticalLayoutGroup
//   with "Child Force Expand Height" OFF and "Control Child Height" ON.
//
// TURN PREFIX:
//   Set NoteLogRowUI.CurrentTurn at the start of each turn in BattleManager.
//   Battle-scope rows display [T12] instead of a wall-clock timestamp.
//   Reset to 0 between battles so system lines fall back to [HH:mm].
// ─────────────────────────────────────────────────────────────────────────────
public class NoteLogRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Color systemColor = new Color(0.8f, 0.9f, 1f);
    [SerializeField] private Color riftColor   = Color.white;
    [SerializeField] private Color battleColor = Color.white;

    // ── Static turn counter ──────────────────────────────────────────────────
    // Write this from BattleManager at the top of each turn:
    //   NoteLogRowUI.CurrentTurn = _turn;
    // Reset between battles:
    //   NoteLogRowUI.CurrentTurn = 0;
    public static int CurrentTurn { get; set; } = 0;

    public void Set(LogEntry e)
    {
        if (!label) return;

        // ── Prefix ────────────────────────────────────────────────────────
        // Battle scope + live turn → compact [T12]  (aligns with math sub-lines)
        // Everything else          → wall clock [HH:mm]
        string prefix;
        if (e.scope == LogScope.Battle && CurrentTurn > 0)
        {
            prefix = $"<color=#555555>[T{CurrentTurn,2}]</color> ";
        }
        else
        {
            var t = DateTimeOffset.FromUnixTimeSeconds(e.unix).ToLocalTime();
            prefix = $"<color=#555555>[{t:HH:mm}]</color> ";
        }

        label.text = prefix + e.text;

        // ── Row tint ──────────────────────────────────────────────────────
        switch (e.scope)
        {
            case LogScope.System: label.color = systemColor; break;
            case LogScope.Rift:   label.color = riftColor;   break;
            default:              label.color = battleColor;  break;
        }
    }
}
