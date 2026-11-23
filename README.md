# HSJeeves

Standalone Windows tray app that tracks finished ranked Hearthstone matches
(result plus rank before and after) and uploads them to Jeeves. It reads the
game with HearthMirror and `Power.log`, so it does not need Hearthstone Deck
Tracker.

- Design: [design/hsjeeves-app.md](design/hsjeeves-app.md)
- Player install guide (draft): [docs/how-to.md](docs/how-to.md)

## Status

Milestone 1 (spike) done: HearthMirror reads rank data standalone.
See the milestones in the design doc for what comes next.

## Setup for development

`src/lib/` holds `HearthMirror.dll`, `untapped-scry-dotnet.dll`,
`System.Reflection.DispatchProxy.dll` and `Newtonsoft.Json.dll`. They are not
committed. Copy them from an HDT install
(`%LocalAppData%\HearthstoneDeckTracker\current`) into `src/lib/`, then:

```powershell
dotnet build src/HSJeeves.Spike -c Release
src\HSJeeves.Spike\bin\Release\net472\HSJeeves.Spike.exe   # with Hearthstone running
```

## Privacy and what HSJeeves reads

- **Game memory:** HSJeeves reads Hearthstone's memory (read-only, through the
  HearthMirror library, the same one Hearthstone Deck Tracker uses). It does
  not change the game, inject code or send input. It reads only your own
  account's rank and match data. This is not an official Blizzard API, and
  HSJeeves makes no claim that Blizzard approves it. HDT's FAQ
  ([Is Blizzard okay with this?](https://github.com/HearthSim/Hearthstone-Deck-Tracker/wiki/FAQ))
  only quotes statements from Blizzard staff about trackers in general, so
  treat this as a tool in the same category as HDT and use it at your own risk.
- **Power.log:** only one line type is read from `Power.log`, the one that says
  a player conceded. Nothing else from the log is stored or sent.
- **What is uploaded** for each finished ranked game: your BattleTag, region,
  format, result, concede flag, start and end time, your rank before and after,
  and **the opponent's BattleTag**. Nothing is uploaded for casual games or
  while Hearthstone is not in a ranked game.
- **Where:** only to the Jeeves backend, with your personal API key from the
  Discord `/hdttoken` command. The key is stored encrypted for your Windows
  user (DPAPI) and is never written to the logs.
- **Local files:** settings, queue and logs are in `%APPDATA%\HSJeeves`. Logs
  rotate after 7 days. "Dry run" in Settings writes payloads to a folder and
  uploads nothing.
