using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(Button))]
public sealed class EncounterButtonGuard : MonoBehaviour
{
    [Header("Requirements")]
    [SerializeField, Min(1)] private int minRequiredTeamMembers = 1;

    [Header("Tutorial Override")]
    [Tooltip("If true, this guard will not fight the tutorial gating while the tutorial is active.")]
    [SerializeField] private bool ignoreGuardWhileTutorialActive = true;

    [Tooltip("When the tutorial is active, force the Encounter button to this interactable state.")]
    [SerializeField] private bool tutorialForcedInteractable = false;

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

        _button.onClick.AddListener(OnButtonClicked);
        Apply();
    }

    private void OnDisable()
    {
        EncounterManager.OnEnergyGained -= HandleEnergy;
        GameEvents.EnergyChanged -= HandleEnergy;
        GameEvents.OnTeamChanged -= HandleTeamChanged;

        _button.onClick.RemoveListener(OnButtonClicked);
    }

    private void HandleEnergy(int a, int b) => Apply();
    private void HandleEnergy() => Apply();
    private void HandleTeamChanged() => Apply();

    private void Apply()
    {
        if (_button == null) return;

        // Tutorial should fully control gating during onboarding.
        if (ignoreGuardWhileTutorialActive && TutorialManager.IsActive)
        {
            _button.interactable = tutorialForcedInteractable;
            return;
        }

        bool hasTeam = HasMinimumTeam(minRequiredTeamMembers);
        _button.interactable = hasTeam;
    }

    private void OnButtonClicked()
    {
        // If tutorial is active and we are forcing this disabled, do nothing (avoid shake/feedback).
        if (ignoreGuardWhileTutorialActive && TutorialManager.IsActive && !tutorialForcedInteractable)
            return;

        // Existing behavior: if clicked without enough energy, shake feedback.
        if (!HasRequiredEnergy())
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

    private static bool HasRequiredEnergy()
    {
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
