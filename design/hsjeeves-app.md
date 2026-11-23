# HSJeeves app — standalone rank tracker (design)

## Goal

HDT stops supporting plugins after 30 Nov 2026, so the current
`RankTrackerPlugin` (in the old HS-Rank-Tracker repo) will stop working. **HSJeeves** replaces it with a
standalone Windows app that:

1. detects finished ranked Hearthstone matches,
2. captures the player's rank before and after the match,
3. uploads each match to the existing backend (same payload, same Bearer key
   from the Jeeves `/hdttoken` command),
4. ships as a **Windows installer** so a player can install it and be set up
   in under a minute.

It must run **alongside HDT** (both are read-only observers) but must not
depend on it.

## Non-goals

- No overlay, deck tracking or replay features.
- No modes other than Ranked in v1 (keep the `UploadedModes` filter idea so
  adding Battlegrounds later is a small change).
- No changes to the backend contract in v1 (`schemaVersion` stays 1).

## Architecture

One process, a tray app with no main window, made of five components.
Game start, end, result and rank come from HearthMirror (process memory), as
proven by the spike (see [docs/open-questions.md](../docs/open-questions.md),
items A1, A8, B9, B10). **`Power.log` is used for one thing only: the
`wasConceded` flag**, which memory cannot provide.

```text
 Hearthstone.exe ── process memory ──► RankReader (HearthMirror) ──► GameTracker ──► Uploader ──► backend
        │                                                              ▲   │
        └── Power.log ──► ConcedeWatcher (concede events only) ────────┘   └──► PendingQueue (jsonl)
 ProcessMonitor ─ (start/stop reading when Hearthstone runs)
```

| Component | Responsibility |
|---|---|
| **ProcessMonitor** | Polls (~2 s) for `Hearthstone.exe`. Starts the rank reader when it appears, stops it when it exits. Also resets cached region. |
| **RankReader** | Thin wrapper over HearthMirror, sampled about once per second while Hearthstone runs: scene state, game type, format, match info (names, BattleTags), `GetAccountId()`, `GetMedalInfo()`. Retries `MatchInfo` until non-null after a game starts. |
| **ConcedeWatcher** | Tails the current session's `Power.log` and reports only `TAG_CHANGE Entity=<name> tag=PLAYSTATE value=CONCEDED` events (same signal as HDT's `TagChangeActions.PlaystateChange`). Opens the file with `FileShare.ReadWrite \| FileShare.Delete`, handles truncation on game launch. Optional: if the log is missing, the game is still tracked and `wasConceded` is sent as `false` (the backend requires a boolean). |
| **GameTracker** | State machine that combines the samples into one `MatchRecord` per game. Detects start (scene `GAMEPLAY`) and end (`SeasonGames` of the played format increased). |
| **Uploader** | Builds the JSON payload, POSTs it with the Bearer key, and falls back to the retry queue on failure. Ported nearly unchanged from `RankTrackerPlugin.SendToServer`. |

Plus: **TrayUi** (status icon, menu, settings window), **Settings**, **Logging**.

### Replacing what HDT provided

| Plugin used (from HDT) | HSJeeves source |
|---|---|
| `GameEvents.OnGameEnd` | `SeasonGames` of the played format's `MedalInfo` entry increases while (or shortly after) the scene is `GAMEPLAY` |
| `Result` | `SeasonWins` also increased → `Win`, otherwise `Loss`. A draw cannot be told apart from a loss in v1. |
| `WasConceded` | `ConcedeWatcher` saw `PLAYSTATE value=CONCEDED` during this game. Same meaning as the plugin: HDT also flags it when the *opponent* concedes. The backend requires a real boolean (not null), so `false` is sent when the log is unavailable, and a warning is logged. |
| `Turns` | Not needed. The 0-turn guard is replaced by "no `SeasonGames` increase → no upload". |
| `GameMode`, `Format` | `GetGameType()` (7 = `GT_RANKED`) and `GetFormat()` (2 = `FT_STANDARD`), cached while in the game because they reset when the player leaves. Mapped like HDT's `HearthDbConverter` to `Ranked`, `Standard`, … |
| Pre-match rank | `MedalInfo` entry **of the played format**, snapshotted when `GAMEPLAY` starts |
| Post-match rank (`...After`) | The same entry once `SeasonGames` increased (readable before the player leaves the game screen) |
| `Region` | `GetAccountId()` → `(accountHi >> 32) & 0xFF` → `US`/`EU`/`AP` (same `RegionLabel` remap); available before any game |
| `GameId` | `Guid.NewGuid()` created at game start (idempotency key) |
| `StartTime` / `EndTime` | Scene entering `GAMEPLAY` / moment the `SeasonGames` increase is seen |
| Player/opponent BattleTag | `MatchInfo.LocalPlayer.BattleTag` / `OpposingPlayer.BattleTag` (filled about 4 s after game start) |

