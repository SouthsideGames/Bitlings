// UploadCatalogs.js
// Deploy to UGS Cloud Code (same module as the other Arena scripts).
//
// Admin/tooling endpoint. Called by the Unity editor tool
// (Tools → Arena → Upload Catalogs to Cloud) to store the authoritative
// monster / title / type-chart catalogs in project-scoped Cloud Save custom
// data. RegisterForTournament reads these to recompute each team's arena score
// and score band SERVER-SIDE, so a modified client cannot sandbag its band.
//
// SECURITY: this writes trusted game data, so it must NOT be callable by
// ordinary players. Restrict it in the UGS dashboard (Access Control) to your
// admin/service token, OR gate it on an allow-listed player id below.
//
// Game Data entities written (project-scoped shared custom data):
//   "arena_catalog" — key "monsters"   → monster catalog JSON
//                     key "titles"      → title catalog JSON
//                     key "typechart"   → type chart JSON
//
// Parameters:
//   monsterCatalogJson (STRING)
//   titleCatalogJson   (STRING)
//   typeChartJson      (STRING, optional — recommended so synergy scoring matches the client)
//
// Returns: { success: true } | { success: false, error }

const { DataApi } = require("@unity-services/cloud-save-1.4");

// OPTIONAL hard gate: if non-empty, only these playerIds may upload. Leave empty
// and rely on dashboard Access Control instead (preferred).
const ADMIN_PLAYER_IDS = [];

const CATALOG_ENTITY = "arena_catalog";

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;

  if (ADMIN_PLAYER_IDS.length > 0 && !ADMIN_PLAYER_IDS.includes(playerId)) {
    logger.warn(`UploadCatalogs denied for non-admin player ${playerId}.`);
    return { success: false, error: "Not authorized." };
  }

  const { monsterCatalogJson, titleCatalogJson, typeChartJson } = params;

  if (!monsterCatalogJson || typeof monsterCatalogJson !== "string") {
    return { success: false, error: "monsterCatalogJson is required." };
  }
  if (!titleCatalogJson || typeof titleCatalogJson !== "string") {
    return { success: false, error: "titleCatalogJson is required." };
  }

  // Validate they parse before storing (a broken catalog would silently disable
  // server-side scoring).
  try {
    JSON.parse(monsterCatalogJson);
    JSON.parse(titleCatalogJson);
    if (typeChartJson) JSON.parse(typeChartJson);
  } catch (e) {
    return { success: false, error: "Catalog JSON did not parse: " + e.message };
  }

  const cloudSave = new DataApi(context);

  try {
    await cloudSave.setPrivateCustomItem(projectId, CATALOG_ENTITY, {
      key: "monsters",
      value: monsterCatalogJson,
    });
    await cloudSave.setPrivateCustomItem(projectId, CATALOG_ENTITY, {
      key: "titles",
      value: titleCatalogJson,
    });
    if (typeChartJson) {
      await cloudSave.setPrivateCustomItem(projectId, CATALOG_ENTITY, {
        key: "typechart",
        value: typeChartJson,
      });
    }
  } catch (e) {
    logger.error("Failed to store catalogs: " + e.message);
    return { success: false, error: "Server error storing catalogs." };
  }

  logger.info(`Catalogs uploaded by ${playerId}.`);
  return { success: true };
};
