// Assets/Scripts/UI/EncounterButtonGuard.cs
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class EncounterButtonGuard : MonoBehaviour
{
    [Header("Rules")]
    [SerializeField, Min(1)] private int minRequiredTeamMembers = 1;
    [SerializeField, Range(0.05f, 1f)] private float pollInterval = 0.2f;

    private Button _button;
    private float _timer;

    private void Awake()
    {
        _button = GetComponent<Button>();
        Apply();
    }

    private void OnEnable()
    {
        _timer = pollInterval; // force refresh next frame
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer >= pollInterval)
        {
            _timer = 0f;
            Apply();
        }
    }

    private void Apply()
    {
        bool hasValidTeam = false;
        var data = SaveManager.Data;

        if (data != null && data.team != null)
        {
            int validCount = 0;
            for (int i = 0; i < data.team.Count; i++)
            {
                var entry = data.team[i];
                if (entry != null && !string.IsNullOrEmpty(entry.monsterId))
                {
                    validCount++;
                    if (validCount >= minRequiredTeamMembers)
                    {
                        hasValidTeam = true;
                        break;
                    }
                }
            }
        }

        _button.interactable = hasValidTeam;
    }
}