> **Verified in the spike (milestone 1, run 2):** one Standard ranked win,
> see [docs/open-questions.md](../docs/open-questions.md). Concede, loss and
> disconnect behaviour are still to be observed.

### Game lifecycle (GameTracker state machine)

```text
Idle ──scene GAMEPLAY + ranked──► InGame ──SeasonGames +1──► Upload ──► Idle
                                    │
                                    └─ scene left GAMEPLAY, no +1 within the grace window ──► drop (warn) ──► Idle
```

- **Game start** (scene becomes `GAMEPLAY`): if `GetGameType()` is not in
  `UploadedModes`, stay `Idle` for this game. Otherwise create `GameId`, note
  the start time, snapshot the `MedalInfo` entry of the played format (from
  `GetFormat()`), and read region. Retry `MatchInfo` (about 1 s interval, up
  to ~30 s) until it is non-null; cache names, BattleTags, game type and
  format, because they reset to empty when the player leaves.
- **InGame:** sample about once per second. When `SeasonGames` of the played
  format is greater than in the snapshot, the game is over and the rank is
  updated: read the entry as the post-match rank, set `result` from
  `SeasonWins`, set the end time, and upload.
- **Left without rank update:** if the scene leaves `GAMEPLAY` and no
  `SeasonGames` increase is seen, keep the game pending and keep watching the
  played format's `SeasonGames` for a bounded time (proposed 10 min), then
  drop it with a warning, because completion cannot be confirmed (replaces the
  plugin's 0-turn guard). Concedes (early and mid-game) and a normal loss were
  tested: the update always arrived 11–14 s before the player left the game
  screen, so a longer wait only matters for disconnects.
- **Network loss:** the scene becomes `FATAL_ERROR` (the Reconnect dialog),
  `BattleTag` reads `null`, and `GameType`/`Format`/`MatchInfo` clear about 4 s
  later. There is no rank update and no `Power.log` end line during the outage,
  so this is the "left without rank update" case above, never a drop on the
  scene change alone. Whether the server later counts the game as a forfeit is
  not yet observed (item B13).
- **A failed read is not a game end.** When Hearthstone is killed or crashes,
  HearthMirror calls return `null`/`0` and log `Access is denied`; the game
  type even reads `0`, the same as "not in a game". The tracker treats null
  `MedalInfo`/`AccountId` as "read failed", checks that `Hearthstone.exe` is
  still running, and drops the game in progress (no partial upload) when the
  process is gone. Tested by killing Hearthstone during a game.
- Only the played format's entry is compared: other formats' values change on
  their own (Wild Legend rank moved 724 → 726 during a Standard game).
- Polling runs on a background thread.

If the app starts mid-game, it ignores that game (no pre-match snapshot to
compare against) and begins tracking from the next one.

## Payload and backend

Unchanged from the plugin so the backend needs no work:

```json
{
  "schemaVersion": 1, "gameId": "…", "startTime": "…", "endTime": "…",
  "gameMode": "Ranked", "format": "Standard", "result": "Win",
  "wasConceded": false, "region": "EU",
  "playerBattleTag": "…", "opponentBattleTag": "…",
  "rank": { "LeagueId": 0, "Rank": 0, "StarLevel": 0, "Stars": 0, "LegendRank": 0 },
  "rankAfter": { "StarLevelAfter": 0, "StarsAfter": 0, "LegendRankAfter": 0 }
}
```

- `POST {ApiUrl}` with `Authorization: Bearer {ApiKey}`, one shared
  `HttpClient`, TLS 1.2 enabled explicitly (net472).
- **Retry queue (new vs. the plugin):** on failure, append the payload to
  `%LocalAppData%\HSJeeves\pending-uploads.jsonl` with attempt count and
  first-attempt time. Retry on startup and every few minutes; drop after 20
  attempts or 7 days. Safe because `gameId` is idempotent server-side (a
  repeat POST returns 200).
- Without an `ApiKey`, log a warning and skip uploading (as today).
- **Response handling** (from the backend source): `200` = done (stored or
  duplicate); `401` = key missing, invalid or revoked → stop retrying, tray
  warning "run /hdttoken"; `400` = our payload is wrong → drop, keep a copy in
  a rejected file; network errors, `408`, `429`, `5xx` → retry with backoff.
- **Backend contract rules** (the API is not changed for v1): every field in
  the payload is required and non-null except `region`. So: `wasConceded` is
  always `true`/`false` (`false` if the log is unavailable); `opponentBattleTag`
  is `""` if unknown; `rank.Rank` is `0`; `endTime` is after `startTime`, both
  with an explicit UTC offset. The bot only uses games with `gameMode`
  `Ranked`, `format` Standard/Wild, `leagueId` 5 and a known `region`.
- **Test connection:** `GET {ApiUrl}/check` with the bearer key: `200` valid,
  `401` invalid or revoked (new endpoint, added to the backend).

## Settings and first-run setup

Stored per user in `%LocalAppData%\HSJeeves\settings.json`:

```json
{ "apiUrl": "https://dee-service.cc/API", "apiKey": "", "startWithWindows": true }
```

- The installer writes `apiUrl` (default) and `apiKey` (from the wizard,
  below). The app never needs the user to edit JSON by hand.
- A **Settings** window (from the tray menu) lets the user change the API key
  later: a text box, **Save**, and **Test connection** button. It replaces
  the plugin's "Reload settings" button hack.
- Protect the key at rest with Windows DPAPI (`ProtectedData`, CurrentUser
  scope) rather than plain text. Never log it.
- If the key is empty on launch, the tray icon shows a warning state and
  clicking it opens Settings.

### Log configuration (for the concede flag only)

`Power.log` is only written when `log.config` has a `[Power]` section. On
startup the app checks `%LocalAppData%\Blizzard\Hearthstone\log.config`:

- file missing → create it with a `[Power]` section (`LogLevel=1`,
  `FilePrinting=true`, `ConsolePrinting=false`, `Verbose=true`);
- file exists with a `[Power]` section → leave untouched (HDT users already have it);
- file exists without one → **append** the section, never overwrite
  (HDT manages the same file); handle a read-only file as HDT's
  `LogConfigUpdater` does.

Changes only apply on the next Hearthstone launch. Until then games are still
tracked, but `wasConceded` is sent as `false`; the tray status says "Restart
Hearthstone once to enable concede detection". The install folder and the
newest `Logs\Hearthstone_<timestamp>\Power.log` are found from the
`Hearthstone.exe` process path (to be confirmed, item B1).

## Running alongside HDT

- Both only read. Process memory is read-only; `Power.log` is opened with
  `FileShare.ReadWrite | FileShare.Delete`. `log.config` is only ever
  appended to, never overwritten (HDT manages the same file).
- Sample game memory about once per second (cheap reads; the spike did this
  without errors).
- Ship a pinned HearthMirror build; after a game patch it may fail until
  updated, so a failed read must degrade gracefully (log, retry, upload with
  partial data), never crash the app.
- Not verified until the spike runs with HDT open — see Milestones.

## Technology

| Choice | Decision | Why |
|---|---|---|
| Runtime | **.NET Framework 4.7.2+, x64**, WinForms tray app | The plugin already targets net472 and HearthMirror was built for HDT's framework. *Confirm HearthMirror's target in the spike; if it supports modern .NET, prefer .NET 8 self-contained.* |
| JSON | Newtonsoft.Json (bundled, since HDT no longer provides it) | Matches existing code. |
| UI | NotifyIcon + a small settings form | No main window needed. |
| Single instance | Named mutex | Avoid two copies uploading duplicates. |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (toggle in Settings) | Per-user, no admin. |
| Logs | `%LocalAppData%\HSJeeves\logs\hsjeeves-.log`, rolling, 7 days | Replaces HDT's log. |

**Licence check before shipping:** confirm HearthMirror's licence and that
redistributing the DLL in your installer is allowed.

## Repository layout

```text
HSJeeves.sln
src/
  lib/               (HearthMirror.dll + dependencies, not committed: licence unverified)
  HSJeeves.Spike/    (milestone 1 console spike)
  HSJeeves/          (app: tray, components above)
  HSJeeves.Tests/    (GameTracker state machine, payload, retry queue)
installer/
  HSJeeves.iss       (Inno Setup script)
  build.ps1          (publish app -> compile installer)
design/
  hsjeeves-app.md    (this file)
docs/
  how-to.md          (player installation and setup guide)
```

Unit-testable without the game: the `GameTracker` state machine (fake
`RankReader` fed with recorded sample sequences, e.g. from
`logs/log-spike.log`),
payload construction, retry queue.

## Windows installer (final step)

### Choice: Inno Setup

| Option | Verdict |
|---|---|
| **Inno Setup** | **Chosen.** Free, one scriptable `.iss` file, produces a single `HSJeeves-Setup-x.y.z.exe`. Per-user install without admin, custom wizard pages (API key), Start Menu/autostart/uninstall built in. |
| WiX (MSI) | More enterprise-friendly but heavy authoring for a tray app. |
| MSIX | Packaging/signing and store requirements add friction; file/registry access to the game's folders is more awkward. |
| Velopack/Squirrel | Good *later* if you want auto-update; not needed for v1. |

### What the installer does

- **Per-user install, no admin prompt:** `PrivilegesRequired=lowest`,
  default dir `{localappdata}\Programs\HSJeeves`.
- Copies the published app + HearthMirror + dependencies.
- **Wizard page "Your Jeeves API key":** one text field with the hint
  *"Send `/hdttoken` to the Jeeves bot on Discord and paste the key here"*.
  The value is written to `settings.json` (the app encrypts it with DPAPI on
  first launch). Can be skipped; the app then opens Settings on first run.
  Pre-fills the key from an existing settings file when upgrading, so
  updates never ask again.
- Start Menu shortcut; optional tasks (ticked by default): **Start with
  Windows**, **Launch HSJeeves now**.
- Detects and closes a running HSJeeves before upgrading (`CloseApplications`
  or a mutex check).
- **Uninstall:** removes program files and the autostart entry; asks whether
  to also delete `%LocalAppData%\HSJeeves` (settings, queue, logs).
- Version comes from the app's assembly version so installer and app can't
  drift (`AppVersion` passed in by `build.ps1`).

### Build and release

`installer/build.ps1`:

1. `dotnet publish` the app in Release/x64 into a staging folder.
2. Run `ISCC.exe HSJeeves.iss /DAppVersion=<version> /DSourceDir=<staging>`.
3. Output `dist/HSJeeves-Setup-<version>.exe`.

**Code signing:** an unsigned installer triggers Windows SmartScreen
("unknown publisher"). For a private group of players that is acceptable —
document the "More info → Run anyway" step in the README. For wider
distribution, sign with a code-signing certificate (`SignTool` in the Inno
script). Decide this before publishing.

Publish by attaching `HSJeeves-Setup-x.y.z.exe` to a GitHub release and
updating the README's install section (replacing the plugin copy-DLL
instructions).

