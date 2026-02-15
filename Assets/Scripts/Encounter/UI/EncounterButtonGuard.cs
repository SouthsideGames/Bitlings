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

    // EncounterManager can be created/destroyed across scenes.
    // Hook its OnStateChanged so the button refreshes when battles start/end.
    private EncounterManager _hookedEncounter;

    private void Awake()
    {
        _button = GetComponent<Button>();
        if (!shakeTarget) shakeTarget = GetComponent<RectTransform>();
        Apply();
    }

    private void Start()
    {
        // In case EncounterManager comes up after this UI.
        TryHookEncounterEvents();
        Apply();
    }

    private void OnEnable()
    {
        EncounterManager.OnEnergyGained += HandleEnergy;
        GameEvents.EnergyChanged += HandleEnergy;
        GameEvents.OnTeamChanged += HandleTeamChanged;
        GameEvents.OnTeamHealthChanged += HandleTeamChanged;

        _button.onClick.AddListener(OnButtonClicked);

        TryHookEncounterEvents();
        Apply();
    }

    private void OnDisable()
    {
        EncounterManager.OnEnergyGained -= HandleEnergy;
        GameEvents.EnergyChanged -= HandleEnergy;
        GameEvents.OnTeamChanged -= HandleTeamChanged;
        GameEvents.OnTeamHealthChanged -= HandleTeamChanged;

        UnhookEncounterEvents();

        _button.onClick.RemoveListener(OnButtonClicked);
    }

    private void Update()
    {
        // Cheap guard: if EncounterManager instance changes across scenes, re-hook.
        // This prevents the button getting stuck disabled after a battle when no
        // energy/team events fire.
        if (_hookedEncounter != EncounterManager.I)
            TryHookEncounterEvents();
    }

    private void TryHookEncounterEvents()
    {
        var em = EncounterManager.I;
        if (em == null)
        {
            UnhookEncounterEvents();
            return;
        }

        if (_hookedEncounter == em) return;

        UnhookEncounterEvents();
        _hookedEncounter = em;
        _hookedEncounter.OnStateChanged += Apply;
    }

    private void UnhookEncounterEvents()
    {
        if (_hookedEncounter != null)
        {
            _hookedEncounter.OnStateChanged -= Apply;
            _hookedEncounter = null;
        }
    }

    private void HandleEnergy(int a, int b) => Apply();
    private void HandleEnergy() => Apply();
    private void HandleTeamChanged() => Apply();

    private void Apply()
    {
        if (_button == null) return;

        _button.interactable = EligibilityRules.CanStartEncounter(minRequiredTeamMembers, out _);
    }

    private void OnButtonClicked()
    {
        // If the button was force-enabled by some other script, still guard click feedback.
        if (!EligibilityRules.CanStartEncounter(minRequiredTeamMembers, out _))
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

}