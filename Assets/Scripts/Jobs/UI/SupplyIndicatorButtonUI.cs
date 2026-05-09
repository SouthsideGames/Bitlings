using UnityEngine;
using UnityEngine.UI;

public sealed class SupplyIndicatorButtonUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Button indicatorButton;

    [Header("State Icons")]
    [SerializeField] private Sprite scarcityIcon;
    [SerializeField] private Sprite lowSupplyIcon;
    [SerializeField] private Sprite surplusIcon;
    [SerializeField] private Sprite glutIcon;

    private JobType _jobType;
    private ResourceType _resource;
    private SupplyState _currentState;
    private bool _initialized;

    public void Init(JobType job)
    {
        if (_initialized && _jobType == job) return;

        _jobType = job;
        _resource = JobOutput.Output(job);
        _initialized = true;

        if (indicatorButton != null)
        {
            indicatorButton.onClick.RemoveAllListeners();
            indicatorButton.onClick.AddListener(OnButtonPressed);
        }

        Refresh();
    }

    public void Refresh()
    {
        if (SupplyIndexSystem.I == null || !_initialized) return;
        if (iconImage == null) return;

        float index = SupplyIndexSystem.I.GetIndex(_resource);
        _currentState = SupplyIndexSystem.GetSupplyState(index);

        if (_currentState == SupplyState.Balanced)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        switch (_currentState)
        {
            case SupplyState.Scarcity:
                iconImage.sprite = scarcityIcon;
                break;
            case SupplyState.LowSupply:
                iconImage.sprite = lowSupplyIcon;
                break;
            case SupplyState.Surplus:
                iconImage.sprite = surplusIcon;
                break;
            case SupplyState.Glut:
                iconImage.sprite = glutIcon;
                break;
        }

        LeanTween.cancel(iconImage.gameObject);
        iconImage.rectTransform.localScale = Vector3.one;

        if (_currentState == SupplyState.Scarcity)
        {
            LeanTween.scale(iconImage.rectTransform, Vector3.one * 1.1f, 0.55f)
                .setEase(LeanTweenType.easeInOutSine)
                .setLoopPingPong()
                .setIgnoreTimeScale(true);
        }
    }

    private void OnEnable()
    {
        GameEvents.ExchangeValuesChanged += Refresh;
        GameEvents.OnJobsChanged += Refresh;
    }

    private void OnDisable()
    {
        GameEvents.ExchangeValuesChanged -= Refresh;
        GameEvents.OnJobsChanged -= Refresh;

        if (iconImage != null)
            LeanTween.cancel(iconImage.gameObject);
    }

    private void OnButtonPressed()
    {
        if (TooltipUI.I == null || SupplyIndexSystem.I == null) return;

        float index = SupplyIndexSystem.I.GetIndex(_resource);
        string resourceName = JobStrings.ResourceName(_resource);
        int displayIndex = Mathf.RoundToInt(index);

        string title = GetTitle(_currentState);
        string body = GetBody(_currentState, resourceName, displayIndex);

        string finalText = $"<b>{title}</b>\n{body}";
        TooltipUI.I.Show(finalText);
    }

    private static string GetTitle(SupplyState state)
    {
        switch (state)
        {
            case SupplyState.Scarcity: return "Resource Scarce";
            case SupplyState.LowSupply: return "Low Supply";
            case SupplyState.Surplus: return "Surplus";
            case SupplyState.Glut: return "Market Flooded";
            default: return "Balanced";
        }
    }

    private static string GetBody(SupplyState state, string resourceName, int index)
    {
        switch (state)
        {
            case SupplyState.Scarcity:
                return $"{resourceName} is critically scarce ({index}/100). Sell price is +40% - workers here are highly valuable right now.";
            case SupplyState.LowSupply:
                return $"{resourceName} supply is low ({index}/100). Sell price is +20% - a good time to have monsters working here.";
            case SupplyState.Surplus:
                return $"Too many workers are producing {resourceName} ({index}/100). Sell price is -20% - consider moving some monsters to a different job.";
            case SupplyState.Glut:
                return $"The {resourceName} market is flooded ({index}/100). Sell price is -40% - reassign your workers to a scarcer resource.";
            default:
                return $"{resourceName} supply is healthy ({index}/100). Prices are stable.";
        }
    }
}