## Milestones

Given the 30 Nov deadline, order by risk:

1. **Spike (console):** reference HearthMirror, print `GetMedalInfo()`,
   `GetAccountId()`, match info while Hearthstone is running **with HDT
   open**. Settles framework target, API names, licence, coexistence.
   *Done for one Standard win. Still to observe: a loss, a concede, a
   disconnect, a Wild game, and a run with HDT confirmed open.*
2. **RankReader + GameTracker:** detect game start/end/result from memory
   samples, unit-tested against recorded sample sequences.
3. **Rank capture:** pre/post-match snapshot of the played format.
   **ConcedeWatcher:** `log.config` handling plus the concede-only log
   reader, unit-tested against lines from a real `Power.log`.
4. **Uploader + retry queue + settings:** end-to-end to the backend,
   running side by side with the old plugin to compare payloads (use a test
   endpoint or key, see D5).
5. **Tray UI + autostart.**
6. **Installer** (`HSJeeves.iss`, `build.ps1`), test on a clean Windows
   user account/VM: install → paste key → play a ranked game → see it
   upload → upgrade-in-place → uninstall.
7. Update README, publish release, retire the plugin.

## Risks

| Risk | Mitigation |
|---|---|
| HearthMirror offsets break after a game patch | Pin a version, degrade gracefully, keep an update path (rebuild + new installer). |
| HearthMirror licence/redistribution unclear | Check in the spike, before building around it. |
| Rank update never seen (concede, disconnect, slow server) | Grace window after leaving the game screen, then drop with a warning; to be tested with a concede and a disconnect. |
| Hearthstone launched before `log.config` has a `[Power]` section | App adds it; the first game(s) are tracked with `wasConceded = false` (unknown) until Hearthstone is restarted. |
| Concede line arrives late or not at all | Short wait before upload (to be measured, B12); otherwise `false`. Draws still read as losses unless the log also gives `TIED`. |
| SmartScreen warning on unsigned installer | Document it, or sign the installer. |
| Missing the 30 Nov date | Milestones 1–4 deliver a working replacement; the tray UI and installer polish can follow, running the app manually meanwhile. |
