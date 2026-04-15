// RegisterForTournament.js
// Deploy to UGS Cloud Code: Dashboard → Cloud Code → Scripts → Create
//
// Registers a player for the current week's arena tournament.
// Stores the player's team snapshot + metadata in shared Game Data
// so it can be grouped into a bracket when registration closes.
//
// Game Data entities used:
//   "tournament_reg_{weekId}"  — key: playerId, value: JSON registration blob
//
// Parameters:
//   teamSnapshotJson (STRING) — JSON-serialized ArenaTeamSnapshot
//   arenaScore       (NUMERIC) — pre-computed team arena score
//   scoreBand        (NUMERIC) — 0=Low, 1=Standard, 2=High, 3=Elite
//   displayName      (STRING)  — player's arena username
//   weekId           (STRING)  — client-computed week ID (e.g., "W20260413")
//
// Returns:
//   { success: true, weekId }                     — registration stored
//   { success: false, error: "reason" }           — validation failure

const { DataApi } = require("@unity-services/cloud-save-1.4");

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const { teamSnapshotJson, arenaScore, scoreBand, displayName, weekId } =
    params;

  // ── Validate parameters ──

  if (!teamSnapshotJson || typeof teamSnapshotJson !== "string") {
    return { success: false, error: "Team snapshot is required." };
  }

  if (typeof arenaScore !== "number" || arenaScore < 0) {
    return { success: false, error: "Invalid arena score." };
  }

  if (typeof scoreBand !== "number" || scoreBand < 0 || scoreBand > 3) {
    return { success: false, error: "Invalid score band." };
  }

  if (!displayName || typeof displayName !== "string") {
    return { success: false, error: "Display name is required." };
  }

  if (!weekId || typeof weekId !== "string" || !weekId.startsWith("W")) {
    return { success: false, error: "Invalid week ID." };
  }

  // ── Validate registration window ──

  const serverWeekId = getCurrentWeekId();
  if (weekId !== serverWeekId) {
    return {
      success: false,
      error: "Registration week mismatch. Please refresh.",
    };
  }

  if (!isRegistrationOpen()) {
    return { success: false, error: "Registration is currently closed." };
  }

  // ── Validate team snapshot parses ──

  let snapshot;
  try {
    snapshot = JSON.parse(teamSnapshotJson);
  } catch (e) {
    return { success: false, error: "Invalid team snapshot format." };
  }

  if (
    !snapshot.slotSnapshots ||
    !Array.isArray(snapshot.slotSnapshots) ||
    snapshot.slotSnapshots.length !== 3
  ) {
    return { success: false, error: "Team must have exactly 3 Bitlings." };
  }

  // ── Store registration ──

  const cloudSave = new DataApi(context);
  const regEntity = `tournament_reg_${weekId}`;

  const registrationData = {
    playerId,
    displayName: displayName.trim(),
    teamSnapshotJson, // Store raw JSON to avoid re-serialisation issues
    arenaScore,
    scoreBand,
    registeredUtc: Math.floor(Date.now() / 1000),
  };

  try {
    await cloudSave.setPrivateCustomItem(projectId, regEntity, {
      key: playerId,
      value: JSON.stringify(registrationData),
    });
    logger.info(
      `Player ${playerId} registered for ${weekId} (band=${scoreBand}, score=${arenaScore})`,
    );
  } catch (e) {
    logger.error("Failed to store registration: " + e.message);
    return { success: false, error: "Server error saving registration." };
  }

  return { success: true, weekId };
};

// ═══════════════════════════════════════════════════════════════
//  Schedule helpers (ET timezone via Intl)
// ═══════════════════════════════════════════════════════════════

function getETComponents() {
  const now = new Date();
  const parts = {};
  for (const p of new Intl.DateTimeFormat("en-US", {
    timeZone: "America/New_York",
    weekday: "short",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).formatToParts(now)) {
    parts[p.type] = p.value;
  }
  return parts;
}

const DAY_MAP = { Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6, Sun: 0 };

function isRegistrationOpen() {
  const et = getETComponents();
  const dow = DAY_MAP[et.weekday];
  // Mon (1) or Tue (2)
  return dow === 1 || dow === 2;
}

function getCurrentWeekId() {
  const et = getETComponents();
  const year = parseInt(et.year);
  const month = parseInt(et.month) - 1;
  const day = parseInt(et.day);
  const dow = DAY_MAP[et.weekday]; // 0=Sun, 1=Mon, ... 6=Sat

  // Monday-based offset
  const mondayOffset = dow === 0 ? -6 : 1 - dow;
  const mondayDate = new Date(year, month, day + mondayOffset);

  const y = mondayDate.getFullYear();
  const m = String(mondayDate.getMonth() + 1).padStart(2, "0");
  const d = String(mondayDate.getDate()).padStart(2, "0");
  return `W${y}${m}${d}`;
}
