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

    // ADMIN GATE (shared secret). Only callers that present this exact secret may
    // overwrite the trusted catalogs. This is an in-code alternative to a UGS
    // Access Control policy — no dashboard/CLI setup needed.
    //
    // It is secure because the ONLY caller is the Unity Editor tool
    // (ArenaDataExporter, wrapped in #if UNITY_EDITOR), which never ships in a
    // player build — so the secret is not extractable from the game client.
    //
    // SETUP: replace the value below with your own random string, and paste the
    // SAME string into ArenaDataExporter.CatalogUploadSecret. Keep them in sync.
    // (If your GitHub repo is public, treat this like any secret and rotate it.)
    private const string ExpectedSecret = "bl-arena-46d8962840c6ee61bbe6c22180920a3d";

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
        string titleCatalogJson,
        string adminSecret = "")
    {
        // ── Admin gate ──
        if (adminSecret != ExpectedSecret)
        {
            _logger.LogWarning("UploadCatalogs denied (bad or missing secret) for {PlayerId}.", ctx.PlayerId);
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
