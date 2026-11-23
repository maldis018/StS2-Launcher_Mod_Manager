# StS2 Launcher Redux

[English](README.md) | **한국어**

안드로이드에서 모드와 함께 **Slay the Spire 2** 를 플레이하세요. StS2 Launcher Redux 는 Steam 에 로그인해 보유한 게임을 Steam 에서 직접 받고, 커스텀 Godot 4.5.1 엔진 빌드 위에서 실행하며, Harmony 런타임 패치로 데스크톱 게임을 터치 화면에 맞게 바꿉니다. 모드(Steam 창작마당 포함), Steam 클라우드 세이브, 로컬 백업도 관리합니다.

**최신 릴리즈: [v0.5.0](https://github.com/maldis018/StS2-Launcher_Mod_Manager/releases/latest)**

> StS2 Launcher Redux 는 비공식 팬 프로젝트이며, Slay the Spire 2 의 개발·배급사인 Mega Crit Games 와 제휴하거나 보증·지원을 받지 않습니다. 게임을 보유한 Steam 계정이 필요하며, 이 저장소와 앱에는 게임 파일이 포함되어 있지 않습니다.

## 설치

필요한 것:

- Android 7.0 이상의 arm64 휴대폰 또는 태블릿
- Slay the Spire 2 를 보유한 Steam 계정
- 게임용 여유 공간 약 3GB

순서:

1. [Releases 페이지](https://github.com/maldis018/StS2-Launcher_Mod_Manager/releases/latest)에서 최신 `StS2LauncherRedux-vX.Y.Z.apk` 를 받아 설치합니다 (안드로이드가 물으면 브라우저나 파일 매니저의 설치를 허용하세요).
2. **StS2 Launcher Redux** 를 열고 **모든 파일 액세스** 를 허용합니다. 모드·백업·로그가 공유 폴더에 있어서 필요합니다.
3. Steam 계정으로 로그인합니다. Steam Guard 코드를 지원하며, 로그인 토큰은 Android Keystore 로 암호화해 저장합니다.
4. Steam 브랜치(보통 `public`)를 고르고 게임을 받은 뒤 **PLAY** 를 누릅니다.

업데이트는 앱의 **CHECK LAUNCHER UPDATE** 로 합니다 (시작할 때도 확인합니다). 새 APK 를 덮어 설치해도 됩니다. 업데이트해도 로그인, 세이브, 받은 게임은 유지됩니다.

## StS2 Launcher Mod 에서 옮겨오기

Redux 는 별도 앱(패키지 `dev.aldis.sts2launcherredux`, 폴더 `/storage/emulated/0/StS2LauncherRedux/`)이라 StS2 Launcher Mod 나 Ekyso 의 StS2 Launcher 를 대체하지 않고 나란히 설치되며, 서로 데이터를 공유하지 않습니다.

안드로이드는 다른 앱의 내부 데이터를 읽지 못하게 하므로 다음은 이전 앱에 남습니다:

- Steam 로그인: Redux 에서 다시 로그인하세요
- 받은 게임(약 3GB): Redux 에서 다시 받으세요
- 현재 세이브: Steam 클라우드에서 돌아옵니다. Steam 클라우드를 쓰지 않았다면 옮기기 전에 이전 앱에서 **Local Backup** 을 누르세요.

이전 공유 폴더의 내용은 모두 가져올 수 있습니다. 첫 실행 시 (모든 파일 액세스 허용 후) Redux 가 `/storage/emulated/0/StS2LauncherMM/` 의 모드, 비활성 모드, `mod_config.json`, Local Backup, LAN 설정을 복사할지 묻습니다. 복사만 하며 이전 폴더는 바꾸지 않고, 새 폴더에 이미 있는 내 항목은 덮어쓰지 않습니다. 로그는 가져오지 않습니다. 나중에 다시 실행하려면 **MOD MANAGER > LOCAL > Import from StS2LauncherMM...** 을 여세요.

## 기능

**Steam 과 게임**
- SteamKit2 기반 Steam 로그인 (Steam Guard 지원). PC, Steam 클라이언트, Steamworks SDK 가 필요 없습니다.
- Steam depot 에서 직접 게임 다운로드, 업데이트 확인, 브랜치 선택 (`public`, `public-beta`, ...). 브랜치를 바꾸면 깨끗하게 다시 받습니다.

**모드**
- 게임이 업데이트되어도 계속 동작하는 모드 로딩. 게임 자체 모드 스캐너를 공유 `Mods` 폴더로 돌립니다.
- 앱 안의 **Mod Hub**: Steam 창작마당 검색·구독, 자동 다운로드·업데이트·의존성 안내.
- 수동 설치, `.zip` 가져오기, 그리고 모드를 지우지 않고 게임에서만 숨기는 **DISABLE**.

**세이브**
- 전체 Steam 클라우드 동기화. 로컬과 클라우드가 다르면 (진행 중인 런 포함) 나란히 보여 주고 어느 쪽을 남길지 직접 고르게 합니다. 시간 기준으로 몰래 고르지 않습니다. **PLAY** 전에, 또는 언제든 **SAVE MANAGER** 에서 합니다.
- 클라우드가 빈 세이브나 새 세이브로 덮어써지는 것을 막는 보호 장치.
- **Local Backup**: 수동 스냅샷(직접 지우기 전까지 보관)과 클라우드 동기화 전마다 찍는 자동 스냅샷으로 잘못 고른 선택도 되돌릴 수 있습니다.

**휴대폰에서**
- 터치 조작과 UI 배율, 화면 회전, 폴더블(접기/펴기), 분할 화면, 울트라와이드 화면.
- 셰이더 워밍업과 Vulkan 파이프라인 캐시 보존으로 첫 등장 끊김 감소.
- PC 와 LAN 멀티플레이 ([LAN 멀티플레이](#lan-멀티플레이) 참조).
- 로그를 공유 폴더에 저장하는 **Debug** 토글. adb 없이 버그 제보에 로그를 첨부할 수 있습니다.

## 모드

**Steam 창작마당 (가장 쉬움):** 실행 화면에서 **MOD MANAGER** 를 누르고 **WORKSHOP** 탭에서 모드를 찾아 구독합니다. 다운로드와 업데이트는 자동이며, 구독을 취소하면 모드가 삭제됩니다.

**직접 설치:** 모드마다 폴더 하나씩 `/storage/emulated/0/StS2LauncherRedux/Mods/` 아래에 넣습니다. 모드 폴더에는 모드의 `.dll`, 선택적 `.pck`, `<ModId>.json` 매니페스트가 들어 있으며, PC 의 `Steam\steamapps\common\Slay the Spire 2\mods\` 와 같은 구조입니다. 일부 파일 매니저(삼성 내 파일 등)는 이 폴더를 보지 못하니 [ZArchiver](https://play.google.com/store/apps/details?id=ru.zdevs.zarchiver) 를 쓰세요. **MOD MANAGER > LOCAL** 의 **Import Mod (.zip)...** 도 쓸 수 있습니다.

![Mods 폴더 구조](docs/images/mods_folder.jpg)

**PLAY** 를 누르고 게임의 "Load mods?" 질문을 수락하면 게임이 런처를 거쳐 한 번 재시작하며 모드가 로드됩니다. 모드가 보이지 않으면 **Debug** 를 켜고 로그의 `[Mods]` 줄을 확인하세요.

**알려진 제한:**
- **BaseLib** 와 그 위에 만든 모드는 로드·실행되지만, BaseLib 의 비동기 훅(`AfterCardPlayed` 등)은 Mono 에서 메모리를 손상시키므로 안드로이드에서는 꺼져 있습니다. BaseLib 의 다른 API 로 추가한 콘텐츠는 동작하고, 그 훅에 의존하는 트리거만 발동하지 않습니다.
- **Steamworks.NET** 을 직접 호출하는 모드(예: QuickReload)는 **PLAY** 시 크래시합니다. Valve 가 안드로이드용 Steamworks 라이브러리를 제공하지 않기 때문입니다. 일부 Steamworks 기능은 특정 모드를 위해 SteamKit2 로 연결할 수 있으니 필요하면 이슈를 열어 주세요. P2P 네트워킹, 오버레이, Steam Input 은 불가능합니다.

## LAN 멀티플레이

휴대폰과 PC 를 같은 로컬 네트워크에 연결합니다. 휴대폰이 네트워크의 게임을 자동으로 찾으며, PC 의 IP 주소를 직접 입력할 수도 있습니다.

PC 에서는 게임 실행 옵션(**Steam > Slay the Spire 2 > 속성 > 실행 옵션**)에 `--fastmp` 를 추가합니다. 두 기기 모두 **같은 Steam 브랜치** 를 쓰세요. 한 브랜치의 클라우드 세이브는 다른 브랜치에서 읽을 수 없고, Steam 은 일반적인 동기화 충돌만 표시합니다.

## 내 파일

직접 보거나 백업할 만한 것은 모두 `/storage/emulated/0/StS2LauncherRedux/` 에 있습니다:

| 폴더 | 내용 |
|---|---|
| `Mods/` | 설치된 모드와 `mod_config.json` (로드 순서, 활성 상태, 창작마당 출처) |
| `ModsDisabled/` | **DISABLE** 로 숨긴 모드 |
| `Saves/manual/`, `Saves/auto/` | Local Backup 스냅샷 (백업마다 날짜·시각 이름의 폴더 하나) |
| `Config/` | LAN 설정 (`lan_client_id.cfg`) |
| `Logs/` | 디버그 로그 (자동 정리) |

게임 본체, 로그인, 현재 세이브는 앱 내부 저장소에 있어서 앱을 삭제하면 함께 지워집니다. 공유 폴더는 남습니다.

## 도움말과 버그 제보

스크린샷이 포함된 단계별 안내는 [docs/USER_GUIDE.md](docs/USER_GUIDE.md) 에 있습니다 (영어판: [docs/USER_GUIDE.en.md](docs/USER_GUIDE.en.md)) (StS2 Launcher Mod 기준으로 쓰였지만 거의 전부 Redux 에도 해당합니다).

버그를 제보하려면 **Debug** 를 켜고 문제를 재현한 뒤 `StS2LauncherRedux/Logs/` 의 최신 파일을 [이슈](https://github.com/maldis018/StS2-Launcher_Mod_Manager/issues)에 첨부해 주세요.

## 프로젝트 계보

Redux 는 포크 계보의 세 번째 프로젝트입니다:

| 프로젝트 | 저장소 | 활동 시기 | 기여 |
|---|---|---|---|
| StS2 Launcher | [Ekyso/StS2-Launcher](https://github.com/Ekyso/StS2-Launcher) | 2026년 3월 (v0.1.0, v0.2.0) | 원조 포팅: 커스텀 Godot 엔진 빌드, Harmony 런타임 패칭 ([Ekyso/Harmony](https://github.com/Ekyso/Harmony)), Steam 로그인과 게임 다운로드 |
| StS2 Launcher Mod | [iunius612/StS2-Launcher_Mod_Manager](https://github.com/iunius612/StS2-Launcher_Mod_Manager) | 2026년 4월 ~ 8월 (v0.2.1 ~ v0.4.2) | 최신 게임 빌드용 모드 로딩, 창작마당 Mod Hub, 세이브 안전장치(충돌 선택, 쓰기 가드, Local Backup), 런처 자체 업데이트, 폴더블·모바일 UX, LAN 수정 |
| StS2 Launcher Redux | [maldis018/StS2-Launcher_Mod_Manager](https://github.com/maldis018/StS2-Launcher_Mod_Manager) | 2026년 11월 ~ (v0.5.0+) | StS2 Launcher Mod v0.4.2 에서 분기해 별도로 유지보수: 독자적인 앱 아이덴티티와 폴더, StS2 Launcher Mod 에서 가져오기, 한국어 전용 메시지의 영어 번역, 자동 릴리즈 빌드 |

엔진 포팅과 모드 매니저는 Ekyso 와 iunius612 의 작업입니다. v0.4.2 까지의 릴리즈 노트(기술 노트 포함)는 [StS2 Launcher Mod 의 Releases 페이지](https://github.com/iunius612/StS2-Launcher_Mod_Manager/releases)에, Redux 릴리즈는 [이 저장소의 Releases 페이지](https://github.com/maldis018/StS2-Launcher_Mod_Manager/releases)에 있습니다.

## 소스에서 빌드

### 동작 방식

시작 시 엔진이 `coreclr_create_delegate` 로 `STS2Mobile.dll` (이 저장소의 C# 프로젝트, `src/STS2Mobile/`)을 로드합니다. 이 DLL 은 데스크톱 게임을 모바일에 맞추는 Harmony 패치를 적용하고, `GameStartupWrapper()` 를 가로채 게임 시작 전에 런처를 띄웁니다.

- **런처 전용 모드:** 아직 게임 파일이 없을 때는 최소 `bootstrap.pck` (`project.godot` 만 포함)가 .NET 런타임과 로그인·다운로드용 런처 UI 를 띄웁니다.
- **일반 모드:** 게임을 받은 뒤에는 패치가 게임의 `sts2.dll` 에 적용되고 게임이 네이티브로 실행됩니다.

커스텀 엔진은 Vulkan 파이프라인 캐시 보존(앱이 포커스를 잃을 때 저장해, 안드로이드가 프로세스를 종료해도 다시 컴파일하지 않음)과 캔버스 우버셰이더(2D 이펙트 첫 등장 끊김 제거)를 추가합니다. `src/stubs/` 의 네이티브 스텁은 데스크톱 전용 라이브러리(Steamworks, Sentry)를 대신해 링커를 만족시킵니다.

### 구조

```
src/STS2Mobile/            C# 패처와 런처
  ModEntry.cs              진입점 ([UnmanagedCallersOnly] Apply())
  Patches/                 Harmony 패치 (관심사별 파일 하나)
  Launcher/                코드로 만든 런처 UI (Mod Hub 포함)
  Steam/                   SteamKit2 로그인, depot 다운로드, 클라우드 세이브, 창작마당
  Modding/                 모드 스캔, 가져오기, 창작마당 설치
src/stubs/                 네이티브 라이브러리 스텁
android/                   Godot 안드로이드 Gradle 프로젝트 (GodotApp.java: 액티비티,
                           Keystore 암호화, 저장소, 로그 캡처)
art/icon/                  아이콘 원본과 안드로이드 아이콘 렌더러
scripts/ci/                빌드 입력 수집과 APK 빌드 (CI 와 로컬 공용)
tools/memberref-audit/     새 게임 빌드에 대해 IL 패치를 검사
docs/                      사용설명서와 이미지
```

### APK 빌드

.NET 9 SDK, JDK 17, Android SDK (platform 35, build-tools 35.0.0), Python 3, 그리고 Steam 설치본에서 가져온 게임의 `sts2.dll` (Windows 빌드, `data_sts2_windows_x86_64/sts2.dll`)이 필요합니다. `sts2.dll` 은 컴파일 시 참조용일 뿐 APK 에 들어가지 않습니다.

```bash
# 1. git 에 없는 바이너리 빌드 입력(엔진 AAR, 네이티브, BCL, FMOD, crypto JAR)을
#    고정된 공개 다운로드에서 가져옵니다.
STS2_DLL=/path/to/sts2.dll bash scripts/ci/fetch-build-inputs.sh

# 2. Gradle 에 Android SDK 위치를 알려 줍니다.
echo "sdk.dir=$ANDROID_HOME" > android/local.properties

# 3. 빌드와 서명. 테스트 빌드는 아무 키스토어나 됩니다. 디버그 키로 서명한 APK 는
#    릴리즈 키로 설치된 앱을 업데이트할 수 없습니다 (먼저 삭제하세요).
RELEASE_KEYSTORE_FILE=~/.android/debug.keystore \
RELEASE_KEYSTORE_ALIAS=androiddebugkey \
RELEASE_KEYSTORE_PASSWORD=android \
    bash scripts/ci/build-apk.sh

adb install -r android/build/outputs/apk/mono/release/StS2LauncherRedux-v*.apk
```

`scripts/build.sh` 와 `scripts/setup-deps.sh` 는 예전 로컬 스크립트입니다. `build.sh` 는 버전을 올리고 csharpier 도 실행하며, `setup-deps.sh` 는 Ekyso v0.2.0 APK 의 FMOD 를 쓰는데 이는 FMOD 2.02 라 최신 모드에는 너무 오래되었습니다 (`android/libs/README.md` 참조). `scripts/ci/` 스크립트를 쓰세요. 그 밖의 작업:

```bash
python3 scripts/make-bootstrap-pck.py   # bootstrap.pck 재생성
bash scripts/build-godot.sh             # 커스텀 엔진 재빌드
bash src/stubs/build_stubs.sh           # 네이티브 스텁 재빌드 (NDK 필요)
uv run --with cairosvg python art/icon/render_android_icons.py   # 아이콘 다시 렌더링
```

### 릴리즈

1. `android/gradle.properties` 의 `export_version_name` 과 `export_version_code` 를 바꿉니다. 코드는 major×10000 + minor×100 + patch (0.5.0 은 500)이며 올라가기만 해야 합니다.
2. 커밋하고 `vX.Y.Z` 태그(`export_version_name` 과 같아야 함)를 만들어 푸시합니다.
3. `.github/workflows/release.yml` 이 APK 를 빌드·서명해 SHA-256 과 함께 **초안(draft)** 릴리즈에 첨부합니다. 앱의 업데이트 확인은 초안이 게시된 뒤에야 릴리즈를 봅니다.

## 라이선스

[MIT](LICENSE). 서드파티 라이선스는 [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) 에 있습니다. FMOD 와 Spine 은 독점 소프트웨어라 이 저장소에서 재배포할 수 없습니다. FMOD 는 수익이 나는 프로젝트에 상용 라이선스가 필요하고, Spine 런타임은 Spine Editor 라이선스가 필요합니다.
