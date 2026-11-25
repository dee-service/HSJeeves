# Building HSJeeves

## Prerequisites

- Windows 10/11 x64, .NET SDK (checked with 10.0), .NET Framework 4.7.2 targeting pack.
- The memory reader, from the HS-Magic project (`D:\_dev\My_Projects\HS-Magic`, UnitySpy based, replaces
  HearthMirror, so no HDT files are needed). Build it there (`scripts/build-unityspy.ps1`, then
  `dotnet build HsMagic.sln -c Release`) and copy the DLLs from
  `src/HsMagic.HearthMirrorCompat/bin/Release/net472/` into `src/lib_compat/`:
  `HsMagic.HearthMirrorCompat.dll`, `HsMagic.Mirror.dll`, `UnitySpy.dll`, `UnitySpy.HearthstoneLib.dll`,
  `Newtonsoft.Json.dll`. `src/lib_compat/` is not committed. See HS-Magic `docs/hsjeeves-integration.md`.
- `src/lib/Newtonsoft.Json.dll` (version 13, used by the app; the reader's own copy is older, the build adds a binding redirect).

## Build and test

```powershell
dotnet build HSJeeves.sln -c Release
dotnet test HSJeeves.sln -c Release
```

Projects: `src/HSJeeves` (tray app), `src/HSJeeves.Spike` (console spike that
writes `logs/log-spike.log`), `src/HSJeeves.Tests` (xUnit).

## Installer and publishing

Extra prerequisites: Inno Setup 6.3+ (`winget install JRSoftware.InnoSetup`), and for
publishing, Windows OpenSSH with a private key that is authorised on the server.

1. **Bump the version** in `src/HSJeeves/HSJeeves.csproj`, line `<Version>0.1.1</Version>`.
   This is the only place. The app, the tray menu, the update check, the installer file
   name and the manifest all read it. Every release needs a new number.
2. **Exit a running HSJeeves** (tray menu > Exit), or the build cannot overwrite the exe.
3. **Build, test and compile the installer:**

   ```powershell
   .\installer\build.ps1
   ```

   Ends with `Done: dist\HSJeeves-Setup-<version>.exe` and a SHA-256 line. A failing test
   stops the script.
4. **Publish to the website:**

   ```powershell
   .\installer\publish.ps1 -Notes "What changed" `
     -Remote jtarjanyi@10.10.28.208:/opt/hs-snake/downloads `
     -IdentityFile $env:USERPROFILE\.ssh\algocloude-nexus
   ```

   It keeps the newest 3 versions, uploads only files the server does not have yet, and
   writes `versions.json`. Expect `Kept: <new>, <previous>, <older>` and no password prompt.
5. **Check** <https://jeeves.dee-service.cc/#hsjeeves>: the new version is latest, the older
   ones are listed under "Previous versions". An installed older version reports the update
   within a day (20 seconds after its next start).

Details, rollback and troubleshooting: [release.md](release.md).
