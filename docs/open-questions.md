# Open questions and checkpoints

Everything that must be decided or verified before (or while) building
HSJeeves. Source design: [../design/hsjeeves-app.md](../design/hsjeeves-app.md).
Reference material from the old repo (`D:\_dev\My_Projects\HS-Rank-Tracker`):
plugin source `src/plugin/`, contract `src/backend/payload-definition.md`,
mock backend `src/backend/main.py`, HDT checkout under `HDT/`.

How to use: tick `[x]` when settled, and write the answer under **Decision**
(one or two lines). Priority: **B** = blocks building, **R** = risk, **S** = small.

Deadline: HDT plugins stop working after **30 Nov 2026**.

---

## A. Spike and HearthMirror (milestone 1)

- [x] **A1 (B)** Run `HSJeeves.Spike` with Hearthstone running and HDT closed. Does every call return data (`GetAccountId`, `GetBattleTag`, `GetSceneMgrState`, `GetGameType`, `GetFormat`, `GetMatchInfo`, `GetMedalInfo`)?
  - Result (partial, run 1): HearthMirror 32.4.2.0 works standalone, no exceptions, 5/5 samples stable. `AccountId`, `BattleTag` (Deesnow#2794) and `MedalInfo` return data. `GameType=0`, `Format=0`, `MatchInfo=null` because Hearthstone was at `Mode=2` (LOGIN, from `Mode.cs` in HDT) and not in a match, so those are expected.
  - Result (run 2, `logs/log-spike.log`, one Standard ranked win): all calls work during a real match. Timeline: scene `TOURNAMENT` to `GAMEPLAY` at 16:03:51, `GameType=7 (GT_RANKED)` and `Format=2 (FT_STANDARD)` at the same moment, `MatchInfo` filled **4 s later** (16:03:55). `MedalInfo` changed at 16:12:40 (Standard 41/3 stars to 42/2, wins 40 to 41, games 85 to 86) while the scene was still `GAMEPLAY`. After the player left at 16:12:54, `GameType` went back to 0, `Format` to 0 and `MatchInfo` to null.
  - Consequences: (1) cache `GameType`, `Format` and `MatchInfo` during the game, they are gone after leaving; (2) after game start, retry `MatchInfo` for a few seconds until it is non-null; (3) post-match rank is readable before the player leaves the game screen.
  - HDT was open in run 2 (see A2).
- [x] **A2 (B)** Run it again with HDT open. Any conflict (IPC, crashes, wrong data)?
  - Result: HDT was open during run 2 (one ranked game). The spike had no errors and read correct data. (HDT-side behaviour was not reported; no problem was mentioned.)
- [x] **A3 (B)** Does the spike need admin rights? Does HearthMirror fail if Hearthstone is elevated? Decide the app manifest (`asInvoker` vs `requireAdministrator`).
  - Result (partial): the spike ran with `Elevated: False` and read everything, so a normal user app works when Hearthstone is not elevated. Use `asInvoker`. Untested: Hearthstone started as administrator.
- [x] **A4 (B)** Which of the four DLLs does HearthMirror really need (`untapped-scry-dotnet`, `System.Reflection.DispatchProxy`, `Newtonsoft.Json`)? Anything else at runtime (the `lib` folder of HDT also has the VC runtime DLLs)?
  - Decision / result: use the actual lib folder ddls.
- [x] **A5 (B)** HearthMirror target framework: net472 only, or can the app be modern .NET? (Design assumes net472.)
  - Result: works from a net472 x64 console app. Modern .NET is untested. Keep net472 (as the design says) unless there is a reason to try.
- [x] **A6 (B)** HearthMirror licence and redistribution in our installer. Source of the DLL: HDT install, HearthSim repo, or NuGet? Which exact version do we pin?
  - Decision / result: Use the HDT dlls now. Later own solution will be developed with Inityspy (https://github.com/hackf5/unityspy)
- [x] **A7 (R)** How does a failed read look after a game patch (exception, null, hang)? Does `Reflection.Client` recover by itself?
  - Result (run 5, Hearthstone killed in Task Manager during a casual game at 16:55:51): no exception reached the caller and nothing hung. HearthMirror logged `Error while invoking <call>: Win32Exception: Access is denied` for every call (about once a second, through the `LogMessage` event), and the calls returned **null or 0**. `GetGameType()` returned `0` first, which looks the same as "not in a game". After about 5 s the errors changed to `ScryInitializationException` (HearthMirror trying to re-initialise), and the process list showed Hearthstone gone at 16:55:57 (about 6 s after the kill). Consequence: a read failure (null `MedalInfo`/`AccountId`, `Access is denied`) must never be treated as "game ended" or "not in a game"; check that `Hearthstone.exe` is still running before deciding. Still unknown: how a game-patch offset break looks (probably the same nulls). After a relaunch HearthMirror recovers by itself (see B6).
- [x] **A8 (S)** Shape of `MedalInfo` for each format (Standard, Wild, Classic, Twist) and for Legend. Which one is "current" for the game just played?
  - Result (shape): `MedalInfo` has one entry per format (`Standard`, `Wild`, `Classic`, `Twist`), all returned at once. Each has `LeagueId`, `StarLevel`, `Stars`, `LegendRank`, `Streak`, `SeasonGames`, `SeasonWins`, `SeasonId`, `BestStarLevel` and rating fields. Legend looks like `StarLevel=51` with `LegendRank>0` (Wild: 51 / 724). There is **no `Rank` field**.
  - Consequence: the tracker must pick the entry matching the format of the game just played (from `GetFormat()` / `MatchInfo`), and snapshot only that one.
  - Verified in run 2: only the played format may be compared. In the same game `Wild.LegendRank` drifted from 724 to 726 without a Wild game being played, so "any change in `MedalInfo`" is not a valid signal. `Standard.SeasonGames` (+1) and `SeasonWins` (+1) changed exactly once, at game end, so they are a better "rank is updated" signal (see C8).
- [ ] **A9 (S)** Record the spike output in the repo (a short `design/spike-results.md`).
  - Decision / result:

## B. Game detection (milestone 2). `Power.log` is used only for the concede flag

- [x] **B1 (B)** (closed 2026-11-23: app looks in configured folder, process, registry InstallLocation, default folders; tray warning + Settings folder picker when not found; installer detects it and asks only when it cannot) How do we find the Hearthstone install folder and the newest `Logs\Hearthstone_<timestamp>\Power.log`? (Process path, registry, or user setting?) HDT uses `HearthstoneDirectory` from its config.
  - Decision / result: implemented: `HearthstonePaths.FindLogsDir` tries settings `HearthstoneDirectory`, the Hearthstone.exe process path, then C:Program Files (x86)Hearthstone and C:Program FilesHearthstone; newest `Hearthstone_*Power.log` wins. Asking the user to pick a folder comes with the settings UI.
  - **Decision (user):** the installer checks the standard location (e.g. `C:\Program Files (x86)\Hearthstone`) and picks the newest `Logs\Hearthstone_<timestamp>\Power.log` that actually exists. If it cannot decide, it asks the user to point at the Hearthstone install folder and finds the logs from there. Also let the running app re-browse for the folder in Settings.
  - Result (partial): on this machine the logs are in `C:\Program Files (x86)\Hearthstone\Logs\Hearthstone_<timestamp>\Power.log` (one folder per Hearthstone launch, one `Power.log` for all games in that session). Finding the install folder from the `Hearthstone.exe` process path is still to be implemented and tested.
- [x] **B2 (B)** Collect real `Power.log` fixtures: a win, a loss, a concede, a draw, a disconnect/reconnect, a 0-turn game, a friendly game, a spectated game.
  - Decision / result: not needed in full: the app no longer parses game start/end from the log. Real concede lines from 2026-11-23 are used as test fixtures (ConcedeTests). Live: normal loss -> wasConceded=false, concede -> true.
  - Decision / result:
- [x] **B3 (B)** Parsing rules, taken from HDT (`HDT/.../Hearthstone Deck Tracker/LogReader/`): game start (`CREATE_GAME`), game end (`STATE=COMPLETE`), which player is the local player, `PLAYSTATE` values, turn count, concede detection.
  - Decision / result: only the concede line is parsed (`GameState.DebugPrintPower() - TAG_CHANGE Entity=<tag> tag=PLAYSTATE value=CONCEDED`, GameState line only). Everything else (start, end, result, rank) comes from memory, so no CREATE_GAME/COMPLETE parsing is needed.
  - Decision / result:
- [x] **B4 (B)** `log.config`: reuse HDT's `LogConfigUpdater` logic? Handle a read-only file, and what exactly goes in the `[Power]` section.
  - Decision / result: implemented in `LogConfigUpdater`: missing file created with `[Power]` (LogLevel=1, FilePrinting=True, ConsolePrinting=False, ScreenPrinting=False, Verbose=True); existing section left alone; section appended (never overwritten); read-only flag cleared and restored. Unit-tested.
  - Decision / result:
- [x] **B5 (R)** App started mid-game: ignore it (design) or try to recover?
  - Decision / result: ignore the *game*, but recover what memory allows: tested casual 2026-11-23, an app restarted mid-game recovers type, format, opponent and before-rank within ~2 s (start time = first seen). Used live: game 7d3db7d8 was picked up this way.
  - Decision / result:
- [x] **B6 (R)** Hearthstone crashes or restarts while a game is in progress. Drop the game or upload with partial data?
  - Result (partial, crash tested with a casual game): the app survived and the failed reads were all null/0, so no upload happens (no `SeasonGames` increase seen). Proposed decision: **drop the game and log a warning**; do not upload partial data. Open: for a ranked game that was counted by the server after the crash, optionally compare the stored last `SeasonGames` on the next start and log the gap (reconcile later, not v1).
  - Result (run 5, relaunch about 42 s after the kill, spike left running): HearthMirror re-attached by itself, the spike needed no restart. For about 10 s after the process appeared the calls failed with `RuntimeBinderException` (null values while the game was loading), then `AccountId`, `MedalInfo` and `BattleTag` worked again at 16:56:49. The scene went `LOGIN` to `GAMEPLAY` (`PrevMode=2`, not `TOURNAMENT`) at 16:56:55 with `GameType 8`, and `MatchInfo` was filled 2 s later. The new session has its own log folder (`Hearthstone_2026_11_21_16_56_39`) with a new `CREATE_GAME` at 16:56:57. The opponent was a different player (pizzahunt vs ПустойТанк before the kill) and the game turn counter started at 1, so the data looks like a **new game**, not a reconnect into the old one; please confirm what you saw. Either way the tracker handles it as a fresh game: the pre-match rank is still valid because rank only changes at game end, and the killed game was never uploaded, so there is no duplicate.
- [ ] **B13 (B)** Network loss during a game (run 6, casual, network cut at about 16:58:25 while in game at turn 5): see result below. Decide how long a pending game is kept and whether it is stored on disk (B7).
  - Result: Hearthstone kept running and reads kept working. First symptom `BattleTag: null` and scene `Mode=9` (**FATAL_ERROR**, the Reconnect dialog; the spike's mode name map said DRAFT, fixed) at 16:58:25. `GameType`/`Format`/`MatchInfo` only cleared about 4 s later (16:58:29). `MedalInfo` did not change, `Power.log` had no `PLAYSTATE` line and no game end (last line 16:57:51). So there was **no rank update and no end signal** during the outage; nothing was uploadable. Not observed (spike stopped at 16:58:45): whether Reconnect returns to the same game, and whether the server later counts a forfeit. **Decided not to test further** (it would need a ranked game and could cost a star): treat the forfeit case as untested and rely on the bounded watch below.
  - Proposed decision: scene `FATAL_ERROR` while a game is pending does **not** drop the game. Keep it pending while the app runs, watch `SeasonGames` of the played format for a bounded time (proposed 10 min) after the scene leaves `GAMEPLAY`, and drop with a warning after that. A later `SeasonGames` +1 uploads with the data cached at game start (start time, names, pre-rank) and end time = moment seen.
- [x] **B7 (R)** App exits while a game or an upload is in flight. Persist the in-flight game state?
  - Decision / result: **do not persist the in-flight game.** Restart test (casual, 09:34 on 2026-11-23): the app restarted mid-game recovered GameType, Format, MatchInfo (incl. opponent BattleTag) and the before-rank MedalInfo from memory within ~2 s. Start time = first time the app saw the game. If the app starts after the rank already updated, the before-rank is lost: drop the game with a warning. A game that starts and ends while the app is closed is lost. Pending uploads (the retry queue) are persisted separately (see D8/E).
  - B13 stays open: the pending watch after a network cut was never tested on a ranked game (SeasonGames after reconnect unobserved); keep as a known risk.
- [x] **B9 (B)** Can the game end be detected **without** `Power.log`? (Answer: yes, except the concede flag.) Run 2 suggests yes for the basics: `GAMEPLAY` scene entered, and `SeasonGames`/`SeasonWins` increasing at the end gives the result (win = `SeasonWins` also +1). Not available from memory: turn count, concede flag, exact end time. Decide: keep `Power.log` (design) or make it optional/later. This would remove `log.config`, log parsing and the restart-Hearthstone step from v1.
  - Decision: **partly kept.** Start, end, result and rank come from memory (scene `GAMEPLAY`, `SeasonGames` +1, `SeasonWins`), no log needed. `Power.log` is brought back **only** for the concede flag (`wasConceded`), because memory cannot provide it (run 3: concede and loss look identical) and the backend needs the field. Design doc updated.
- [ ] **B8 (S)** Log file truncation/rotation on game launch with `FileShare.ReadWrite | FileShare.Delete`: confirm with a real restart.
  - Decision / result: not tested with a real Hearthstone restart. Watcher handles truncation (position reset) and a new session folder (read from start); unit-tested only. Keep open until a real restart is observed.
  - Decision / result:
- [x] **B12 (B)** Minimal log reader scope: only the concede flag (and optionally `TIED` for draws). Which line exactly (`TAG_CHANGE Entity=<name> tag=PLAYSTATE value=CONCEDED`)? Does it appear before our `SeasonGames` +1 (so the upload can wait a short time for it)? Does it mean *you* conceded or *either* player (HDT flags both)? Check with HDT's `TagChangeActions.cs:1000` and a real `Power.log` from the concede games played in run 3.
  - Result (real `Power.log` of run 3, session `Hearthstone_2026_11_21_15_54_09`, HDT had already created the `[Power]` section): the line is `GameState.DebugPrintPower() - TAG_CHANGE Entity=Deesnow#2794 tag=PLAYSTATE value=CONCEDED` (the entity is the BattleTag of whoever conceded, so it can be compared with the local tag from `MatchInfo`). It is repeated once more under `PowerTaskList`; use the `GameState` line only. Timing: game 1 line at 16:22:02.56 and game 2 at 16:27:15.27, the same moment as the rank update (spike saw it at 16:22:03.3 and 16:27:15.7), so the reader needs no long wait, about 2 s is enough. For normal wins/losses the memory update comes about 1 s after the `PowerTaskList` end lines (win 16:12:39.9 -> 16:12:40.7), and 38 s after the `GameState` end line (16:12:02) because the end animation plays first; the concede has no animation. Still open: the draw line (`TIED`) was not observed.
- [x] **B10 (B)** Observe the end of a **loss**, a **concede**, a **disconnect/early leave** and a **Wild** game with the spike. Does `SeasonGames` always increase, and how soon after leaving? This sets the grace window (proposed 15 s) before a game is dropped.
  - Result (run 3, three Standard ranked games with HDT open): `SeasonGames` increased by exactly 1 in all three, and the update was visible **before** the player left the game screen:

    | Game | Start | Rank update seen | Player left | Standard before → after |
    |---|---|---|---|---|
    | 1. early concede | 16:21:41 | 16:22:03 (22 s after start) | 16:22:17 (14 s later) | 42/2★ → 42/1★, Games 86→87, Wins 41, Streak 3→0 |
    | 2. mid-game concede | 16:22:31 | 16:27:15 (4 min 44 s) | 16:27:26 (11 s later) | 42/1★ → 42/0★, Games 88, Wins 41 |
    | 3. normal loss | 16:27:46 | 16:39:33 (11 min 47 s) | 16:39:46 (13 s later) | 42/0★ → 41/2★ (level lost), Games 89, Wins 41 |

  - Conclusions: a concede (at any time) and a normal loss look identical in the data: `SeasonGames` +1, `SeasonWins` unchanged. So `Power.log` is **not** needed to detect them. Only the `wasConceded` flag cannot be known (C9). The 15 s grace window is only a safety net, since updates arrived 11–14 s before leaving.
  - Run 4 (Wild ranked, Legend, normal loss; 16:48:36 start, rank update 16:53:11, left 16:53:20, 9 s later): `GameType=7`, `Format=1 (FT_WILD)`. Wild entry: `LegendRank` 728 → 779 (worse, higher number), `Games` 166 → 167, `Wins` 91 unchanged, `Streak` 3 → 0, `StarLevel` stays 51, `Stars` 0. The Standard entry did not change. So the same `SeasonGames` +1 rule works for Legend and Wild. Note: Legend rank also drifts between games without playing (724 → 726 → 728 earlier), so the pre-match value is the one read at game start, not an earlier one.
  - Still untested: disconnect or Hearthstone crash during a game.
- [x] **B11 (R)** Does a concede or a game left at once count in `SeasonGames` and star loss? If not, those games are never uploaded; is that acceptable?
  - Result: yes. An early concede (about 20 s after start) and a mid-game concede both counted: `SeasonGames` +1, a star lost, streak reset. Leaving the game screen immediately after conceding was not tested; the update appeared before the player left in all three games.

## C. Payload and mapping

- [x] **C1 (B)** Mapping of HearthMirror game type and format to the strings the backend gets (`gameMode`, `format`). Which game types count as Ranked?
  - Result (partial): confirmed `GameType 7 = GT_RANKED` and `Format 2 = FT_STANDARD` in a real game. Same mapping as HDT's `HearthDbConverter.GetGameMode/GetFormat` (`GT_RANKED` to `Ranked`; `FT_WILD/STANDARD/CLASSIC/TWIST` to `Wild/Standard/Classic/Twist`). `Format 1 = FT_WILD` also confirmed (run 4, Wild ranked game at Legend). Casual: `GameType 8 = GT_CASUAL` seen in a real game (run 5, `Format` read as Wild there), so it is not uploaded. Classic and Twist values come from HDT's code, not yet seen in a game.
- [x] **C2 (B)** `result` strings (`Win`/`Loss`/`Draw`) and how a concede is reported (`result=Loss` + `wasConceded=true`, as in the plugin?).
  - Result: `result` is a free `str` in the backend (no enum), and nothing reads it; send `Win` or `Loss` (concede counts as `Loss`, with `wasConceded=true`). `Draw` is only possible if the log shows `TIED` (B12). `gameMode` is a free string too, but the bot needs exactly `Ranked` and `format` `Standard`/`Wild` (any case).
- [x] **C3 (B)** Source of each rank field: `LeagueId`, `Rank`, `StarLevel`, `Stars`, `LegendRank` and the three `...After` fields, from `MedalInfo`. Which fields does the backend really read (open question from the design)?
  - Result (from `bot/services/rank_tracker_data.py` and `tracker_ingest.py`, the only readers): the bot reads `start_time`, `end_time`, `star_level`, `stars`, `legend_rank`, `star_level_after`, `stars_after`, `legend_rank_after`, `player_battletag` (case-insensitive, joined to `user_battletags` with `region`), `region`, `game_mode = 'Ranked'` (exact case), `format` (`standard`/`wild`, case-insensitive) and `league_id = 5`. **Not read by anything:** `result`, `was_conceded`, `opponent_battletag`, `rank`. They are still stored, `opponent_battletag` is `NOT NULL` (send an empty string if unknown), and `rank` is required (send 0).
- [x] **C4 (B)** Timestamps: local time with offset (as today) or UTC? Source of start and end time.
  - Result: the backend parses `startTime`/`endTime` as ISO 8601 `datetime` into `TIMESTAMPTZ`. Always send an explicit offset (the plugin sent local time with offset, UTC `Z` is equally fine); a value without offset would be ambiguous. Charts use `start_time` as the pre-match point and `end_time` as the post-match point, so `endTime` must be later than `startTime`. Source: start = scene `GAMEPLAY` entered, end = the moment the `SeasonGames` increase was seen.
- [x] **C5 (B)** Region: confirm `GetAccountId` to `US`/`EU`/`AP`, and what to send when unknown.
  - Result: region is required for the bot to use a game: every chart query filters `region = <EU|US|AP>`, and rows with `NULL` region are excluded. The model accepts `EU`/`US`/`AP`/`UNKNOWN`/`CHINA` (uppercased on ingest; anything else is stored and logged), so a missing value can be sent as `UNKNOWN`, but such games will not show up in the charts. In practice the region is readable from the first spike sample, before any game, so `UNKNOWN` should be rare.
- [x] **C6 (R)** `playerBattleTag` and `opponentBattleTag`: HearthMirror first, log fallback. Is the opponent tag always available?
  - Result (partial): both `playerBattleTag` and `opponentBattleTag` are required non-null strings in the backend (send an empty string if the opponent is unknown). `MatchInfo` gives both BattleTags (`LocalPlayer.BattleTag`, `OpposingPlayer.BattleTag`, 4 s after start). It also holds the opponent's league, star level and Legend rank per format, so `opponentRank` could be added later. Still unknown: whether the opponent tag is always present (disconnects, AI games).
- [ ] **C7 (S)** Capture one real upload from the old plugin **after** the "Region fix" commit (the sample in `received_games.log` has no `region`) and use it as the golden payload for tests.
  - Decision / result:
- [x] **C8 (S)** Post-match rank is "ready" when? The plugin used `LegendRankAfter > 0 || StarLevelAfter > 0 || StarsAfter > 0`. The design says "differs from the pre-match snapshot". Pick one.
  - Result: neither. Run 2 shows a safer rule: for the played format, `SeasonGames` is greater than at game start. It works for wins and losses, also when stars do not change (for example a loss at a protected level), and ignores the Wild Legend drift. Proposed: "ready" = `SeasonGames` increased for the played format; on timeout upload with what we have.
- [x] **C9 (B)** `wasConceded` (and draws) are not available from memory. Decided: read the concede flag from `Power.log`; the app always sends a boolean.
  - Result (backend source `HS-Snake/decktrackerAPI`): the backend requires a real boolean (`was_conceded: bool`, column `NOT NULL`; `null` gives a 400). **Decision: the API stays as is.** The app always sends `true` or `false`. `false` is also sent when the log is unavailable (no `[Power]` section yet), and a warning is logged so such games can be recognised. Nothing reads `was_conceded` in the bot today.
  - **Decided:** `wasConceded` is `true` whenever **either** player conceded (same as the plugin and HDT). Source: `PLAYSTATE value=CONCEDED` for any player entity in `Power.log`; no need to compare with the local BattleTag. The API is unchanged and requires a boolean.

## D. Backend and uploader

- [x] **D1 (B)** Real production `ApiUrl`, and a test/staging URL and key for development.
  - Decision / result: local test stand-in in `test-api/` (git-ignored): `docker compose up --build -d`, URL `http://localhost:8000/API`, key `hsjeeves-local-test-key`, no DB. Same contract as production (401/400/duplicate 200, `GET /API/check`); every request is appended to `test-api/logs/payloads.jsonl`.
  - Result: production `ApiUrl` is `https://dee-service.cc/API`. No staging stack; the project owner runs the API locally in Docker for tests. Note: `docker-compose.dev.yml` does not publish a host port for `rank-api` (only the internal network), so a local URL such as `http://localhost:8000/API` needs `ports: ["8000:8000"]` on that service. The local database also needs a token row (see `RankTrackerAPI-design.md`, manual provisioning).
- [x] **D2 (B)** Is `gameId` idempotency really enforced by the real backend (unique key, repeat POST is a no-op, which status code)?
  - Result: **yes.** `game_id` is the primary key and the insert is `ON CONFLICT (game_id) DO NOTHING`; a repeat POST of the same `gameId` returns `200 {"status":"ok","gameId":...}`, never 409. So "already uploaded" looks like success to the client (`main.py`, `RankTrackerAPI-design.md`).
- [x] **D3 (B)** Status handling for the retry queue: 5xx and network errors retry; 401/403 stop and warn; other 4xx drop; 409 counts as success?
  - Result: statuses from the source: `200` stored or duplicate; `401` missing, invalid or revoked token (checked **before** the body is read); `400` malformed JSON or a body that fails validation (the 422 default is overridden); a database error would be an unhandled `500`; the Cloudflare Tunnel in front can add `5xx` (52x) when the origin is down. Client policy: 200 = done; 401 = stop, mark key invalid, warn in the tray (do not retry); 400 = drop and keep a copy in a rejected file (our bug, retrying cannot help); network errors, 408, 429 and 5xx = retry with backoff (20 attempts or 7 days, as designed).
- [x] **D4 (B)** "Test connection" button: no endpoint exists (`/health` takes no key). Add a backend endpoint, or change the feature?
  - Decision: **done on the backend.** `GET /API/check` added in `decktrackerAPI/main.py` (uses the same bearer auth as `POST /API`; 200 `{"status":"ok"}` for a valid key, 401 for a missing, invalid or revoked key, stores nothing). The Settings "Test connection" button calls it. Needs deploying to production before the button works there.
- [x] **D5 (B)** Parallel running with the old plugin creates duplicates (different `gameId`). Use a dry-run mode, a test endpoint, or a test key?
  - Decision / result: dry-run mode (settings `DryRun`, payload written to `%APPDATA%HSJeevesdryrun\`) plus the local test API (`test-api/`). HDT uploads to the real API, HSJeeves to the local one, so no duplicates in production.
  - Result (partial): the backend dedupes only on `gameId`, so the plugin and HSJeeves would insert two rows for one game (the plugin uses HDT's GUID, the app creates its own). Proposed: a **dry-run mode** in HSJeeves (build the payload, write it to the log, do not POST) to compare with the plugin's payload in HDT's log, plus a test key against a dev stack (`docker-compose.dev.yml`) for end-to-end tests. Needs your OK; D1 test URL still open.
- [x] **D6 (R)** What does `/hdttoken` do when run again: revokes the old key? (Marked TODO in `docs/how-to.md`.)
  - Result: **yes, revokes the old key.** `/hdttoken` on an existing user asks for Confirm/Cancel and then marks the old row `revoked_at = now()` in the same transaction as inserting the new one (`RankTrackerAPI-design.md`, `bot/commands/hdttoken_commands.py`). A revoked key gives 401 at once, so the app must show a clear "key revoked, run /hdttoken" state. Update `docs/how-to.md`: remove the TODO.
- [x] **D7 (R)** Migration of existing plugin users: reuse their key, tell them to remove the plugin DLL.
  - Result: existing plugin users keep their key. HSJeeves uses the same `/API` and the same token table, so no new auth is needed: paste the same key (or run `/hdttoken` for a new one). Old plugin and new app would use the same `token_id`, so the dedupe issue is only the different `gameId` (see D5).
- [ ] **D8 (S)** Retry queue limits (20 attempts / 7 days), file format, and what happens if the file is corrupt.
  - Decision / result:

## E. App, settings and security

- [x] **E1 (B)** Settings file schema, including the DPAPI-protected key field (for example `apiKeyProtected`) next to the plain key the installer writes.
  - Decision / result: implemented in `Settings`: `ApiUrl` (default https://dee-service.cc/API), `ApiKeyProtected`, optional plain `ApiKey` (installer, migrated then removed), `DryRun`, `StartWithWindows`, `HearthstoneDirectory`. Corrupt/undecryptable key gives an empty key and a warning, not a crash.
  - Decision / result:
- [x] **E2 (R)** Plain key sits on disk until first launch. Acceptable, or have the installer call the app to protect it?
  - Decision / result: acceptable: the installer may write a plain `ApiKey`; on first launch the app moves it into `ApiKeyProtected` (DPAPI, current user) and removes the plain value. Live-verified on the dev settings file.
  - Decision / result:
- [x] **E3 (R)** Single-instance mutex name, and what the second launch does (activate the first, or exit).
  - Decision / result: mutex `LocalHSJeeves-single-instance` (per Windows user). A second launch exits silently; the tray icon of the first instance is already there.
  - Decision / result:
- [x] **E4 (R)** Account or region switch during one Hearthstone session.
  - Decision / result: handled: ranks and region are per account. The tracker remembers the account (Hi:Lo) and region at game start and drops a pending game if the account changes (switch US/EU by restarting Hearthstone). Unit-tested; live-verified that the same BattleTag shows a different account id per region.
  - Decision / result:
- [ ] **E5 (S)** Logging library (Serilog?), log format, redaction rules.
  - Decision / result:
- [ ] **E6 (S)** Tray: icon states (OK, warning, no game, uploading), menu items (Settings, Open logs, Version, Exit), notifications, icon assets (.ico).
  - Decision / result: partly done: menu = status line, last game, upload state, Settings..., Open logs folder, version, Exit; double-click opens Settings; warning icon + balloon when the key is missing or rejected; tooltip shows status. Still open: dedicated warning/uploading icon artwork (currently the stock Windows warning icon).
  - Decision / result:
- [x] **E7 (S)** (closed 2026-11-23: README section "Privacy and what HSJeeves reads") Privacy note: opponent BattleTags are uploaded; memory reading disclaimer in the README.
  - Decision / result:

## F. Repository, build and tests

- [x] **F1 (B)** One repository layout. The design doc, the new repo (`src/HSJeeves.Spike`) and the old repo (`src/app/...`) all differ. Pick one and update the design doc.
  - Decision: **done.** Root `HSJeeves.sln`; `src/HSJeeves` (app), `src/HSJeeves.Spike`, `src/HSJeeves.Tests`, `src/lib` (git-ignored DLLs); later `installer/`. Design doc layout matches.
- [x] **F2 (B)** How the HearthMirror DLLs get into a fresh clone (`src/lib` is git-ignored): a documented copy step, a build script, or commit them after the licence check?
  - Decision: **documented copy step** (`docs/build.md`) until the HearthMirror licence is checked (A6).
- [x] **F3 (B)** Solution skeleton: `HSJeeves.sln`, app project (WinForms, net472, x64), test project. Test framework (xUnit or NUnit)?
  - Decision: **done.** WinForms tray app, net472, x64; xUnit 2.9 tests; builds with 0 warnings and 5 tests pass (first piece: `Region.FromAccountHi`, tested with the real EU account id).
- [x] **F4 (R)** `.gitignore` for the new repo (bin/obj, installer output, `dist/`). Remove old-repo rules.
  - Decision: `.gitignore` already covers bin/obj, dist, `src/lib`, `logs/`; no old-repo rules left.
- [ ] **F5 (R)** Fake `RankReader` interface so `GameTracker` is testable without the game.
  - Decision / result:
- [ ] **F6 (S)** CI: GitHub Actions on a Windows runner (build, tests, installer compile)? Needed, or manual for v1?
  - Decision / result:
- [ ] **F7 (S)** Update `README.md` and `docs/how-to.md` after the first release (replace the plugin instructions).
  - Decision / result:

## G. Installer and release

- [x] **G1 (B)** (closed 2026-11-23: Inno Setup installed, `installer\build.ps1` compiles `dist\HSJeeves-Setup-0.1.0.exe`) Inno Setup installed on the build machine. Fixed `AppId` GUID.
  - Decision / result: AppId fixed in `installer/HSJeeves.iss` ({114BFFB8-5761-40D1-80AC-0E56359951D8}, never change it). Script written but NOT compiled yet: Inno Setup 6.3+ is not installed on this machine (`winget install JRSoftware.InnoSetup`); `installer/build.ps1` builds, tests and compiles it.
- [x] **G2 (R)** .NET Framework 4.7.2 prerequisite check in the installer.
  - Decision / result: done in the script: checks .NET Framework Release >= 461808 (4.7.2) in InitializeSetup and stops with a download link if missing. Untested until the installer is compiled and run.
- [x] **G3 (R)** Version source of truth (assembly version) and release naming (`HSJeeves-Setup-x.y.z.exe`).
  - Decision / result: done: the `<Version>` in `src/HSJeeves/HSJeeves.csproj` is the single source; `installer/build.ps1` reads it and passes it to Inno as AppVersion; output is `dist/HSJeeves-Setup-<version>.exe` (dist/ is git-ignored).
- [x] **G4 (R)** (decided 2026-11-23: ship UNSIGNED. build.ps1 writes a .sha256 next to the installer; docs/how-to.md explains the SmartScreen "Run anyway" step and how to verify the checksum. Revisit: Azure Trusted Signing, and a winget manifest as a free second route.) Code signing: sign, or document the SmartScreen "Run anyway" step? Decide before publishing.
  - Decision / result:
- [ ] **G5 (S)** (partly tested 2026-11-23 on the dev machine, silent mode: fresh install writes valid settings.json and the Run key, upgrade keeps settings untouched, uninstall removes files and the Run key and keeps settings and logs. NOT tested: wizard pages by eye (key page, folder page), key entry, in-place upgrade over a running app, clean Windows user or VM, SmartScreen) Test on a clean Windows user or VM: install, paste key, play, upload, upgrade in place, uninstall.
  - Decision / result:
- [ ] **G6 (S)** (in progress 2026-11-23: installers are published to the Jeeves website, HS-Snake branch `hsjeeves-downloads`: /downloads/ + versions.json, newest 3 kept, via installerpublish.ps1. Site: https://jeeves.dee-service.cc/ (live, first installer published 2026-11-23). Open: merge hsjeeves-downloads to main.) Where the release is published (GitHub release in this repo?) and who can download it.
  - Decision / result:

## H. Schedule

- [x] **H1 (B)** (closed 2026-11-23: confirmed OK) Cut line for 30 Nov: milestones 1–4 (working uploads, run manually) are the minimum. Confirm that the tray UI and installer can follow after the deadline.
  - Decision / result:
- [x] **H2 (R)** (decided 2026-11-23: HSJeeves starts in dry-run mode so it cannot double-upload next to the HDT plugin; a tray notice at start and after every dry-run game says nothing was uploaded and asks to switch the plugin off (HDT > Options > Plugins) and untick Dry run. Archiving the plugin: after 30 Nov.) Plan for the last day of plugin support: when to switch players from the plugin to HSJeeves, and when to archive the plugin.
  - Decision / result:
