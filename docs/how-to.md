# HSJeeves: installation and setup

HSJeeves is a small Windows app that runs in the background while you play
Hearthstone. After every finished **ranked** match it sends your result and
your rank before and after the match to Jeeves, so your rank history is
tracked automatically. You don't have to do anything while you play.

## What you need

- Windows 10 or 11 (64-bit) with .NET Framework 4.7.2 or newer (already part
  of current Windows).
- Hearthstone installed through the Battle.net app.
- A Jeeves account on Discord, to get your API key.
- Hearthstone Deck Tracker (HDT) is **not** required. If you have it, HSJeeves
  runs next to it, but see "Switching from the HDT plugin" below.

## 1. Get your API key

1. Open Discord and send the command `/hdttoken` to the **Jeeves** bot.
2. Jeeves replies with your API key. If you used the old HDT RankTracker
   plugin, the same key works.
3. Keep the key private: anyone who has it can post results as you.

## 2. Install

1. Download `HSJeeves-Setup-<version>.exe` from the release page *(link to be
   added when the release is published)*.
2. **Optional check:** the release page lists a SHA-256 checksum. In PowerShell:
   `Get-FileHash .\HSJeeves-Setup-<version>.exe -Algorithm SHA256`. The value
   must match the one on the release page.
3. Run the installer. You don't need administrator rights: it installs for
   your Windows user only.
4. **Windows will warn you.** The installer is not code-signed, so Windows
   SmartScreen shows "Windows protected your PC" and "Unknown publisher".
   Click **More info**, then **Run anyway**. Your browser may also ask you to
   confirm that you want to keep the download.
5. In the wizard:
   - **API key:** paste the key from step 1. You can leave it empty and enter
     it later in Settings.
   - **Hearthstone folder:** this page appears only if the installer could not
     find Hearthstone. Select the folder that contains `Hearthstone.exe`.
   - **Start HSJeeves when I sign in to Windows:** keep it ticked so the app is
     running when you play.
6. Finish. A HSJeeves icon appears in the notification area (click the **^**
   arrow next to the clock if you don't see it).

## 3. Switching from the HDT plugin

HSJeeves starts in **dry-run mode**: it detects your games and writes them to a
folder, but uploads nothing. This keeps a game from being counted twice if the
old HDT RankTracker plugin is still active. To go live:

1. In HDT, switch off the RankTracker plugin (**Options > Plugins**).
2. In HSJeeves, right-click the tray icon > **Settings**, paste your key if
   needed, click **Test connection**, **untick Dry run**, then **Save**.

While dry run is on, the tray icon shows a warning symbol and every finished
game shows a "NOT uploaded" notice.

## 4. Restart Hearthstone once

HSJeeves reads Hearthstone's game log to see whether a game ended by a concede.
On first start it switches that log on, but Hearthstone only applies the change
when it starts. If Hearthstone was already running, close it and open it again
once. Without the log, games are still uploaded, but never marked as conceded.

## 5. Check that it works

1. Start Hearthstone. The tray menu shows "Hearthstone: connected" and your
   region.
2. Play a ranked match (Standard, Wild, Classic or Twist). Concedes count.
3. A few seconds after the match, the tray menu shows it under "Last game" and
   the upload status. A balloon notice appears only for problems.

Casual, Arena, Battlegrounds, Mercenaries and friendly games are not uploaded.
Ranks differ per region (US, EU, AP), and HSJeeves handles all of them.

## Settings

Right-click the tray icon and choose **Settings** (or double-click the icon):

- **API URL and API key**, with **Test connection**;
- **Dry run** on or off;
- **Start with Windows**;
- **Hearthstone folder**, only if it was not found automatically.

**Save** applies the changes and closes the window.

## Updating

Run the newer installer. It closes the running app, replaces the files and
keeps your settings and key.

## Uninstalling

**Settings > Apps > Installed apps > HSJeeves > Uninstall.** This removes the
app and its startup entry. Your settings, queue and logs stay in
`%APPDATA%\HSJeeves`; delete that folder too if you want a full cleanup.

## Troubleshooting

| Problem | What to do |
|---|---|
| No tray icon | Click the **^** arrow next to the clock. Otherwise start HSJeeves from the Start menu. |
| Warning icon: "DRY RUN" | Dry run is on. See "Switching from the HDT plugin". |
| Warning icon: API key missing or rejected | Open Settings, paste a current key from `/hdttoken`. |
| "Hearthstone folder not found" | Settings > Hearthstone folder > Browse, then select the folder with `Hearthstone.exe`. |
| Games are not uploaded | Check Settings > **Test connection**, and that Dry run is off. Failed uploads are kept and retried for up to 7 days, so nothing is lost. |
| Stopped working after a Hearthstone update | Reading the game's memory can break after a patch until HSJeeves is updated. Install the latest version. |

### Files and logs

Everything is under `%APPDATA%\HSJeeves\`:

| Path | Contents |
|---|---|
| `settings.json` | Your settings. The API key is stored encrypted. |
| `queue\` | Finished games waiting to be uploaded. |
| `rejected\` | Games the server refused (bad data). |
| `dryrun\` | Games written while Dry run was on. |
| `logs\` | Daily log files, kept for 7 days. Tray menu > **Open logs folder**. The logs never contain your key. |

## What HSJeeves sends

For each finished ranked match: a unique match ID, start and end time, format,
result, whether a player conceded, your region and BattleTag, **the opponent's
BattleTag**, and your rank before and after. It does not send chat, decks,
friends or collection data.

## For developers

See [build.md](build.md) and [design/hsjeeves-app.md](../design/hsjeeves-app.md).
