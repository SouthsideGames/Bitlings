// ArenaModule — Dependency injection setup
// Registers IGameApiClient so handlers can call Cloud Save, Leaderboards, etc.

using Microsoft.Extensions.DependencyInjection;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace ArenaModule;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.Dependencies.AddSingleton<IGameApiClient>(GameApiClient.Create());
    }
}
