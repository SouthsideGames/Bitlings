# Arena Server Authority — Deployment Runbook

**The live arena backend is the C# Cloud Code module in `/ArenaModule`.** The
`.js` files under `Assets/CloudCode/` are the older JavaScript prototype that was
ported to that module — they are NOT deployed (the client calls
`CallModuleEndpointAsync("ArenaModule", ...)`, which only resolves to a C#
module). All server fixes below are in the C# module.

## What each gap's status is

| Gap | Risk | Status |
|-----|------|--------|
| 1. Forgeable leaderboard scores | Anyone could write `placement=1` weekly | **Fixed on this branch** — new `SubmitTournamentResult` handler + client wiring |
| 2. Bracket sandbagging | Deflate score → easier bracket | **Already handled** in the module (`SnapshotValidator` + server band recompute) — needs catalogs uploaded |
| 3. Future-week lock griefing | Pre-lock a future week → arena breaks | **Fixed on this branch** — current-week guard in `GetTournamentBracket` / `LockAndAssignBrackets` |

## What changed on this branch

**Cloud Code module (`/ArenaModule/ArenaModule/`)**
- `Handlers/SubmitTournamentResult.cs` (new) — verifies a player's claimed
  placement against the standings the bracket's *other real players* independently
  computed (consensus), then writes the weekly score and an authoritative all-time
  championship counter via `api.Leaderboards.AddLeaderboardPlayerScoreAsync`. The
  server becomes the only leaderboard writer.
- `Handlers/GetTournamentBracket.cs` / `Handlers/LockAndAssignBrackets.cs` — reject
  any `weekId` that isn't the current server week before lazy-locking / locking.
- `Models.cs` — added `SubmitResultOutcome` and `StoredResult`.

**Client (`Assets/Scripts/`)**
- On tournament completion the client no longer writes leaderboards. It queues its
  computed standings (`ArenaSaveData.pendingResultSubmission`, persisted) and
  submits to `SubmitTournamentResult`, retrying on each arena open until the server
  returns a terminal status (`ArenaTournamentService.TrySubmitPendingResultAsync`,
  wired into `ArenaMainPanelUI.TrySyncBracketOnOpen`).
- `ArenaLeaderboardService.SubmitWeeklyPlacementAsync` /
  `SubmitAllTimeChampionshipsAsync` are marked `[Obsolete]`; the read methods stay.

## Deployment steps (in order; each is independently safe to ship)

### Step 1 — deploy the updated module
The module deploys the same way you deploy it today. Two options:

- **Unity Editor**: open the **Deployment** window (Window → Deployment, from the
  `com.unity.services.deployment` package), tick the ArenaModule, and press Deploy.
- **UGS CLI**: `dotnet publish ArenaModule/ArenaModule -c Release` then
  `ugs deploy ArenaModule/ArenaModule` (or the `.ccm`/publish output per your
  existing setup — see `ArenaModule/ArenaModule/Properties/PublishProfiles/FolderProfile.pubxml`).

This single deploy ships the new `SubmitTournamentResult` endpoint AND the
current-week guards (Gaps 1 and 3). Old clients keep working — they just don't
call the new endpoint yet.

**Before deploying, do one build** (`dotnet build ArenaModule/ArenaModule`). The
`.csproj` is not committed to git, so build locally with your existing project
file. The one call to verify is in `SubmitTournamentResult.WriteLeaderboardScore`:

```csharp
await api.Leaderboards.AddLeaderboardPlayerScoreAsync(
    ctx, ctx.ServiceToken, Guid.Parse(ctx.ProjectId),
    leaderboardId, playerId, new LeaderboardScore(score));
```

This matches Unity's documented Cloud Code Leaderboards C# API. If your pinned
`Com.Unity.Services.CloudCode.Apis` version (currently `1.0.2-alpha`) exposes a
slightly different signature, that's the only line to adjust.

### Step 2 — upload the catalogs (enables Gap 2 validation)
`SnapshotValidator` and the server band recompute only run when the catalogs exist
in Cloud Save. In the Unity editor:
1. **Tools → Arena → Export Reference Data**
2. Enter Play Mode (to authenticate UGS), then **Tools → Arena → Upload Catalogs to Cloud**

This writes the `arena_catalogs` entity (keys `monsters`, `titles`). Until it's
uploaded, `SnapshotValidator` logs a warning and skips score validation (safe
degradation). Re-run whenever monster/title arena scores change.

Lock down the `UploadCatalogs` function so only you can call it — in the UGS
dashboard **Access Control**, deny player tokens access to it (it writes trusted
game data).

### Step 3 — ship the client, then lock the leaderboards
1. Release the client build from this branch. New clients submit results via the
   endpoint; until Step 1 is deployed they keep the result pending and retry (no
   data lost).
2. Once analytics show old clients have mostly upgraded, add a UGS **Access
   Control** policy that DENIES player tokens the leaderboard *submit score* action
   for `arena_weekly` and `arena_alltime`. The module runs under the service
   context and is unaffected, so `SubmitTournamentResult` stays the only writer.
   Check current UGS docs for the exact resource-identifier syntax — it has changed
   across versions, so it isn't hard-coded here.

After Step 3, forged scores are impossible: the client cannot write, and the
endpoint only accepts a placement corroborated by the bracket's other real players.

## Known limitation of consensus scoring (documented, acceptable for v1)

`SubmitTournamentResult` trusts a placement once `ConsensusQuorum` (2) real players
in the bracket submit identical standings.
- **Tiny brackets**: a bracket with only 1 real player (31 bots) can't reach
  quorum, so that player is scored on their own submission (logged). Low risk —
  they beat 31 bots to place.
- **Collusion**: a group of modified clients equal to or larger than the honest
  player count in one 32-bracket could submit identical forged standings and reach
  quorum. High effort for a weekly placement score.

Closing both fully needs the server to *reproduce* the bracket result itself, which
means porting the bot generator + match resolver + battle simulator to run in the
module with output bit-identical to the C# client. The blocker is float
determinism (the simulator uses `float` damage math and `System.Random`); an exact
port is a real project with golden-vector testing, not wired up here. The clean
path if you ever want it: make the simulator integer-deterministic first, then the
server can resolve brackets exactly and consensus can be dropped entirely.
