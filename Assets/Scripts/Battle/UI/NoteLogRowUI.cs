using UnityEngine;
using TMPro;
using System;

public class NoteLogRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Color systemColor = new Color(0.8f, 0.9f, 1f);
    [SerializeField] private Color encounterColor = Color.white;
    [SerializeField] private Color battleColor = Color.white;

    public void Set(LogEntry e)
    {
        if (!label) return;

        var time = DateTimeOffset.FromUnixTimeSeconds(e.unix).ToLocalTime().ToString("HH:mm:ss");
        label.text = $"[{time}] {e.text}";

        switch (e.scope)
        {
            case LogScope.System:    label.color = systemColor;    break;
            case LogScope.Encounter: label.color = encounterColor; break;
            default:                 label.color = battleColor;    break;
        }
    }
}
