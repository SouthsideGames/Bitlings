using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(Button))]
public sealed class EncounterButtonGuard : MonoBehaviour
{
    [Header("Requirements")]
    [SerializeField, Min(1)] private int minRequiredTeamMembers = 1;

    [Header("Feedback")]
    [SerializeField] private RectTransform shakeTarget;
    [SerializeField, Range(0.05f, 0.5f)] private float shakeDuration = 0.2f;
    [SerializeField, Range(1f, 30f)] private float shakeMagnitude = 10f;

    private Button _button;
    private Coroutine _shakeRoutine;

    private void Awake()
    {
        _button = GetComponent<Button>();
        if (!shakeTarget) shakeTarget = GetComponent<RectTransform>();
        Apply();
    }

    private void OnEnable()
    {
        EncounterManager.OnEnergyGained += HandleEnergy;
        GameEvents.EnergyChanged += HandleEnergy;
        GameEvents.OnTeamChanged += HandleTeamChanged;
        GameEvents.OnTeamHealthChanged += HandleTeamChanged;

        _button.onClick.AddListener(OnButtonClicked);
        Apply();
    }

    private void OnDisable()
    {
        EncounterManager.OnEnergyGained -= HandleEnergy;
        GameEvents.EnergyChanged -= HandleEnergy;
        GameEvents.OnTeamChanged -= HandleTeamChanged;
        GameEvents.OnTeamHealthChanged -= HandleTeamChanged;

        _button.onClick.RemoveListener(OnButtonClicked);
    }

    private void HandleEnergy(int a, int b) => Apply();
    private void HandleEnergy() => Apply();
    private void HandleTeamChanged() => Apply();

    private void Apply()
    {
        if (_button == null) return;

        // Encounter is only actionable when:
        //  - We are not currently inside a battle
        //  - The player has at least N team members that are alive (HP != 0)
        //  - The player has enough Energy to pay the encounter cost (unless the next encounter is free)
        bool inBattle = EncounterManager.I != null && EncounterManager.I.IsInBattle;

        bool hasTeamAlive = HasMinimumTeam(minRequiredTeamMembers);
        bool hasEnergyOrFree = HasRequiredEnergyOrFree();

        _button.interactable = !inBattle && hasTeamAlive && hasEnergyOrFree;
    }

    private void OnButtonClicked()
    {
        // If the button was force-enabled by some other script, still guard click feedback.
        if (!HasRequiredEnergyOrFree())
        {
            StartShake();
            return;
        }
    }

    private void StartShake()
    {
        if (!shakeTarget) return;

        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);

        _shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        Vector3 originalPos = shakeTarget.localPosition;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float offsetX = Random.Range(-1f, 1f) * shakeMagnitude;
            float offsetY = Random.Range(-1f, 1f) * shakeMagnitude;

            shakeTarget.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        shakeTarget.localPosition = originalPos;
        _shakeRoutine = null;
    }

    private static bool HasMinimumTeam(int minMembers)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null) return false;

        int count = 0;

        for (int i = 0; i < data.team.Count; i++)
        {
            var entry = data.team[i];
            if (entry != null && !string.IsNullOrEmpty(entry.monsterId))
            {
                if (entry.currentHP != 0)
                {
                    count++;
                    if (count >= minMembers) return true;
                }
            }
        }

        return false;
    }

    private static bool HasRequiredEnergyOrFree()
    {
        // Free encounter bypasses the energy check.
        if (EncounterManager.I != null && EncounterManager.I.NextEncounterIsFree)
            return true;

        int needed = 1;
        int current = 0;

        if (EncounterManager.I != null)
        {
            needed = Mathf.Max(1, EncounterManager.I.GetEncounterCost());
            current = Mathf.Max(0, EncounterManager.I.GetEnergyPoints());
        }
        else
        {
            needed = 1;
            current = Mathf.Max(0, ResourceBank.Get(ResourceType.Energy));
        }

        return current >= needed;
    }
}