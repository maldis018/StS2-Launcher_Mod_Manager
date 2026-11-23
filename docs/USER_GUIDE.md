# StS2 Launcher Redux User Guide

한국어: [USER_GUIDE.ko.md](USER_GUIDE.ko.md)

This is a step-by-step guide for first-time users of **StS2 Launcher Redux**, with screenshots of what each step looks like on your phone. It was originally written in Korean for StS2 Launcher Mod, and the Korean version is the original text. Some screenshots still show the older app name and a few Korean strings; the steps are the same.

StS2 Launcher Redux is an unofficial fan project and is not affiliated with Mega Crit Games.

> **Tested on**: Galaxy Z Fold7 (both the main and cover screens). For tablets and other form factors, see [issue #6](https://github.com/iunius612/StS2-Launcher_Mod_Manager/issues/6).

---

## Contents

1. [Download and install the APK](#1-download-and-install-the-apk)
2. [First launch: grant permission](#2-first-launch-grant-permission)
- [Coming from StS2 Launcher Mod](#coming-from-sts2-launcher-mod)
3. [Sign in to Steam](#3-sign-in-to-steam)
4. [Pick a branch and download the game](#4-pick-a-branch-and-download-the-game)
5. [The main screen at a glance](#5-the-main-screen-at-a-glance)
6. [Starting the game (PLAY)](#6-starting-the-game-play)
7. [Game updates and switching branches (CHECK GAME UPDATE)](#7-game-updates-and-switching-branches-check-game-update)
8. [Auto Sync toggle](#8-auto-sync-toggle)
9. [Using the Save Manager dialog](#9-using-the-save-manager-dialog)
10. [Manual Push and Pull](#10-manual-push-and-pull)
11. [Local Backup](#11-local-backup)
12. [Installing mods (optional)](#12-installing-mods-optional)
13. [Launcher updates (CHECK LAUNCHER UPDATE)](#13-launcher-updates-check-launcher-update)
14. [Debug logs (Debug toggle)](#14-debug-logs-debug-toggle)
15. [FAQ](#15-faq)
16. [Known issues and unsupported devices](#16-known-issues-and-unsupported-devices)
17. [Useful adb commands (optional)](#useful-adb-commands-optional)

---

## 1. Download and install the APK

Download the newest `StS2LauncherRedux-vX.Y.Z.apk` from the [Releases page](https://github.com/maldis018/StS2-Launcher_Mod_Manager/releases/latest) to your phone.

  <img src="images/1.jpg" width="600">

You may need to allow **installing apps from unknown sources**. Android asks once, the first time your browser or file manager installs an APK.

  <img src="images/2.jpg" width="600">

> **Updating**: installing a newer APK over the old one (signed with the same key) keeps your saves, Steam sign-in and the downloaded game (~3 GB). Minor upgrades such as 0.5.0 to 0.5.1 leave your data as it is.

---

## 2. First launch: grant permission

On first launch the app asks for **All files access**.

  <img src="images/3.jpg" width="600">

The launcher needs it because:
- you can put mods straight into the mod folder (`/storage/emulated/0/StS2LauncherRedux/Mods/`), and
- Local Backup (save backups) has to write save snapshots to shared storage.

Turn the toggle on, then press Back to return to the launcher.

  <img src="images/4.jpg" width="600">

---

## Coming from StS2 Launcher Mod

Redux is a separate app (package `dev.aldis.sts2launcherredux`, folder `/storage/emulated/0/StS2LauncherRedux/`). It installs next to StS2 Launcher Mod and Ekyso's StS2 Launcher instead of replacing them, and the apps don't share data.

Android doesn't let one app read another app's private data, so these stay with the old app:

- **Your Steam sign-in**: sign in again in Redux.
- **The downloaded game (~3 GB)**: download it again in Redux.
- **Your live saves**: they come back from Steam Cloud. If you didn't use Steam Cloud, tap **Local Backup** in the old app before you switch.

Everything in the old shared folder can come over. On first launch (once All files access is granted), Redux offers to copy your mods, disabled mods, `mod_config.json`, Local Backup sets and LAN config from `/storage/emulated/0/StS2LauncherMM/`. It only copies: the old folder is never changed, and nothing you already have in the new folder is overwritten. Logs are left behind.

To run the import again later, open **MOD MANAGER > LOCAL > Import from StS2LauncherMM...**.

---

## 3. Sign in to Steam

The sign-in screen shows a QR code. There are two ways to sign in.

### Sign in with another phone or tablet (recommended)

1. On the other device, open the Steam app and go to the QR scanner in the **Steam Guard** tab.
2. Scan the QR code on the launcher screen and approve the sign-in in the Steam app.

You never type your password, so the launcher never sees it. If the code expires or you deny the request, tap **NEW CODE** to try again.

### Sign in with only this phone

1. Tap **SIGN IN ON THIS PHONE**.
2. Enter your Steam account name and password, then tap **LOGIN**.
3. If you have the Steam mobile app, it asks you to approve the sign-in. Otherwise, enter the Steam Guard code you received (for example by email).

Your password is sent to Steam once and never stored. To go back to the QR code, tap **Use a QR code from another device instead**.

  <img src="images/5.jpg" width="600">

### Steam Guard code warning

When you sign in on this phone and switch to another app (such as the Steam mobile app) to get your two-factor code, **the sign-in session fails after the launcher has been in the background for only about 5 to 10 seconds**, and you have to start over.

  <img src="images/6.jpg" width="600">

**Workarounds**:
- **Pop-up window mode**: open the Steam mobile app in a pop-up window to read the code, then tap the launcher right away to keep it focused.
- **Split screen**: show the launcher and the Steam app side by side, and read from one while typing into the other.

  <img src="images/7.jpg" width="600">

---

## 4. Pick a branch and download the game

After you sign in, if there are no game files yet, the **branch picker** appears.

  <img src="images/8.jpg" width="600">

> **Choose the same branch you use on PC.** Progress from a beta branch on mobile can't be carried to the regular branch on PC, or the other way around (Steam Cloud reports a sync conflict). See the FAQ for details.

Pick a branch and start the download. The game is about **3 GB**, so Wi-Fi is recommended.

  <img src="images/9.jpg" width="600">

While the download runs you can tap **PAUSE**; everything downloaded so far is kept. Tap **RESUME DOWNLOAD** to continue from where it stopped. If the connection drops or the app is closed, the download also resumes next time. The download can stall with the screen off, so keep the screen on until it finishes.

---

## 5. The main screen at a glance

This is the main launcher screen once the download finishes. The table gives a one-line summary of each control; details are in the sections below.

  <img src="images/10.jpg" width="600">

| Control | What it does | Details |
|---|---|---|
| **PLAY** | Starts the game. It first checks your progress against Steam Cloud, then enters the game | [Section 6](#6-starting-the-game-play) |
| **CHECK GAME UPDATE** | Checks for game updates and lets you **switch branches** | [Section 7](#7-game-updates-and-switching-branches-check-game-update) |
| **CHECK LAUNCHER UPDATE** | Checks GitHub for a newer launcher APK, with in-app download and install | [Section 13](#13-launcher-updates-check-launcher-update) |
| **Auto Sync** toggle | Turns automatic Steam Cloud sync ON or OFF | [Section 8](#8-auto-sync-toggle) |
| **SAVE MANAGER** | Shows the current sync state and lets you compare cloud and local saves and decide by hand | [Section 9](#9-using-the-save-manager-dialog) |
| **MOD MANAGER** | Mod Hub: search, subscribe and auto-download from the Steam Workshop, plus disabling and removing mods | [Section 12](#12-installing-mods-optional) |
| **Push to Cloud** | Force-uploads all saves to Steam Cloud (special cases only) | [Section 10](#10-manual-push-and-pull) |
| **Pull from Cloud** | Force-downloads all Steam Cloud saves to the phone (special cases only) | [Section 10](#10-manual-push-and-pull) |
| **Local Backup** | Snapshots your whole save tree to shared storage (manual). Backups are also made automatically during the PLAY handshake | [Section 11](#11-local-backup) |
| **Debug** toggle (next to Console) | Saves the log capture to shared storage (ON by default, covers everything from boot to entering the game) | [Section 14](#14-debug-logs-debug-toggle) |

---

## 6. Starting the game (PLAY)

When you tap **PLAY**, the launcher first **checks your progress against Steam Cloud**. The screen may go black for a moment while the Save Manager downloads files from the cloud, so please wait. (If it takes more than 30 seconds to a minute, suspect a network problem or a bug.)

  <img src="images/11.jpg" width="600">

One of these happens next:

- **Both sides match, or neither has data**: you go straight to the game's main menu (no dialog).
- **Only one side has progress, or the two differ**: the **Save Manager dialog** appears automatically. See [Section 9](#9-using-the-save-manager-dialog).

On the first run, the game scans and unpacks shaders.

  <img src="images/12.jpg" width="600">

When the shader step finishes, you return to the launcher main screen after a moment.

After that, entering the game works like normal Slay the Spire 2.

> When you quit the game (game menu > Quit), the launcher waits for the cloud upload queue to empty (up to 5 minutes, usually 1 to 5 seconds). The process stays alive in the background during this time and you may see some Steam network traffic. This is normal.

---

## 7. Game updates and switching branches (CHECK GAME UPDATE)

> **Game and launcher checks are separate buttons.** **CHECK GAME UPDATE** (this section) and **CHECK LAUNCHER UPDATE** ([Section 13](#13-launcher-updates-check-launcher-update)) each have their own button, result and dialog.

Even when the game is already downloaded, tapping **CHECK GAME UPDATE** reopens the branch picker. Use it to switch branches (for example `public` to `public-beta`).

  <img src="images/13.jpg" width="600">

### Before you switch branches

- **A new branch is a full re-download** (~3 GB). Delta downloads are deliberately not used, because binaries on different branches may not match byte for byte.
- **Saves, Steam sign-in and mod settings are kept.** Only the game files (`game/`, `download_state/`) are downloaded again.
- **Match your PC's branch.** Different branches can cause Steam Cloud sync conflicts. See the [FAQ](#15-faq) entry on matching branches.

### Fixing wrong card, relic or potion images: Clear image cache

After a game update, **card, relic or potion** icons can show the wrong picture. This happens when the mobile texture cache (ETC2-compressed) is left over from the old build. There are two ways to recover.

**Automatic (no action needed in most cases)**

  <img src="images/29.jpg" width="600">

When a game update (changed PCK file) is detected, the launcher rebuilds the texture cache on the next start. A loading screen ("Rebuilding the image index cache") shows for 30 to 60 seconds, then the app starts normally. Your saves, progress and sign-in are untouched.

  <img src="images/30.jpg" width="600">

**Manual: the `Clear image cache` button**

  <img src="images/31.jpg" width="600">

Use this if automatic detection missed something, or you simply want to clear the cache:

1. Tap **CHECK GAME UPDATE** on the main screen.
2. In the branch picker, find the small gray **`Clear image cache`** button at the **bottom right**.
3. A confirmation dialog explains what will happen (about 30 to 60 seconds extra on the next launch, and what is kept).
4. Tap **OK**. The app restarts and shows the same loading screen as the automatic flow.

**What is kept and what is rebuilt:**

| Item | Result |
|---|---|
| Game PCK and download data (`game/`, `download_state/`) | **Kept**: no re-download |
| Saves and progress (`steam/<userId>/...`) | **Kept** |
| Steam sign-in and Auto Sync setting | **Kept** |
| Local Backup snapshots (`StS2LauncherRedux/Saves/`) | **Kept**: they live in shared storage, so clearing the cache doesn't touch them |
| Mobile texture cache (`etc2_cache/.godot/imported/`) | **Rebuilt** (about 30 to 60 seconds) |

This has the same effect as the old workaround of clearing app data and reinstalling, but without re-downloading the game. For background, see [issue #23](https://github.com/iunius612/StS2-Launcher_Mod_Manager/issues/23).

---

## 8. Auto Sync toggle

The **Auto Sync: ON / OFF** toggle on the main screen turns automatic Steam Cloud sync on or off.

  <img src="images/14.jpg" width="600">

| State | Behavior |
|---|---|
| **Auto Sync: ON** (default) | Saves are pushed to Steam Cloud automatically while you play, and checked with a dialog on the next PLAY |
| **Auto Sync: OFF** | Cut off from the cloud. Saves stay on the phone only and are not synced with other devices such as your PC |

### When to turn it OFF

Almost always leave it ON. Cases where OFF makes sense:

- **Temporary debugging**: if you suspect a cloud sync problem, turn it OFF and play locally, then turn it back ON and decide properly in the Save Manager.
- **Deliberately different progress from your PC**: for example, a separate character or run on mobile while the PC stays as it is. When you turn it ON again, a conflict dialog appears and you will have to decide then.

When you turn it back ON after being OFF, the **Save Manager detects the conflict automatically** and asks which side to keep. The dialog appears on the next PLAY.

---

## 9. Using the Save Manager dialog

The dialog appears automatically when Steam Cloud and your phone differ in progress. You can also tap **SAVE MANAGER** on the launcher screen at any time to check the current sync state.

### What the dialog shows

Each card shows the progress data of one profile.

| Item | Meaning |
|---|---|
| **Profile N** (subtitle) | Which profile the card is for (1, 2, 3, or a modded profile) |
| **Newer** badge | The more recently changed of the two cards (the newer of the mtimes of progress.save and current_run.save) |
| **Last modified / File size** | mtime and size of progress.save |
| **Total playtime** | Accumulated play time |
| **Current run** | Shows something like `Act 1 floor 3` if a run is in progress, otherwise a dash placeholder |
| **Record / Highest Ascension / Floors climbed / Relics discovered** | Cumulative stats from progress.save |

### What the dialog looks like

#### Case 1: Conflict

The body reads "Progress on this device and in Steam Cloud differs. Choose which version to keep." with two cards and the buttons **Cancel / Keep Local / Keep Cloud**. If several profiles differ at once, the text adds the number of profiles.

  <img src="images/15.jpg" width="600">

### Deciding which side to keep

The dialog highlights the button for the newer side (the card with the "Newer" badge). A typical decision:

| Situation | Choose |
|---|---|
| You just played on the phone and want to carry it to another device | **Keep Local** |
| You played on PC and want to bring it to the phone | **Keep Cloud** |
| Not sure | **Cancel**: cloud sync stays OFF for that session, and you can decide again at the next start |

> **When in doubt, play it safe**: look at the "Current run" row. The side with a run in progress, or the one further along (for example Act 1 floor 5 versus Act 1 floor 3), is usually the data you don't want to lose.

#### Case 2: Identical

The two cards match in size, stats and current run. The body reads "Your local progress and Steam Cloud are in sync. No action is needed." with a Close button.

  <img src="images/16.jpg" width="600">

---

## 10. Manual Push and Pull

The **Push to Cloud** and **Pull from Cloud** buttons are force-sync tools. **They aren't recommended for normal use.** Cloud sync happens automatically when the game starts and exits, and if that ever breaks, the Save Manager gives you precise options.

  <img src="images/17.jpg" width="600">

| Button | What it does |
|---|---|
| **Push to Cloud** | Force-uploads all local saves (progress, current_run, history `.run` files, prefs and settings, for every profile) to Steam Cloud |
| **Pull from Cloud** | Force-downloads all Steam Cloud saves to the phone |

### When to use them

Special cases only:

- **The Save Manager doesn't work for some reason** (for example the dialog doesn't respond) and you need to force a one-way sync.
- **Moving PC progress to the phone**: the Save Manager normally handles this, but you can use Pull if you want to grab everything in one go.

> The normal flow, an automatic check at PLAY plus the Save Manager dialog, is enough for everyday use.

---

## 11. Local Backup

In case your saves are overwritten by mistake, the launcher copies your whole save data to shared storage, **keeping the original folder structure**. Backups are made in two ways.

- **Manual backup**: tap **Local Backup** in the launcher's actions area, confirm, and the whole current save tree is snapshotted at once. When it finishes, a result dialog shows the number of files, total size and location.

  <img src="images/18.jpg" width="600">

- **Automatic backup**: made once per launch during the Steam Cloud handshake that runs when you tap PLAY. If local and cloud match, the local tree is snapshotted once. If they conflict, **both the side you keep and the side you discard** are backed up, so you can get the discarded data back even if you chose wrong.

> Backup files are **not renamed** (no `.bak` suffix). They are copied with their original file names and folder structure, so restoring is just copying files back to where they came from.

### Location and layout

Backups go under `/storage/emulated/0/StS2LauncherRedux/Saves/`, split by **type**. Inside each is a folder named for the backup time (`<ts>` = `yyyyMMdd_HHmmss`), and inside that is the original save tree.

```
StS2LauncherRedux/Saves/
  ├── manual/<ts>/<original tree>                    (Local Backup button = manual)
  └── auto/
      ├── <ts>_match/<original tree>                 (handshake: local and cloud match)
      └── <ts>_conflict/
          ├── kept/<original tree>                   (handshake conflict: the side you kept)
          └── discarded/<original tree>              (handshake conflict: the side you discarded)
```

`<original tree>` is the actual path the game uses:

```
steam/<SteamID>/[modded/]profile<N>/saves/<file name>
   e.g. steam/76561198xxxxxxxxx/profile1/saves/progress.save
        steam/76561198xxxxxxxxx/modded/profile2/saves/current_run.save
```

(The `modded/` level appears only for profiles played with mods enabled. Empty saves are not included in snapshots.)

### Retention

- **Manual backups (`manual/`) are never deleted automatically.** You made them on purpose, so they stay until you remove them.
- **Automatic backups (`auto/`) keep only the newest 10 sets**, and older ones are deleted oldest first (one `<ts>_match` or one `<ts>_conflict` counts as one set).

### How to restore

1. Tap **SAVE MANAGER** on the launch screen and choose **Restore Backup**. (With cloud sync off, the same button opens a small menu with **Restore Backup** and **Copy Profile**.)
2. Pick a snapshot. The folder's date, time and type (`_match` or `_conflict`) tell you which backup it is.
3. Confirm. All saves are rolled back to that snapshot, and your current saves are backed up automatically first, so the restore itself can be undone.
4. If cloud sync is on, the launcher then asks **Also upload to Steam Cloud?** Choose **Yes** to make the restored saves the cloud copy too. With **No**, the next sync shows the restored saves and the cloud copy as a conflict for you to settle.

The snapshots are plain copies of the save files, so you can also browse them with a file manager (or `adb pull`) under `StS2LauncherRedux/Saves/`, for example to keep a copy elsewhere. The game's live saves are in the app's private storage, so restoring has to go through **Restore Backup**.

---

## 12. Installing mods (optional)

There are two ways to install mods: **subscribing on the Steam Workshop (recommended)** and **manual install**. Both use the same `Mods/` folder, so you can mix them.

### 12-1. Install from the Steam Workshop (recommended)

Tap **MOD MANAGER** on the launcher home screen to open the **Mod Hub**. The Workshop features require you to be signed in to Steam.

  <img src="images/workshop_1.png" width="600">

The Mod Hub has four tabs:

| Tab | What it does |
|---|---|
| **WORKSHOP** | Search and subscribe. Sort (popular, new, trending, recently updated, rating), tag filters and infinite scroll |
| **SUBSCRIBED** | Your subscribed mods (synced with your Steam account). DETAIL / DISABLE / UNSUBSCRIBE |
| **LOCAL** | Mods you installed by hand (not from the Workshop) |
| **DOWNLOADS** | Download progress |

The portrait/landscape toggle at the top right rotates the Mod Hub UI (useful when you hold the phone upright).

  <img src="images/workshop_2.png" width="600">

#### Search and subscribe

- Search accepts a **title, body text, or a pasted Workshop URL or ID**. Unlisted mods can be looked up and subscribed to directly by URL or ID.
- **SUBSCRIBE** registers the subscription on your Steam account and starts the download right away. Downloaded mods go into the same `/storage/emulated/0/StS2LauncherRedux/Mods/` folder as manual installs.

  <img src="images/workshop_3.png" width="600">

- Each installed mod's origin (Workshop or manual) and enabled state are recorded in `Mods/mod_config.json`. You can open it with a file manager to check.

  <img src="images/workshop_4.png" width="600">

- **Automatic updates**: every time you open the Mod Hub, it checks your subscribed mods for updates and downloads them.
- **Dependency prompts**: if a subscribed mod requires another mod (for example BaseLib), a list dialog appears so you can subscribe to it right away.
- **DETAIL** opens the detail page with four tabs: description, update notes (the Workshop change log as is), discussions and comments. Discussion and comment text is shown by opening the Steam Community page in a browser.

  <img src="images/workshop_7.png" width="600">

> If you leave the Mod Hub while a download is in progress, the download is cancelled (you get a warning first).

#### Checking in the game

Subscribed mods appear in the game's installed mods list alongside manually installed ones. On the first run, follow the same "Load mods?" dialog flow described in 12-3 below.

  <img src="images/workshop_8.png" width="600">

### 12-2. Disabling mods (DISABLE / ENABLE)

**UNSUBSCRIBE deletes the mod from your device.** To turn a mod off for a while without deleting it, use **DISABLE**. Pick whichever fits.

| Button | What it does |
|---|---|
| **DISABLE** | Moves the mod to `StS2LauncherRedux/ModsDisabled/`: hidden from the game only, files kept |
| **ENABLE** | Restores it immediately (no re-download). If an update was missed while it was disabled, it asks whether to fetch it |
| **UNSUBSCRIBE** | Cancels the subscription and deletes the mod folder from the device |

  <img src="images/workshop_5.png" width="600">

- Disabled mods don't receive updates.
- Moving a folder between `Mods/` and `ModsDisabled/` yourself with a file manager is recognized the same way (what's on disk is what counts).

### 12-3. Manual install (file manager)

For mods that aren't on the Workshop, put them in the folder by hand:

1. We recommend **[ZArchiver](https://play.google.com/store/apps/details?id=ru.zdevs.zarchiver)** (free on the Play Store). Some stock OEM file managers, such as Samsung's **My Files**, can't see `/storage/emulated/0/StS2LauncherRedux/` because of Android's scoped storage rules (confirmed in testing).
2. In ZArchiver, go to `/storage/emulated/0/StS2LauncherRedux/Mods/`.
3. Copy the whole mod folder (for example `MyMod/`) into it. The folder should contain the `.dll`, an optional `.pck` and a `<ModId>.json` manifest. (Same as on PC.)

  <img src="images/19.jpg" width="600">
  <img src="images/20.jpg" width="600">
  <img src="images/21.jpg" width="600">

4. When you start the game, tap OK on the game's own **"Load mods?"** dialog. After you choose once, mods load automatically from then on.

  <img src="images/22.jpg" width="600">

Manually installed mods also show up in the Mod Hub's **LOCAL** tab and in `mod_config.json`, and work together with Workshop mods:

  <img src="images/workshop_6.png" width="600">

### 12-4. Mod compatibility

The Workshop makes mods easy to get, but **PC and mobile are structurally different**, so mods that rely on the following don't work on mobile:

1. **P2P game traffic** (Steam NetworkingSockets / SDR)
2. **The overlay and Steam Input**: these belong to the Steam client process and can't be implemented at the protocol level (for example QuickReload)

If a mod doesn't work, please report it in an [issue](https://github.com/maldis018/StS2-Launcher_Mod_Manager/issues) together with the debug log from [Section 14](#14-debug-logs-debug-toggle).

> **Merging mod saves**: after a game update changed how saves are stored, **the UnifiedSave mod can no longer be used.** If you need to merge mod saves, use [SaveMerger](https://steamcommunity.com/sharedfiles/filedetails/?id=3748139900). Before using any mod that touches saves, we recommend making a snapshot with [Local Backup](#11-local-backup).

---

## 13. Launcher updates (CHECK LAUNCHER UPDATE)

The launcher can update its own APK in the app. A dialog tells you when a new version is out, and one tap on OK downloads it, installs it and restarts the app.

### Automatic check (once at startup)

When the launcher reaches the launch stage after boot, it automatically compares against GitHub `releases/latest`. If there is a newer version, a dialog appears right away. If you are up to date, nothing is shown.

  <img src="images/24.jpg" width="600">
  <img src="images/25.jpg" width="600">

### CHECK LAUNCHER UPDATE button (manual)

The **CHECK LAUNCHER UPDATE** button above PLAY lets you check at any time. What happens depends on the result:

| Result | What happens |
|---|---|
| **New version available** | A dialog says "Launcher v0.X.Y is available." and asks "Download and install now?" On OK it downloads, then installs |
| **Already up to date** | A dialog says "You're already on the latest launcher version." and asks "Open the GitHub releases page anyway?" |
| **Check failed** (network, etc.) | A dialog says "Failed to check for launcher updates." followed by the error message |

  <img src="images/26.jpg" width="600">

### The permission flow on first use

The first time you try to download, Android (8 and later) asks you to allow **installing apps from this source** (REQUEST_INSTALL_PACKAGES):

1. Tap OK in the launcher dialog. You are taken to the system settings screen.
2. Turn on **Allow from this source**, then press Back.
3. Back in the launcher, tap CHECK LAUNCHER UPDATE again. This time a download progress dialog appears.
4. When the download finishes, the Android install screen opens. Tap **Update** and the app restarts.

> The update is signed with the same key, so your saves, Steam sign-in and the downloaded game (~3 GB) are kept.
>
> If a release has no APK attached, the launcher falls back to opening the GitHub releases page in your browser.

---

## 14. Debug logs (Debug toggle)

When something goes wrong, the launcher can capture the logcat output you need for diagnosis to shared storage. It is controlled by the **Debug: ON / OFF** toggle next to Console.

  <img src="images/27.jpg" width="600">

### How it works

- **ON by default**: it is on from the first boot, so even a crash right at startup leaves a log.
- It captures only its own process (`logcat --pid <self>`), so no extra permission is needed.
- **Starts automatically at boot**: capture begins in `onCreate`, so it includes the .NET, FMOD and BCL startup logs.
- Every line is flushed immediately, so the log survives a force-close or crash.
- To keep storage from growing forever, **at most 20 files** are kept and the oldest are deleted first.

### Where logs are saved

```
/storage/emulated/0/StS2LauncherRedux/Logs/
  ├── launcher_<YYYYMMDD>_<HHMMSS>.log  (a new file every launch)
  └── ...
```

  <img src="images/28.jpg" width="600">

### Reporting a problem

1. Open the path above with an external file manager (ZArchiver recommended).
2. Find the **launcher_*.log** from when the problem happened (usually the newest) and move it to your PC or send it through a messenger.
3. Attach it to an issue: [maldis018/StS2-Launcher_Mod_Manager/issues](https://github.com/maldis018/StS2-Launcher_Mod_Manager/issues)

### Turning Debug OFF

If you'd rather not capture logs (storage or privacy), turn the toggle OFF. The setting is saved in SharedPreferences and applies from the next launch; the capture in progress stops immediately. If you turn it back ON, a new file starts from that moment. Because the app has already booted, the logs from right after boot are missing; to get a full launch log, turn it ON and restart the app.

---

## 15. FAQ

### Q. Can I sync if my PC and phone are on different branches?

It depends.

- **Two branches with the same save schema** (for example, only a minor update apart): cloud sync works as is.
- **Branches with a schema change** (for example PC on public and mobile on public-beta, where beta added new fields): Steam Cloud may show a "sync conflict" dialog and recovery can get difficult.

**The safest procedure for switching between devices** (verified once):
1. In PC Steam, open StS2 properties, go to the Betas tab and choose **the same branch as the phone**.
2. Run StS2 on the PC once, get to the main menu, then quit. This pushes the PC's saves to the cloud in the new branch's schema.
3. On the phone, tap PLAY. If the Save Manager dialog correctly recognizes the new data in the cloud, take it with Keep Cloud or the automatic flow.

This procedure is **not always needed**: if the branches differ only slightly, sync just works. It is the standard recovery path when a conflict does occur.

### Q. I pushed bad data to the cloud. Can I recover it?

On PC, the [shrederr/sts2-progress-rebuild](https://github.com/shrederr/sts2-progress-rebuild) tool rebuilds `progress.save` from your `.run` history and restores it to the cloud.

If only the phone's local data is wrong (the cloud is fine), choose **Keep Cloud** in the Save Manager to recover.

If you have Local Backup snapshots, restore one from **SAVE MANAGER > Restore Backup**. In particular, every conflict saves **the side you discarded** as its own snapshot (listed as **Auto · Conflict (discarded)**, stored under `auto/<ts>_conflict/discarded/`), so you can undo a wrong Keep Local or Keep Cloud choice. (See [Section 11](#11-local-backup).)
  <img src="images/23.jpg" width="600">

### Q. The Save Manager says "conflict" every time.

In some cases PC and mobile each normalize progress.save slightly differently: the stats are identical but the size differs by a few KB each time. The destructive-change guard added in StS2 Launcher Mod 0.3.2 keeps this safe, but you still see the dialog once in a while. It is being tracked separately.

When it happens, choose **Keep Local** or **Keep Cloud**, whichever is visibly newer (the device you just played on).

### Q. Cloud upload seems to take a while after I quit the game.

That's normal. When you choose menu > Quit, the launcher waits until the cloud queue is fully empty (up to 5 minutes, usually 1 to 5 seconds). The process stays alive in the background during this time and you may see Steam network traffic.

### Q. If I force-close the phone by swiping the app away, do I lose data?

Not since StS2 Launcher Mod 0.3.2: it recovers automatically. Even if the cloud queue wasn't empty when you swiped, the next boot's `SaveProgressComparer` detects that the local side is newer and pushes it to the cloud.

Quitting normally with **menu > Quit** is still the safest.

### Q. How do I download the game again?

Choose a different branch in CHECK GAME UPDATE and it re-downloads in full automatically. To re-download the same branch, switch to another branch and then back to the original one.

---

## 16. Known issues and unsupported devices

| Issue | Status |
|---|---|
| The launcher fails to start on some tablets (Lenovo Y700/Y704 and others) | [Issue #6](https://github.com/iunius612/StS2-Launcher_Mod_Manager/issues/6): collecting device logs |
| The PC and phone `progress.save` sizes diverge (a conflict dialog shows once per device switch) | Tracked separately. The destructive impact was blocked in StS2 Launcher Mod 0.3.2 |

If you find a new problem, please open an [issue](https://github.com/maldis018/StS2-Launcher_Mod_Manager/issues). Attaching a log captured with `adb logcat -d > sts2.log` helps a lot.

---

## Useful adb commands (optional)

If you have your phone connected to a PC with USB debugging:

```bash
# View the launcher logcat
adb logcat -s DOTNET:I STS2Mobile:V godot:V

# List the mod and save folders in shared storage
adb shell ls -la /storage/emulated/0/StS2LauncherRedux/Mods/
adb shell ls -la /storage/emulated/0/StS2LauncherRedux/Saves/

# Full reset (wipe all data and start like a new user)
adb shell pm clear dev.aldis.sts2launcherredux
adb shell rm -rf /storage/emulated/0/StS2LauncherRedux
```

`adb shell pm clear` is blocked on some phones (OnePlus/ColorOS, for example). If it fails, uninstall and reinstall the app instead. This only affects Redux: it never touches the old StS2 Launcher Mod folder (`StS2LauncherMM`), and you shouldn't delete that folder yourself.

---

*This document is part of [maldis018/StS2-Launcher_Mod_Manager](https://github.com/maldis018/StS2-Launcher_Mod_Manager). If you spot wrong information or want something added, let us know with a PR or an issue.*
