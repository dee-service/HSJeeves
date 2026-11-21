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

```text
 Hearthstone.exe ── Power.log ──► LogWatcher ──► GameTracker ──► Uploader ──► backend
        │                                            ▲   │
        └──── process memory ──► RankReader (HearthMirror)  └──► PendingQueue (jsonl)
                                                     ▲
 ProcessMonitor ─────────────────────────────────────┘ (start/stop reading when game runs)
```

| Component | Responsibility |
|---|---|
| **ProcessMonitor** | Polls (~2 s) for `Hearthstone.exe`. Starts the log watcher and rank reader when it appears, stops them when it exits. Also resets cached region. |
| **LogWatcher** | Tails `Power.log` of the *current* Hearthstone session. Emits `GameStarted`, `GameEnded(result, conceded)`. Reopens on truncation/rotation. |
| **RankReader** | Thin wrapper over HearthMirror: `GetMedalInfo()`, `GetAccountId()`, match info (game type, format, names). Owns the retry loop (HDT's was 5 × 150 ms; we use our own, see below). |
| **GameTracker** | State machine that combines the above into one `MatchRecord` per game. |
| **Uploader** | Builds the JSON payload, POSTs it with the Bearer key, and falls back to the retry queue on failure. Ported nearly unchanged from `RankTrackerPlugin.SendToServer`. |

Plus: **TrayUi** (status icon, menu, settings window), **Settings**, **Logging**.

### Replacing what HDT provided

| Plugin used (from HDT) | HSJeeves source |
|---|---|
| `GameEvents.OnGameEnd` | `LogWatcher` sees `GameEntity` tag `STATE = COMPLETE` |
| `Turns`, `Result`, `WasConceded` | Log: `TURN` tag, `PLAYSTATE` of the local player (`WON`/`LOST`/`CONCEDED`/`TIED`) |
| `GameMode`, `Format` | HearthMirror match info (game type + format type); mapped to the same strings the backend already receives (`Ranked`, `Standard`, …) |
| Pre-match rank | `GetMedalInfo()` snapshot at `GameStarted` |
| Post-match rank (`...After`) | `GetMedalInfo()` polling after `STATE COMPLETE` |
| `Region` | `GetAccountId()` → `(accountHi >> 32) & 0xFF` → `US`/`EU`/`AP` (same `RegionLabel` remap) |
| `GameId` | `Guid.NewGuid()` created at `GameStarted` (idempotency key) |
| Player/opponent BattleTag | HearthMirror match info; log player lines as fallback |

> **Verify in the spike:** exact HearthMirror member names and the
> `MedalInfo` shape differ between versions. The table above is the intent,
> not the final API calls.

### Game lifecycle (GameTracker state machine)

```text
Idle ──GameStarted──► InGame ──STATE COMPLETE──► AwaitingRank ──rank changed | timeout──► Upload ──► Idle
```

- **GameStarted:** if the game type is not in `UploadedModes`, stay `Idle`
  for this game. Otherwise create `GameId`, snapshot pre-match medal info,
  region, names.
- **STATE COMPLETE:** capture result/conceded/turns. If `Turns < 1`, drop the
  game (same rule as the plugin's 0-turn guard).
- **AwaitingRank:** poll `GetMedalInfo()` every ~250 ms for up to **8 s**
  (same budget as the plugin). The post-match rank is "ready" when it differs
  from the pre-match snapshot, or is non-null after the match-end screen
  loaded. On timeout, upload with whatever is available and log a warning —
  same behaviour as today.
- Polling runs on a background thread. Nothing blocks the log tail.

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
  attempts or 7 days. Safe because `gameId` is idempotent server-side.
- Without an `ApiKey`, log a warning and skip uploading (as today).
- **One open question for the backend:** `OpponentBattleTag`/rank fields and
  `rank.Rank` come from HDT's `GameStats`; confirm the backend actually uses
  each field so we don't port fields nobody reads.

## Settings and first-run setup

Stored per user in `%LocalAppData%\HSJeeves\settings.json`:

```json
{ "apiUrl": "https://…/API", "apiKey": "", "startWithWindows": true }
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

### Log configuration

HearthMirror reads memory, but game-end detection needs `Power.log`. On
startup the app checks `%LocalAppData%\Blizzard\Hearthstone\log.config`:

- file missing → create it with a `[Power]` section (`LogLevel=1`,
  `FilePrinting=true`, `ConsolePrinting=false`, `Verbose=true`);
- file exists with a `[Power]` section → leave untouched;
- file exists without one → **append** the section, never overwrite
  (HDT manages the same file).

Changes to `log.config` only apply on the next Hearthstone launch; the tray
status says "Restart Hearthstone once to enable logging" when the app had to
change it.

## Running alongside HDT

- Both only read. The app opens `Power.log` with
  `FileShare.ReadWrite | FileShare.Delete` and handles the game
  truncating/rotating the file on launch.
- Poll game memory only around game start and end, not continuously.
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
  HSJeeves.Tests/    (GameTracker state machine, log parsing, payload)
installer/
  HSJeeves.iss       (Inno Setup script)
  build.ps1          (publish app -> compile installer)
design/
  hsjeeves-app.md    (this file)
docs/
  how-to.md          (player installation and setup guide)
```

Unit-testable without the game: log parsing (fixtures from real
`Power.log` snippets), the `GameTracker` state machine (fake `RankReader`),
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
- Creating or extending `log.config` is **left to the app**, not the
  installer, so the logic lives in one place and is testable.
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
2. **LogWatcher + GameTracker:** detect game start/end/result from
   `Power.log`, unit-tested against fixtures.
3. **Rank capture:** pre/post-match snapshot and the post-match polling.
4. **Uploader + retry queue + settings:** end-to-end to the backend,
   running side by side with the old plugin to compare payloads.
5. **Tray UI + autostart + log.config handling.**
6. **Installer** (`HSJeeves.iss`, `build.ps1`), test on a clean Windows
   user account/VM: install → paste key → play a ranked game → see it
   upload → upgrade-in-place → uninstall.
7. Update README, publish release, retire the plugin.

## Risks

| Risk | Mitigation |
|---|---|
| HearthMirror offsets break after a game patch | Pin a version, degrade gracefully, keep an update path (rebuild + new installer). |
| HearthMirror licence/redistribution unclear | Check in the spike, before building around it. |
| Post-match rank not available in time | 8 s polling window, upload partial data, retry/reconcile is a possible later improvement. |
| Hearthstone launched before `log.config` exists | App writes it and tells the user to restart Hearthstone once. |
| SmartScreen warning on unsigned installer | Document it, or sign the installer. |
| Missing the 30 Nov date | Milestones 1–4 deliver a working replacement; the tray UI and installer polish can follow, running the app manually meanwhile. |
