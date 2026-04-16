// ArenaModule — UploadCatalogs handler
// Called from the Unity Editor to push monster + title catalog JSON
// into Cloud Save private custom data for server-side validation.
//
// Entity: "arena_catalogs" with keys "monsters" and "titles"
//
// This is an admin/editor-only function — not called by players.

using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace ArenaModule.Handlers;

public class UploadCatalogs
{
    private const string CatalogEntity = "arena_catalogs";

    private readonly ILogger<UploadCatalogs> _logger;

    public UploadCatalogs(ILogger<UploadCatalogs> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Stores monster and title catalog JSON in Cloud Save for server-side validation.
    /// Called from Unity Editor after exporting reference data.
    /// </summary>
    [CloudCodeFunction("UploadCatalogs")]
    public async Task<UploadCatalogsResult> Execute(
        IExecutionContext ctx,
        IGameApiClient api,
        string monsterCatalogJson,
        string titleCatalogJson)
    {
        if (string.IsNullOrWhiteSpace(monsterCatalogJson))
            return new UploadCatalogsResult { Success = false, Error = "Monster catalog is required." };

        if (string.IsNullOrWhiteSpace(titleCatalogJson))
            return new UploadCatalogsResult { Success = false, Error = "Title catalog is required." };

        try
        {
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, CatalogEntity,
                new SetItemBody("monsters", monsterCatalogJson));

            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, CatalogEntity,
                new SetItemBody("titles", titleCatalogJson));

            _logger.LogInformation("Catalogs uploaded to Cloud Save entity \"{Entity}\".", CatalogEntity);
            return new UploadCatalogsResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to upload catalogs: {Error}", ex.Message);
            return new UploadCatalogsResult { Success = false, Error = "Server error storing catalogs." };
        }
    }
}

public class UploadCatalogsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
