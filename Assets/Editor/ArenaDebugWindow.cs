#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ArenaDebugWindow : EditorWindow
{
    private Vector2 _scroll;
    private int _ticketCount = 3;
    private int _botCount = 31;
    private ArenaScoreBand _band = ArenaScoreBand.Standard;
    private int _roundIndex;
    private ArenaTournamentRecord _activeTournament;
    private string _standingsText = "";
    private string _saveStateText = "";

    [MenuItem("Bitlings/Arena/Debug Window")]
    public static void Open()
    {
        GetWindow<ArenaDebugWindow>("Arena Debug").Show();
    }

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the Arena debugger.", MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawUnlockSection();
        EditorGUILayout.Space(6);
        DrawTicketSection();
        EditorGUILayout.Space(6);
        DrawTournamentSection();
        EditorGUILayout.Space(6);
        DrawResolutionSection();
        EditorGUILayout.Space(6);
        DrawInspectSection();
        EditorGUILayout.Space(6);
        DrawDangerZone();

        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────────────────────────────────────────
    private void DrawUnlockSection()
    {
        EditorGUILayout.LabelField("Unlock / Onboarding", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Force Unlock Arena", GUILayout.Height(26)))
            ArenaDebugHelper.ForceUnlockArena();
        if (GUILayout.Button("Dump Save State", GUILayout.Height(26)))
        {
            _saveStateText = ArenaDebugHelper.DumpSaveState();
            Debug.Log(_saveStateText);
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_saveStateText))
        {
            EditorGUILayout.HelpBox(_saveStateText, MessageType.None);
        }
    }

    // ─────────────────────────────────────────────────────────────
    private void DrawTicketSection()
    {
        EditorGUILayout.LabelField("Tickets", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _ticketCount = EditorGUILayout.IntSlider("Amount", _ticketCount, 1, 10);
        if (GUILayout.Button("Grant", GUILayout.Width(60), GUILayout.Height(18)))
            ArenaDebugHelper.GrantTickets(_ticketCount);
        if (GUILayout.Button("Set", GUILayout.Width(60), GUILayout.Height(18)))
            ArenaDebugHelper.SetTickets(_ticketCount);
        EditorGUILayout.EndHorizontal();
    }

    // ─────────────────────────────────────────────────────────────
    private void DrawTournamentSection()
    {
        EditorGUILayout.LabelField("Tournament Creation", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _botCount = EditorGUILayout.IntSlider("Bot Count", _botCount, 1, 31);
        _band = (ArenaScoreBand)EditorGUILayout.EnumPopup(_band, GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Create Fake Tournament", GUILayout.Height(26)))
        {
            _activeTournament = ArenaDebugHelper.CreateFakeTournament(_botCount, _band);
            _standingsText = "";
        }
        if (GUILayout.Button("Open Registration State", GUILayout.Height(26)))
            ArenaDebugHelper.OpenRegistrationState();
        EditorGUILayout.EndHorizontal();

        if (_activeTournament != null)
        {
            EditorGUILayout.HelpBox(
                $"Active: {_activeTournament.tournamentId}\n" +
                $"Entries: {_activeTournament.entries.Count}  State: {_activeTournament.state}  Band: {_activeTournament.scoreBand}",
                MessageType.Info);
        }
    }

    // ─────────────────────────────────────────────────────────────
    private void DrawResolutionSection()
    {
        EditorGUILayout.LabelField("Match Resolution", EditorStyles.boldLabel);

        // ── Service-based (uses ArenaTournamentService) ──
        bool hasServiceRecord = ArenaTournamentService.HasActiveRecord;

        EditorGUILayout.LabelField("Service (live entry flow)", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = hasServiceRecord;
        if (GUILayout.Button("Resolve Next Round", GUILayout.Height(26)))
        {
            int r = ArenaTournamentService.ResolveNextRound();
            if (r >= 0) Debug.Log($"Service resolved round {r}.");
            _standingsText = "";
            // Sync local reference
            _activeTournament = ArenaTournamentService.GetActiveRecord();
        }
        if (GUILayout.Button("Resolve ALL Rounds", GUILayout.Height(26)))
        {
            ArenaTournamentService.ResolveAllRounds();
            _standingsText = "";
            _activeTournament = ArenaTournamentService.GetActiveRecord();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (!hasServiceRecord)
            EditorGUILayout.HelpBox("Enter a tournament via the UI or \"Enter via Service\" below, then resolve here.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Enter via Service", GUILayout.Height(22)))
        {
            if (ArenaTournamentService.TryEnterTournament(out string err))
            {
                _activeTournament = ArenaTournamentService.GetActiveRecord();
                _standingsText = "";
                Debug.Log("Entered tournament via service.");
            }
            else
            {
                Debug.LogWarning($"Entry failed: {err}");
            }
        }
        GUI.enabled = hasServiceRecord;
        if (GUILayout.Button("Discard Service Record", GUILayout.Height(22)))
        {
            ArenaTournamentService.DiscardActiveRecord();
            _activeTournament = null;
            _standingsText = "";
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // ── Legacy manual (uses local _activeTournament from fake tournament) ──
        EditorGUILayout.LabelField("Manual (fake tournament)", EditorStyles.miniLabel);
        bool hasTournament = _activeTournament != null;
        GUI.enabled = hasTournament;

        EditorGUILayout.BeginHorizontal();
        _roundIndex = EditorGUILayout.IntSlider("Round", _roundIndex, 0, ArenaConstants.TotalRounds - 1);
        if (GUILayout.Button("Resolve Round", GUILayout.Width(110), GUILayout.Height(18)))
        {
            ArenaDebugHelper.SimulateRound(_activeTournament, _roundIndex);
            _standingsText = "";
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Instantly Complete Tournament", GUILayout.Height(26)))
        {
            ArenaDebugHelper.InstantlyCompleteTournament(_activeTournament);
            _standingsText = "";
        }

        GUI.enabled = true;

        if (!hasTournament)
            EditorGUILayout.HelpBox("Create a fake tournament first.", MessageType.Info);
    }

    // ─────────────────────────────────────────────────────────────
    private void DrawInspectSection()
    {
        EditorGUILayout.LabelField("Inspect", EditorStyles.boldLabel);

        bool hasTournament = _activeTournament != null;
        GUI.enabled = hasTournament;

        if (GUILayout.Button("Show Standings", GUILayout.Height(24)))
        {
            _standingsText = ArenaDebugHelper.InspectStandings(_activeTournament);
            Debug.Log(_standingsText);
        }

        GUI.enabled = true;

        if (!string.IsNullOrEmpty(_standingsText))
        {
            EditorGUILayout.TextArea(_standingsText, GUILayout.MinHeight(120));
        }
    }

    // ─────────────────────────────────────────────────────────────
    private void DrawDangerZone()
    {
        EditorGUILayout.LabelField("Danger Zone", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Arena History", GUILayout.Height(24)))
            ArenaDebugHelper.ClearArenaHistory();
        if (GUILayout.Button("Full Arena Reset", GUILayout.Height(24)))
        {
            if (EditorUtility.DisplayDialog("Full Arena Reset",
                "This will wipe ALL arena save data. Continue?", "Reset", "Cancel"))
            {
                ArenaDebugHelper.FullArenaReset();
                _activeTournament = null;
                _standingsText = "";
                _saveStateText = "";
            }
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
