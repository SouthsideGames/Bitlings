// ValidateAndSetUsername.js
// Deploy to UGS Cloud Code: Dashboard → Cloud Code → Scripts → Create
//
// This server-side script validates a candidate arena username for uniqueness
// and, if valid, atomically claims it for the calling player.
//
// Username index is stored in Cloud Save Game Data (Private Custom Items):
//   Entity: "username_index", Key: normalised username, Value: playerId
//
// Parameters:
//   username (string) — the candidate display name
//
// Returns:
//   { success: true }                         — username claimed
//   { success: false, error: "reason" }       — validation or uniqueness failure

const { DataApi } = require("@unity-services/cloud-save-1.4");

const MIN_LENGTH = 2;
const MAX_LENGTH = 16;
const CUSTOM_ID = "username_index";

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const username = params.username;

  // ── Client-side validation (belt-and-suspenders) ──

  if (!username || typeof username !== "string") {
    return { success: false, error: "Username is required." };
  }

  const trimmed = username.trim();

  if (trimmed.length < MIN_LENGTH) {
    return {
      success: false,
      error: `Name must be at least ${MIN_LENGTH} characters.`,
    };
  }

  if (trimmed.length > MAX_LENGTH) {
    return {
      success: false,
      error: `Name must be at most ${MAX_LENGTH} characters.`,
    };
  }

  if (!isUsernameSafe(trimmed)) {
    return { success: false, error: "Name contains invalid characters." };
  }

  // ── Single service-scoped client (handles both player and game data) ──
  const cloudSave = new DataApi(context);

  // ── Check if this player already has a username ──

  let existingData;
  try {
    existingData = await cloudSave.getItems(projectId, playerId, ["arena_v1"]);
  } catch (e) {
    logger.error("Failed to read player data: " + e.message);
    return { success: false, error: "Server error reading player data." };
  }

  if (existingData?.data?.results) {
    for (const item of existingData.data.results) {
      if (item.key === "arena_v1" && item.value) {
        let arenaData;
        try {
          arenaData =
            typeof item.value === "string"
              ? JSON.parse(item.value)
              : item.value;
        } catch (_) {
          /* ignore parse error */
        }

        if (arenaData && arenaData.usernameCreated && arenaData.arenaUsername) {
          return { success: false, error: "You already have a username." };
        }
      }
    }
  }

  // ── Check uniqueness via Game Data (private custom items) ──

  const nameKey = trimmed.toLowerCase();
  let nameTaken = false;
  let oldNameKey = null; // Track if this player already owns a different name.

  try {
    const indexResult = await cloudSave.getPrivateCustomItems(
      projectId,
      CUSTOM_ID,
    );
    logger.info(
      "Username index read OK. Results: " +
        JSON.stringify(indexResult?.data?.results?.length ?? 0),
    );

    if (indexResult?.data?.results) {
      for (const item of indexResult.data.results) {
        // Check if this player owns a different name (from a previous reset).
        if (item.value === playerId && item.key !== nameKey) {
          oldNameKey = item.key;
        }
        if (item.key === nameKey) {
          if (item.value && item.value !== playerId) {
            nameTaken = true;
          }
          // If value === playerId, this player already owns it (crash retry).
        }
      }
    }
  } catch (e) {
    // 404 means the entity doesn't exist yet — name is available.
    const status = e?.response?.status || e?.status;
    if (status !== 404) {
      logger.error(
        "Failed to check username index: status=" +
          status +
          " message=" +
          e.message,
      );
      return {
        success: false,
        error: "Server error checking name availability.",
      };
    }
    logger.info("Username index entity not found (404) — first username ever.");
  }

  if (nameTaken) {
    return { success: false, error: "That name is already taken." };
  }

  // ── Release old name if this player is switching ──

  if (oldNameKey) {
    try {
      await cloudSave.deletePrivateCustomItem(projectId, CUSTOM_ID, oldNameKey);
      logger.info(`Released old username index entry: "${oldNameKey}"`);
    } catch (e) {
      // Non-fatal — log but continue.
      logger.error("Failed to release old username: " + e.message);
    }
  }

  // ── Claim the name ──

  try {
    await cloudSave.setPrivateCustomItem(projectId, CUSTOM_ID, {
      key: nameKey,
      value: playerId,
    });
    logger.info(
      `Wrote username index: entity="${CUSTOM_ID}", key="${nameKey}", value="${playerId}"`,
    );
  } catch (e) {
    logger.error("Failed to write username index: " + e.message);
    return { success: false, error: "Server error claiming name." };
  }

  // ── Write to the player's arena data ──

  try {
    let arenaData = {};
    if (existingData?.data?.results) {
      for (const item of existingData.data.results) {
        if (item.key === "arena_v1" && item.value) {
          try {
            arenaData =
              typeof item.value === "string"
                ? JSON.parse(item.value)
                : item.value;
          } catch (_) {
            arenaData = {};
          }
        }
      }
    }

    arenaData.arenaUsername = trimmed;
    arenaData.usernameCreated = true;

    await cloudSave.setItem(projectId, playerId, {
      key: "arena_v1",
      value: JSON.stringify(arenaData),
    });
  } catch (e) {
    logger.error("Failed to write player arena data: " + e.message);
    return {
      success: false,
      error: "Name claimed but profile update failed. Reopen the Arena.",
    };
  }

  logger.info(`Player ${playerId} claimed username "${trimmed}"`);
  return { success: true };
};

function isUsernameSafe(name) {
  if (!name || name.trim() !== name) return false;
  for (let i = 0; i < name.length; i++) {
    const c = name.charCodeAt(i);
    if (c < 32) return false; // control characters
    if (c === 60 || c === 62) return false; // < >
  }
  return true;
}
