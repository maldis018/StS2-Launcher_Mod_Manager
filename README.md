# StS2 Launcher Redux

**English** | [한국어](README.ko.md)

Play **Slay the Spire 2** on Android, with mods. StS2 Launcher Redux signs in to Steam, downloads the game you own straight from Steam, and runs it on a custom Godot 4.5.1 engine build, adapting the desktop game for touch screens with Harmony runtime patches. It also manages your mods (including the Steam Workshop), Steam Cloud saves and local backups.

**Latest release: [v0.5.0](https://github.com/maldis018/sts2-launcher-redux/releases/latest)**

> StS2 Launcher Redux is an unofficial fan project. It is not affiliated with, endorsed by, or supported by Mega Crit Games, the developer and publisher of Slay the Spire 2. You need a Steam account that owns the game; no game files are included in this repository or in the app.

## Install

You need:

- An arm64 Android phone or tablet running Android 7.0 or later
- A Steam account that owns Slay the Spire 2
- About 3 GB of free space for the game

Steps:

1. Download the latest `StS2LauncherRedux-vX.Y.Z.apk` from the [Releases page](https://github.com/maldis018/sts2-launcher-redux/releases/latest) and install it (allow installs from your browser or file manager when Android asks).
2. Open **StS2 Launcher Redux** and allow **All files access** when asked. Mods, backups and logs live in a shared folder, so the launcher needs it.
3. Sign in by scanning the on-screen QR code with the Steam app on another phone or tablet, so the launcher never sees your password. With only one phone, sign in on this phone with your password and approve the request in the Steam app (Steam Guard codes also work). The password is never stored, and the sign-in token is stored encrypted with the Android Keystore.
4. Pick a Steam branch (normally `public`), download the game, then tap **PLAY**.

To update, tap **CHECK LAUNCHER UPDATE** in the app (it also checks on start), or install a newer APK over the old one. Updates keep your sign-in, saves and downloaded game.

## Coming from StS2 Launcher Mod

Redux is a separate app (package `dev.aldis.sts2launcherredux`, folder `/storage/emulated/0/StS2LauncherRedux/`), so it installs next to StS2 Launcher Mod and Ekyso's StS2 Launcher instead of replacing them, and none of them share data.

Android doesn't let one app read another app's private data, so these stay with the old app:

- your Steam sign-in: sign in again in Redux
- the downloaded game (~3 GB): download it again in Redux
- your live saves: they come back from Steam Cloud. If you didn't use Steam Cloud, tap **Local Backup** in the old app before you switch.

Everything in the old shared folder can come over. On first launch (once All files access is granted), Redux offers to copy your mods, disabled mods, `mod_config.json`, Local Backup sets and LAN config from `/storage/emulated/0/StS2LauncherMM/`. It only copies: the old folder is never changed, and nothing you already have in the new folder is overwritten. Logs are left behind. To run it again later, open **MOD MANAGER > LOCAL > Import from StS2LauncherMM...**.

## What it does

**Steam and the game**
- Steam sign-in through SteamKit2: password-free QR sign-in from another device, or password plus Steam app approval or Steam Guard code on one phone. No PC, Steam client or Steamworks SDK needed.
- Game download straight from Steam's depots, with update checks and a branch picker (`public`, `public-beta`, ...). Switching branches does a clean re-download.

**Mods**
- Mod loading that keeps working as the game updates. The game's own mod scanner is redirected to the shared `Mods` folder.
- An in-app **Mod Hub**: browse, search and subscribe on the Steam Workshop, with automatic download, updates and dependency prompts.
- Manual installs, `.zip` import, and **DISABLE** to hide a mod from the game without deleting it.

**Saves**
- Full Steam Cloud sync. When local and cloud differ, you see them side by side (including a run in progress) and choose which one to keep, instead of a silent timestamp guess. This happens before **PLAY**, or any time from **SAVE MANAGER**.
- Guards against the cloud being overwritten with empty or fresh saves.
- **Local Backup**: manual snapshots (kept until you delete them) plus an automatic snapshot before every cloud sync, so a wrong choice can be undone.

**On your phone**
- Touch controls and UI scaling, rotation, foldables (fold/unfold), split screen and ultra-wide screens.
- Shader warmup and a persistent Vulkan pipeline cache to cut first-time stutters.
- LAN multiplayer with a PC (see [LAN multiplayer](#lan-multiplayer)).
- A **Debug** toggle that saves logs to the shared folder, so you can attach them to a bug report without adb.

## Mods

**From the Steam Workshop (easiest):** tap **MOD MANAGER** on the launch screen, find a mod on the **WORKSHOP** tab and subscribe. Downloads and updates happen automatically, and unsubscribing removes the mod.

**By hand:** put each mod in its own folder under `/storage/emulated/0/StS2LauncherRedux/Mods/`. A mod folder holds the mod's `.dll`, an optional `.pck` and a `<ModId>.json` manifest, the same layout PC players use in `Steam\steamapps\common\Slay the Spire 2\mods\`. Some phone file managers (Samsung's My Files, for one) can't see this folder; [ZArchiver](https://play.google.com/store/apps/details?id=ru.zdevs.zarchiver) can. You can also use **Import Mod (.zip)...** in **MOD MANAGER > LOCAL**.

![Mods folder layout](docs/images/mods_folder.jpg)

Tap **PLAY** and accept the game's "Load mods?" prompt. The game restarts once through the launcher with mods loaded. If a mod doesn't show up, turn on **Debug** and look for `[Mods]` lines in the log.

**Known limits:**
- **BaseLib** and the mods built on it load and run, but BaseLib's async hooks (`AfterCardPlayed` and the like) are switched off on Android because they corrupt memory under Mono. Content added through BaseLib's other APIs works; triggers that rely on those hooks never fire.
- Mods that call **Steamworks.NET** directly (QuickReload, for example) crash on **PLAY**: Valve doesn't ship the Steamworks library for Android. Some Steamworks features could be bridged through SteamKit2 for a specific mod, so open an issue if one matters to you. P2P networking, the overlay and Steam Input can't be.

## LAN multiplayer

Put the phone and the PC on the same local network. The phone finds games on the network by itself, or you can type the PC's IP address.

On the PC, add `--fastmp` to the game's launch options (**Steam > Slay the Spire 2 > Properties > Launch Options**). Keep both on the **same Steam branch**: cloud saves from one branch can't be read on another, and Steam only shows a generic sync conflict.

## Your files

Everything you might want to see or back up lives in `/storage/emulated/0/StS2LauncherRedux/`:

| Folder | What's in it |
|---|---|
| `Mods/` | Installed mods, plus `mod_config.json` (load order, enabled state, Workshop origin) |
| `ModsDisabled/` | Mods hidden with **DISABLE** |
| `Saves/manual/`, `Saves/auto/` | Local Backup snapshots, one folder per backup, named by date and time |
| `Config/` | LAN settings (`lan_client_id.cfg`) |
| `Logs/` | Debug logs, rotated automatically |

The game itself, your sign-in and your live saves are in the app's private storage, so uninstalling the app removes them. The shared folder stays.

## Help and bug reports

A step-by-step guide with screenshots is in [docs/USER_GUIDE.md](docs/USER_GUIDE.md) (Korean: [docs/USER_GUIDE.ko.md](docs/USER_GUIDE.ko.md)). Some screenshots still show the older StS2 Launcher Mod UI.

To report a bug, turn on **Debug**, reproduce the problem, and attach the newest file from `StS2LauncherRedux/Logs/` to an [issue](https://github.com/maldis018/sts2-launcher-redux/issues).

## Project history

Redux is the third project in a line of forks:

| Project | Repository | Active | What it brought |
|---|---|---|---|
| StS2 Launcher | [Ekyso/StS2-Launcher](https://github.com/Ekyso/StS2-Launcher) | March 2026 (v0.1.0, v0.2.0) | The original port: the custom Godot engine build, Harmony runtime patching ([Ekyso/Harmony](https://github.com/Ekyso/Harmony)), Steam sign-in and game download |
| StS2 Launcher Mod | [iunius612/StS2-Launcher_Mod_Manager](https://github.com/iunius612/StS2-Launcher_Mod_Manager) | April to August 2026 (v0.2.1 to v0.4.2) | Mod loading for current game builds, the Workshop Mod Hub, save safety (conflict choice, write guards, Local Backup), launcher self-update, foldable and mobile UX work, LAN fixes |
| StS2 Launcher Redux | [maldis018/sts2-launcher-redux](https://github.com/maldis018/sts2-launcher-redux) | November 2026 on (v0.5.0+) | Forked from StS2 Launcher Mod v0.4.2 and maintained separately: its own app identity and folder, import from StS2 Launcher Mod, English text for Korean-only messages, automated release builds |

The engine port and the mod manager are the work of Ekyso and iunius612. Release notes up to v0.4.2 (in Korean, with technical detail) are on [StS2 Launcher Mod's Releases page](https://github.com/iunius612/StS2-Launcher_Mod_Manager/releases); Redux releases are on [this repo's Releases page](https://github.com/maldis018/sts2-launcher-redux/releases).

## Building from source

### How it works

At startup the engine loads `STS2Mobile.dll` (this repo's C# project, `src/STS2Mobile/`) through `coreclr_create_delegate`. It applies Harmony patches that adapt the desktop game to mobile, and intercepts `GameStartupWrapper()` to show the launcher before the game starts.

- **Launcher-only mode:** with no game files yet, a minimal `bootstrap.pck` (just a `project.godot`) brings up the .NET runtime and the launcher UI for sign-in and download.
- **Normal mode:** once the game is downloaded, the patches apply against the game's `sts2.dll` and the game runs natively.

The custom engine adds a persistent Vulkan pipeline cache (saved when the app loses focus, so Android killing the process doesn't force recompiles) and canvas ubershaders (no first-time 2D effect stutters). Native stubs in `src/stubs/` stand in for desktop-only libraries (Steamworks, Sentry) so the linker is satisfied.

### Layout

```
src/STS2Mobile/            C# patcher and launcher
  ModEntry.cs              entry point ([UnmanagedCallersOnly] Apply())
  Patches/                 Harmony patches, one file per concern
  Launcher/                launcher UI built in code, incl. the Mod Hub
  Steam/                   SteamKit2 sign-in, depot download, cloud saves, Workshop
  Modding/                 mod scanning, import, Workshop install
src/stubs/                 native library stubs
android/                   Godot Android Gradle project (GodotApp.java: activity,
                           Keystore encryption, storage, log capture)
art/icon/                  icon sources and the Android icon renderer
scripts/ci/                build input fetch and APK build, used by CI and locally
tools/memberref-audit/     checks the IL patches against a new game build
docs/                      user guide (English and Korean) and images
```

### Build an APK

You need the .NET 9 SDK, JDK 17, the Android SDK (platform 35, build-tools 35.0.0), Python 3, and your own copy of the game's `sts2.dll` from a Steam install (Windows build, `data_sts2_windows_x86_64/sts2.dll`). It is only a compile-time reference and is never packaged.

```bash
# 1. Fetch the binary build inputs that aren't in git (engine AAR, natives,
#    BCL, FMOD, crypto JAR). All come from pinned public downloads.
STS2_DLL=/path/to/sts2.dll bash scripts/ci/fetch-build-inputs.sh

# 2. Point Gradle at your Android SDK.
echo "sdk.dir=$ANDROID_HOME" > android/local.properties

# 3. Build and sign. Any keystore works for a test build; a debug key can't
#    update an install signed with the release key (uninstall first).
RELEASE_KEYSTORE_FILE=~/.android/debug.keystore \
RELEASE_KEYSTORE_ALIAS=androiddebugkey \
RELEASE_KEYSTORE_PASSWORD=android \
    bash scripts/ci/build-apk.sh

adb install -r android/build/outputs/apk/mono/release/StS2LauncherRedux-v*.apk
```

`scripts/build.sh` and `scripts/setup-deps.sh` are the older local scripts. `build.sh` also bumps the version and runs csharpier; `setup-deps.sh` takes FMOD from Ekyso's v0.2.0 APK, which is FMOD 2.02 and too old for current mods (see `android/libs/README.md`). Prefer the `scripts/ci/` scripts. Other one-off tasks:

```bash
python3 scripts/make-bootstrap-pck.py   # regenerate bootstrap.pck
bash scripts/build-godot.sh             # rebuild the custom engine
bash src/stubs/build_stubs.sh           # rebuild native stubs (needs the NDK)
uv run --with cairosvg python art/icon/render_android_icons.py   # re-render icons
```

### Releases

1. Set `export_version_name` and `export_version_code` in `android/gradle.properties`. The code is major×10000 + minor×100 + patch (0.5.0 is 500) and must only go up.
2. Commit, tag `vX.Y.Z` (it must match `export_version_name`) and push the tag.
3. `.github/workflows/release.yml` builds and signs the APK and attaches it, with its SHA-256, to a **draft** release. The in-app update check only sees a release once the draft is published.

## License

[MIT](LICENSE). Third-party licenses are listed in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md). FMOD and Spine are proprietary and can't be redistributed from this repository: FMOD needs a commercial license if a project makes money, and the Spine Runtimes need a Spine Editor license.
