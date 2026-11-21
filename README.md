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
