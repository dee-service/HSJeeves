# HSJeeves — installation and setup

HSJeeves is a small Windows app that runs in the background while you play
Hearthstone. After every finished **ranked** match it sends your result and
your rank before and after the match to Jeeves, so your rank history is
tracked automatically. You don't have to do anything while you play.

> **Draft.** This guide describes the installer and app as designed in
> [design/hsjeeves-app.md](../design/hsjeeves-app.md). Menu names, file names
> and the download link are placeholders until the first release. Items marked
> *(TODO)* need to be confirmed against the real installer.

## What you need

- Windows 10 or 11 (64-bit).
- Hearthstone installed through the Battle.net app.
- A Jeeves account on Discord, to get your API key.
- Hearthstone Deck Tracker (HDT) is **not** required. If you have it, HSJeeves
  runs alongside it without any conflict.

## 1. Get your API key

1. Open Discord and send the command `/hdttoken` to the **Jeeves** bot.
2. Jeeves replies with a direct message containing your API key.
3. Keep that message open. You will paste the key during setup.

The key is personal. Don't share it: anyone who has it can post results as
you. If it leaks, run `/hdttoken` again to get a new one *(TODO: confirm that
this revokes the old key)*.

## 2. Install

1. Download `HSJeeves-Setup-<version>.exe` from *(TODO: release page link)*.
2. Run it. You don't need administrator rights, because HSJeeves installs for
   your Windows user only.
3. If Windows shows **"Windows protected your PC"**, click **More info**, then
   **Run anyway**. This warning appears for installers without a paid
   publisher certificate. *(Remove this step if the installer gets signed.)*
4. Follow the wizard:
   - **Your Jeeves API key:** paste the key from step 1. You can leave it empty
     and enter it later in Settings (see below).
   - **Additional tasks:** keep **Start HSJeeves with Windows** ticked so it
     is always running when you play. Keep **Launch HSJeeves now** ticked.
5. Click **Finish**. A HSJeeves icon appears in the notification area at the
   bottom right of the taskbar. If you don't see it, click the **^** arrow to
   show hidden icons.

## 3. Restart Hearthstone once

HSJeeves reads Hearthstone's game log to detect when a match ends. On first
start it switches that log on for you, but Hearthstone only picks up the
change when it starts.

- If Hearthstone is running when you first start HSJeeves, **close it and open
  it again**. HSJeeves shows *"Restart Hearthstone once to enable logging"*
  when this is needed.
- You only have to do this once.

If you use HDT, the log is probably already enabled and no restart is needed.

## 4. Check that it works

1. Start Hearthstone. The HSJeeves tray icon shows that it found the game
   *(TODO: describe the icon states: waiting for Hearthstone, tracking, key
   missing, error)*.
2. Play a ranked match (Standard or Wild). Concedes count too.
3. A few seconds after the match ends, the result is sent. Right-click the
   tray icon to see the status of the last upload.
4. Check your rank history on the Jeeves side *(TODO: where players see it)*.

Casual, Arena, Battlegrounds, Mercenaries and friendly games are not
uploaded.

## Settings

Right-click the tray icon and choose **Settings** to:

- change or paste your **API key**, then use **Test connection** to check it;
- turn **Start with Windows** on or off.

## Running next to HDT

HSJeeves and HDT only read from the game, so they can run at the same time.
After a Hearthstone update, one of them may work again a little before the
other. When that happens, HSJeeves keeps trying, and the match is uploaded
once reading works again.

## Updating

Run the newer installer over the existing installation. It closes HSJeeves,
replaces the files and keeps your settings and API key, so it doesn't ask
for the key again.

## Uninstalling

**Settings → Apps → Installed apps → HSJeeves → Uninstall.** The uninstaller
asks whether to also delete your settings, the upload queue and the logs. It
also removes HSJeeves from Windows startup.

## Troubleshooting

| Problem | What to do |
|---|---|
| No tray icon | Click the **^** arrow next to the clock. Otherwise start HSJeeves from the Start menu. |
| Icon says the API key is missing | Open **Settings** and paste your key from `/hdttoken`. |
| Matches don't upload | Use **Test connection** in Settings. Check that your key is correct and that you're online. Failed uploads are saved and retried automatically, so nothing is lost. |
| Nothing happens after a match | Make sure the game mode was ranked. Close and restart Hearthstone once so logging is switched on. |
| Rank is missing from an upload | The game didn't report the new rank in time. The match is still recorded. If this happens a lot, send us the log file (below). |
| Stopped working after a Hearthstone update | Reading the game's memory can break after a patch until HSJeeves is updated. Install the latest version. |

### Files and logs

HSJeeves keeps everything under `%LocalAppData%\HSJeeves\`:

| Path | Contents |
|---|---|
| `settings.json` | Your settings. The API key is stored encrypted. |
| `pending-uploads.jsonl` | Matches waiting to be re-sent after a failed upload. |
| `logs\` | Log files. Attach the latest one when you report a problem. |

The logs never contain your API key.

## What HSJeeves sends

For each finished ranked match: a unique match ID, start and end time, mode
and format, result, whether you conceded, your region, your BattleTag and your
opponent's BattleTag, and your rank before and after the match. It does not
send chat, decks, friends or collection data.

## For developers

To build HSJeeves and the installer yourself, see
[design/hsjeeves-app.md](../design/hsjeeves-app.md).
