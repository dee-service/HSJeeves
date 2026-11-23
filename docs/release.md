# Releasing HSJeeves (maintainer notes)

How to build a new version and publish it on the Jeeves website. Written for
the maintainer, not for players (player guide: [how-to.md](how-to.md)).

Download page: https://jeeves.dee-service.cc/#hsjeeves
Server folder: `jtarjanyi@10.10.28.208:/opt/hs-snake/downloads`

## One-time setup

- Inno Setup 6.3+ (`winget install JRSoftware.InnoSetup`).
- The four DLLs in `src/lib/` (see [build.md](build.md)).
- Windows OpenSSH (`ssh`, `scp`) and a private key whose public half is in
  `~/.ssh/authorized_keys` on the server. Ours: `%USERPROFILE%\.ssh\algocloude-nexus`.
  Without a key the script asks for the password on every ssh and scp call.

## Release steps

1. **Bump the version** in `src/HSJeeves/HSJeeves.csproj` (`<Version>`, three
   parts, for example `0.1.2`). This is the only place; the installer, the
   file name, the tray menu and the update check all read it. A new version
   number is required: the website and the installer both treat the same number
   as "already installed".
2. **Build, test, compile the installer:**
   ```powershell
   .\installer\build.ps1
   ```
   It builds Release, runs the tests (stops on a failure), compiles
   `dist\HSJeeves-Setup-<version>.exe` and writes `dist\HSJeeves-Setup-<version>.exe.sha256`.
   Stop a running HSJeeves first, or the build cannot overwrite the exe.
3. **Publish:**
   ```powershell
   .\installer\publish.ps1 -Notes "What changed, one line" `
     -Remote jtarjanyi@10.10.28.208:/opt/hs-snake/downloads `
     -IdentityFile $env:USERPROFILE\.ssh\algocloude-nexus
   ```
   What it does:
   - copies the installer and its `.sha256` into `dist\downloads\` (the local mirror),
   - keeps the newest 3 versions there and on the server (`-Keep` to change),
     deleting older ones,
   - rebuilds `versions.json` (version, file, SHA-256, size, date, notes),
   - over ssh: removes pruned files and lists the server folder, then over scp
     uploads only files that are missing or differ in size, and `versions.json` last.

   Without `-Remote` it only updates `dist\downloads\`. `-Dest` changes that folder.
4. **Check the result:**
   - https://jeeves.dee-service.cc/#hsjeeves shows the new version as latest and
     the older ones under "Previous versions".
   - `curl https://jeeves.dee-service.cc/downloads/versions.json`
   - Download the file and compare `Get-FileHash <file> -Algorithm SHA256` with the page.
5. **Installed copies** see the new version within a day (check at start + every
   24 h) and show "Update available" in the tray menu. Nothing updates itself.
6. Commit the version bump (feature branch, pull request; `main` only changes by PR).

## Rollback

Old versions stay on the site (the last 3). To roll back yourself or help a
player: download the older installer from "Previous versions" and run it over
the newer install. The installer asks "Install the older version?" first; the
settings and key are kept.

To take a bad version off the site: delete its three files on the server
(`.exe`, `.exe.sha256`) and its entry from `versions.json`, or re-run
`publish.ps1` after deleting them from `dist\downloads\` (the manifest is
rebuilt from the folder; note that this will not delete them on the server
unless they are pruned by `-Keep`, so remove them with `ssh` as well).

## How the pieces fit

- The website container (HS-Snake repo, `www/`) serves `/downloads/` from the
  host folder `./downloads` next to its `docker-compose.yml`, mounted read-only.
  Publishing needs no rebuild or redeploy of the site.
- The app reads `https://jeeves.dee-service.cc/downloads/versions.json`
  (setting `UpdateUrl`) and only informs the user.
- The installer is **not code-signed** (decision G4). Players see the SmartScreen
  "Run anyway" step; the SHA-256 is on the page and in `how-to.md`.

## Troubleshooting

| Problem | What to do |
|---|---|
| `build.ps1`: "Could not copy ... HSJeeves.exe" | A running HSJeeves locks the file. Exit it (tray menu > Exit), build again. |
| `build.ps1`: Inno Setup not found | `winget install JRSoftware.InnoSetup`. |
| `publish.ps1` hangs after the password | Use `-IdentityFile`. The script already runs ssh with `-n`. Test the key alone: `ssh -n -T -i <key> -o BatchMode=yes jtarjanyi@10.10.28.208 "ls /opt/hs-snake/downloads"`. |
| `ssh failed (does ... exist on the server?)` | Create the folder on the server: `mkdir -p /opt/hs-snake/downloads`, and check that the www container mounts it. |
| Page says "Downloads are not available" | `versions.json` is missing on the server, or the folder is not mounted into the www container. |
| Installed app never shows the update | The log (`%APPDATA%\HSJeeves\logs`, tray menu > Open logs folder) has a line "update check done ..." per check (since 0.1.2). "Check for updates" must be ticked in Settings. |
