# Arena Server Authority — Deployment Runbook

This branch makes arena scoring server-authoritative. The code is written; the
remaining work is **deploying the Cloud Code scripts and setting one dashboard
policy**. Follow the steps in order — each is safe to ship on its own, and old
clients keep working throughout.

## What changed in this repo

**Client (`Assets/Scripts/`)**
- The client no longer writes leaderboards directly. On tournament completion it
  builds the full final standings and queues them (`ArenaSaveData.pendingResultSubmission`),
  then submits to the new `SubmitTournamentResult` endpoint
  (`ArenaTournamentService.BuildPendingSubmission` / `TrySubmitPendingResultAsync`).
  Submission is retried on every arena open until the server returns a terminal
  status, so a result awaiting consensus survives app restarts.
- `ArenaLeaderboardService.SubmitWeeklyPlacementAsync` /
  `SubmitAllTimeChampionshipsAsync` are marked `[Obsolete]` — the read methods stay.

**Cloud Code (`Assets/CloudCode/`)**
- `SubmitTournamentResult.js` (new) — verifies a player's claimed placement against
  the standings other real players in the same bracket independently computed
  (consensus), then writes the weekly score and maintains an authoritative all-time
  championship counter. The server is the only leaderboard writer.
- `UploadCatalogs.js` (new) — stores the monster/title/type-chart catalogs the
  editor tool exports, so registration can score teams server-side.
- `RegisterForTournament.js` — now recomputes each team's arena score + band from
  the trusted catalog (ignoring the client's claimed numbers). Falls back to the
  client value only if the catalog hasn't been uploaded yet (logged).
- `GetTournamentBracket.js` / `LockAndAssignBrackets.js` — reject any weekId that
  isn't the current server week (fixes the future-week lock-griefing hole).

**Editor (`Assets/Editor/ArenaDataExporter.cs`)**
- "Upload Catalogs to Cloud" now also uploads the type chart (needed for server
  synergy scoring).

## Deployment steps

### Step 1 — redeploy the two hardened bracket scripts (do first, no client release needed)
Deploy `GetTournamentBracket.js` and `LockAndAssignBrackets.js` to your Cloud Code
module. Then check Cloud Save Game Data for any `tournament_lock_*` entities dated
in a **future** week and delete them (leftovers from the griefing bug would block
real brackets).

### Step 2 — upload the catalogs and deploy registration scoring
1. In the Unity editor: **Tools → Arena → Export Reference Data**, then enter Play
   Mode (to authenticate UGS) and **Tools → Arena → Upload Catalogs to Cloud**.
2. Deploy `UploadCatalogs.js` and the updated `RegisterForTournament.js`.
3. Lock down `UploadCatalogs` so only you can call it — either add your admin
   playerId to `ADMIN_PLAYER_IDS` in the script, or deny player-token access to it
   in dashboard Access Control. (It writes trusted game data.)

After this, bracket banding is computed from server-trusted scores; sandbagging by
sending a deflated score no longer works.

### Step 3 — deploy result scoring + ship the client, then lock the leaderboards
1. Deploy `SubmitTournamentResult.js`.
2. **Verify the leaderboard-write call** in that script against your installed SDK.
   It uses `@unity-services/leaderboards-1.4`:
   `new LeaderboardsApi(context).addLeaderboardPlayerScore(projectId, leaderboardId, playerId, { score })`.
   If your project pins a different Leaderboards SDK major version, adjust the
   single `writeLeaderboardScore()` function — that is the only place it writes.
3. Release the client build from this branch. New clients submit via the endpoint;
   until it's deployed they just keep the result pending and retry (no lost data).
4. Once your analytics show old clients have mostly upgraded, set a dashboard
   **Access Control** policy that DENIES player tokens the leaderboard submit-score
   action for `arena_weekly` and `arena_alltime`. Cloud Code (service context) is
   unaffected, so `SubmitTournamentResult` remains the only writer. Check the
   current UGS docs for the exact resource identifier syntax — it has changed
   across versions, so I'm deliberately not hard-coding it here.

At the end of Step 3, forged leaderboard scores are impossible: the client cannot
write, and the endpoint only accepts a placement corroborated by the bracket's
other real players.

## Known limitation of consensus scoring (documented, acceptable for v1)

`SubmitTournamentResult` trusts a placement once `CONSENSUS_QUORUM` (2) real
players in the bracket submit identical standings. Residual gaps:
- **Tiny brackets**: a bracket with only 1 real player (31 bots) can't reach
  quorum, so that player is scored on their own submission (logged). They beat 31
  bots to place, so the risk is low, but a lone player could over-claim placement.
- **Collusion**: if a group of modified clients equal to or larger than the honest
  player count in one 32-bracket submit identical forged standings, they can reach
  quorum. This needs multiple coordinated modified clients landing in the same
  bracket — high effort for a weekly placement score.

Closing both fully requires the server to *reproduce* the bracket result itself.
That means porting `ArenaBotGenerator` + `ArenaMatchResolver` + `ArenaBattleSimulator`
to run server-side with **bit-identical** output to the C# client. The blocker is
float determinism: the simulator uses `float` damage math and `System.Random`, which
don't reproduce identically in JS without a careful port of `System.Random`'s
algorithm and float32 handling, verified against golden vectors generated in Unity.
It's a real project (days, mostly determinism testing), not wired up here. If you
want it, the clean path is to make the simulator integer-deterministic first, then
the JS port becomes exact and consensus can be dropped entirely.
