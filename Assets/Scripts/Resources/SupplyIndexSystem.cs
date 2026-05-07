using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class SupplyIndexSystem : MonoBehaviour
{
    public static SupplyIndexSystem I { get; private set; }

    [SerializeField] private float baselineRatePerHour = 100f;
    [SerializeField] private float decayPerHour = 2f;
    [SerializeField] private float tickWeight = 1f;
    [SerializeField] private float tickSeconds = 1f;

    private readonly Dictionary<ResourceType, float> _supplyIndex = new Dictionary<ResourceType, float>();
    private float _accum;
    private float _persistTimer;

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }

        I = this;

        foreach (ResourceType res in Enum.GetValues(typeof(ResourceType)))
        {
            if (!_supplyIndex.ContainsKey(res))
                _supplyIndex[res] = 0f;
        }

        LoadFromSave();
    }

    private void Update()
    {
        if (SaveManager.IsHardWiping) return;
        if (IronCareerRuntime.IsActive) return;

        _accum += Time.unscaledDeltaTime;
        if (_accum >= tickSeconds)
        {
            Tick(_accum);
            _accum = 0f;
        }

        _persistTimer += Time.unscaledDeltaTime;
        if (_persistTimer >= 300f)
        {
            _persistTimer = 0f;
            PersistToSave();
        }
    }

    public float GetIndex(ResourceType resource)
    {
        return _supplyIndex.TryGetValue(resource, out float v) ? v : 50f;
    }

    public static SupplyState GetSupplyState(float index)
    {
        if (index < 20f) return SupplyState.Scarcity;
        if (index < 40f) return SupplyState.LowSupply;
        if (index < 60f) return SupplyState.Balanced;
        if (index < 80f) return SupplyState.Surplus;
        return SupplyState.Glut;
    }

    public SupplyState GetStateForResource(ResourceType resource)
    {
        return GetSupplyState(GetIndex(resource));
    }

    public float GetPriceMultiplier(ResourceType resource)
    {
        switch (GetStateForResource(resource))
        {
            case SupplyState.Scarcity: return 1.4f;
            case SupplyState.LowSupply: return 1.2f;
            case SupplyState.Balanced: return 1.0f;
            case SupplyState.Surplus: return 0.8f;
            case SupplyState.Glut: return 0.6f;
            default: return 1.0f;
        }
    }

    private void Tick(float dtSeconds)
    {
        float dtHours = dtSeconds / 3600f;

        var productionByResource = new Dictionary<ResourceType, float>();

        if (JobManager.I != null && JobManager.I.States != null)
        {
            foreach (JobSiteState s in JobManager.I.States)
            {
                if (s == null || s.config == null) continue;
                if (s.cachedRatePerHour <= 0f) continue;

                ResourceType res = JobOutput.Output(s.config.jobType);
                float contribution = (s.cachedRatePerHour / baselineRatePerHour) * tickWeight;

                if (productionByResource.ContainsKey(res))
                    productionByResource[res] += contribution;
                else
                    productionByResource[res] = contribution;
            }
        }

        var keys = new List<ResourceType>(_supplyIndex.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            ResourceType res = keys[i];
            float production = productionByResource.TryGetValue(res, out float p) ? p : 0f;

            if (production > 0f)
                _supplyIndex[res] = Mathf.Clamp(_supplyIndex[res] + production * dtHours * 60f, 0f, 100f);
            else
                _supplyIndex[res] = Mathf.Clamp(_supplyIndex[res] - decayPerHour * dtHours, 0f, 100f);
        }

        GameEvents.ExchangeValuesChanged?.Invoke();
    }

    private void PersistToSave()
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.supplyIndexMap ??= new Dictionary<int, float>();
        SaveManager.Data.supplyIndexMap.Clear();

        foreach (var kv in _supplyIndex)
            SaveManager.Data.supplyIndexMap[(int)kv.Key] = kv.Value;

        SaveManager.Save();
    }

    private void LoadFromSave()
    {
        if (SaveManager.Data?.supplyIndexMap == null) return;

        foreach (var kv in SaveManager.Data.supplyIndexMap)
        {
            ResourceType res = (ResourceType)kv.Key;
            if (_supplyIndex.ContainsKey(res))
                _supplyIndex[res] = Mathf.Clamp(kv.Value, 0f, 100f);
        }
    }
}
