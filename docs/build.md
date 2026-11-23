# Building HSJeeves

## Prerequisites

- Windows 10/11 x64, .NET SDK (checked with 10.0), .NET Framework 4.7.2 targeting pack.
- The HearthMirror DLLs. They are **not committed** (licence unverified, see
  `docs/open-questions.md` A6). Copy these four files from an installed HDT
  (`%LocalAppData%\HearthstoneDeckTracker\app-<version>\`) into `src/lib/`:
  `HearthMirror.dll`, `untapped-scry-dotnet.dll`,
  `System.Reflection.DispatchProxy.dll`, `Newtonsoft.Json.dll`.

## Build and test

```powershell
dotnet build HSJeeves.sln -c Release
dotnet test HSJeeves.sln -c Release
```

Projects: `src/HSJeeves` (tray app), `src/HSJeeves.Spike` (console spike that
writes `logs/log-spike.log`), `src/HSJeeves.Tests` (xUnit).
