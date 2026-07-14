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

    // ADMIN GATE: only these UGS player IDs may upload catalogs. This is an
    // in-code alternative to a UGS Access Control policy, so no dashboard/CLI
    // setup is required to keep players from overwriting the trusted catalogs.
    //
    // To find your own player ID: run the editor "Upload Catalogs to Cloud" tool
    // once — this handler logs "UploadCatalogs called by <playerId>" (visible in
    // the UGS dashboard Cloud Code logs). Copy that ID into the array below and
    // redeploy the module. While the array is EMPTY, uploads are allowed from any
    // authenticated caller (convenient for first-time setup) but a warning is
    // logged — lock it down before launch.
    private static readonly string[] AdminPlayerIds =
    {
        // "add-your-ugs-player-id-here",
    };

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
        // ── Admin gate ──
        _logger.LogInformation("UploadCatalogs called by {PlayerId}", ctx.PlayerId);
        if (AdminPlayerIds.Length == 0)
        {
            _logger.LogWarning(
                "UploadCatalogs has no admin allow-list configured — allowing this call. " +
                "Add your player ID to AdminPlayerIds and redeploy before launch.");
        }
        else if (Array.IndexOf(AdminPlayerIds, ctx.PlayerId) < 0)
        {
            _logger.LogWarning("UploadCatalogs denied for non-admin player {PlayerId}.", ctx.PlayerId);
            return new UploadCatalogsResult { Success = false, Error = "Not authorized." };
        }

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
