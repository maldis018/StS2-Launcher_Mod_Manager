# Korean UI glossary

This is the single reference for the Korean launcher UI (`Loc.Tr("한국어", "English")` calls under `src/STS2Mobile/`), `README.ko.md` and `docs/USER_GUIDE.ko.md`. When a string is added or reworded, check it against this file first, and add the term here if it is new.

The Korean was written by a non-native speaker, using the wording of Android, Samsung One UI, the Steam Korean client and Korean game launchers as the reference. Rows marked **review** are judgement calls that a native speaker should confirm; the "Alternatives" column lists what was considered.

## Style rules

| Topic | Rule |
|---|---|
| Statements | 합니다체: "~입니다", "~했습니다", "~할 수 없습니다". |
| Instructions | "~하세요" / "~해 주세요" ("다시 시도해 주세요", "권한을 허용하세요"). |
| Confirmation questions | "~할까요?" everywhere ("백업할까요?"). Never "~하시겠습니까?" or "~하시겠어요?". |
| Status lines | Short "~하는 중..." or a noun phrase ("연결하는 중...", "확인 중..."). Ellipsis is three ASCII dots. |
| Buttons | Short noun or verb stem ("저장", "삭제", "구독", "백업"), not "~하기", except where the idiom needs it ("가져오기", "올리기", "받기", "나가기"). No all-caps styling and no decoration that only made sense for English caps. |
| Confirmation dialog buttons | Answer the question with a specific verb where one reads better ("가져오기" / "나중에", "나가기" / "계속 다운로드", "덮어쓰기" / "취소"). Default pair is 확인 / 취소. Never 예 / 아니오 for the launcher's own dialogs. |
| Toggle state | "켜짐" / "꺼짐" ("자동 동기화: 켜짐"). |
| Device | 기기, never 디바이스 ("이 기기"). |
| Numbers | Counters attach to the number: "3개", "2명", "5층". Byte sizes keep the Latin unit with no space: "9.9MB". Use `Loc.Size(bytes)`, which drops the space in Korean only. Times read "2시간 5분". |
| Spacing | Particles attach directly to the preceding word, including Latin words and numbers: "Steam에", "3개를", "Slay the Spire 2를". Choose the particle by how the word is read, not by its last letter (see below). Avoid "을(를)" fallbacks; reword if the particle is uncertain. |
| Proper nouns | Latin script, unchanged: Steam, Slay the Spire 2, StS2 Launcher Redux, StS2 Launcher Mod, BaseLib, Steam Guard, GitHub, mod IDs, file and folder names, `mod_config.json`, `StS2LauncherMM`, `ModsDisabled`. |
| Not localized | Log and console lines (`PatchHelper.Log`, `AppendLog`), exception text from Steam or the network (appended after a Korean prefix: "다운로드 실패: {message}"), file names, branch names (`public`, `public-beta`). |

### Particles after recurring Latin words

| Word | Read as | Final consonant | Particles |
|---|---|---|---|
| Steam | 스팀 | yes | 은 / 을 / 이 / 과 / 으로 (에, 에서, 의 are unaffected) |
| Steam 클라우드 | 클라우드 | no | 는 / 를 / 가 / 와 / 로 |
| Slay the Spire 2 | 투 | no | 는 / 를 / 가 / 와 / 로 |
| StS2 Launcher Redux | 리덕스 | no | 는 / 를 / 가 / 와 / 로 |
| StS2LauncherMM | 엠엠 | yes | 은 / 을 / 이 / 과 / 으로 |
| BaseLib | 베이스립 | yes | 은 / 을 / 이 / 과 / 으로 |
| GitHub | 깃허브 | no | 는 / 를 / 가 / 와 / 로 |

## Terms

### Shared

| English | Korean | Where | Reason, alternatives |
|---|---|---|---|
| OK / Cancel / Close | 확인 / 취소 / 닫기 | every dialog | Android standard. `LauncherView.ShowConfirmation` and `StyledDialog` both default to `Loc.Tr("확인", "OK")` / `Loc.Tr("취소", "Cancel")`. |
| Back | 뒤로 | Mod Hub top bar | Android standard. |
| Retry | 다시 시도 | RETRY | Android standard. |
| Refresh | 새로고침 | Mod Hub | Android and browser standard. |
| Done / Failed | 완료 / 실패 | result dialogs | |
| Yes / No | (not used) | | Replaced by verb pairs, see the style rules. |
| Device ("this device") | 기기 ("이 기기") | cloud conflict card, hints | Android settings say 기기. The old copy said 디바이스. |
| Local | 로컬 | tabs, badges, cards | Loanword in common use. Alternative: 기기 내. |
| Cloud | 클라우드 | | |
| Steam Cloud | Steam 클라우드 | cards, hints, docs | The Steam Korean client says "Steam 클라우드". The old copy said "Steam Cloud" with a Korean particle ("Steam Cloud의"). |
| Save (game save data) | 세이브 | everywhere | Project term, matches the docs. 저장 data is avoided because it also means "to save". |
| Profile | 프로필 | | Matches the game. |
| Debug | 디버그 | Debug toggle | |
| Console | 콘솔 | log panel header | |
| Mod | 모드 | | |
| Storage permission | 저장소 권한 | | Android's own wording is "저장 공간" in prose and "파일 및 미디어" for the permission; the buttons say "저장소 권한" for brevity. |
| All files access | 모든 파일 액세스 | permission prompt | Android system setting name. |

### Main screen

| English | Korean | Where | Reason, alternatives |
|---|---|---|---|
| LOGIN | 로그인 | LoginSection | Steam client wording. |
| SIGN IN ON THIS PHONE | 이 휴대폰에서 로그인 | QrLoginSection | Switches from the QR code to the password form. 휴대폰 matches the hint text; 기기 would also fit tablets but reads less naturally here. |
| NEW CODE | 새 코드 받기 | QrLoginSection | Shown when the QR code expired or was denied; matches the hint "새 코드를 받아 다시 시도하세요". |
| Steam Username | Steam 계정 이름 | login placeholder | Steam Korean client wording ("계정 이름"). |
| Password | 비밀번호 | login placeholder | Steam Korean client wording. |
| Enter Steam Guard code | Steam Guard 코드를 입력하세요 | CodeSection | |
| Code | 코드 | placeholder | |
| SUBMIT | 확인 | CodeSection | Alternative: 인증. 확인 is the usual label for a code field. |
| DOWNLOAD GAME FILES | 게임 파일 다운로드 | DownloadSection | |
| RETRY DOWNLOAD | 다운로드 다시 시도 | DownloadSection | |
| RESUME DOWNLOAD (n%) | 이어받기 (n%) | DownloadSection | 이어받기 is the usual Korean term for resuming a partial download. |
| CHOOSE BRANCH | 브랜치 선택 | DownloadSection | After a fatal failure on the saved branch. |
| PAUSE | 일시정지 | DownloadSection | Matches the status "다운로드 일시정지됨" and Android media controls. |
| UPDATE GAME FILES | 게임 파일 업데이트 | DownloadSection | |
| PLAY / LAUNCH | **게임 시작** | main button | **Decision, review.** Korean mobile game launchers and patchers (Nexon, Netmarble, Kakao Games) show "게임 시작" after the patch step, and this launcher is exactly that: download, then hand off to the game. It is also unambiguous for people who do not read "플레이" as a media Play button. Alternatives: 플레이 (Steam, Battle.net and Riot say this; 3 syllables, matches desktop habits), 시작 (too generic), 실행 (Epic). |
| RESTART APP | 앱 재시작 | main button | The old Korean copy already said "앱 재시작 필요". |
| App restart required | 앱 재시작 필요 | main button | Kept. |
| SAVE MANAGER | 세이브 관리 | main button, dialog title | **Review.** Alternatives: 세이브 관리자, 세이브 매니저. 관리 follows Android labels such as "앱 관리". |
| MOD MANAGER | 모드 관리 | main button | **Review.** Same reasoning. |
| Mod Hub | 모드 허브 | Mod Hub title, docs | **Review.** "허브" is a loanword; alternatives 모드 센터, or just reuse 모드 관리. |
| Local Backup | 로컬 백업 | main button, docs | **Review.** Keeps the loanword the docs already use. Alternatives: 기기에 백업, 지금 백업. |
| Auto Sync: ON / OFF | 자동 동기화: 켜짐 / 꺼짐 | main toggle | Samsung and Android toggles use 켜짐 / 꺼짐. |
| Push to Cloud | 클라우드에 올리기 | main button | **Review.** Alternatives: 클라우드로 업로드, 클라우드로 보내기. 올리기 is the usual verb for uploading to a cloud drive. |
| Pull from Cloud | 클라우드에서 받기 | main button | **Review.** Alternatives: 클라우드에서 다운로드, 클라우드에서 가져오기. Pairs with 올리기. |
| CHECK GAME UPDATE | 게임 업데이트 확인 | main button | |
| CHECK LAUNCHER UPDATE | 런처 업데이트 확인 | main button | |
| UP TO DATE | 최신 버전 | button state | Google Play wording. |
| CHECK FAILED | 확인 실패 | button state | |
| Checking... | 확인 중... | button state | |
| Loading branches... | 브랜치를 불러오는 중... / 불러오는 중... | download status / game update button | The button uses the short form so it fits. |
| v1.2.3 available | v1.2.3 업데이트 가능 | button state | |
| Debug: ON / OFF | 디버그: 켜짐 / 꺼짐 | Console header | |
| Update available! | 업데이트가 있습니다 | status | |
| Release (GitHub) | 릴리스 | update dialogs, docs | GitHub's Korean UI and the standard spelling. Never 릴리즈. |
| Branch | 브랜치 | | Steam Korean client: "베타" tab, but the launcher selects real Steam branches (`public`, `public-beta`), so 브랜치 is used, and 베타 only for "beta branches". |

### Dialogs

| English | Korean | Where | Reason, alternatives |
|---|---|---|---|
| Select game version | 게임 버전 선택 | branch picker | |
| Current | 현재 | branch pill | |
| Clear image cache | 이미지 캐시 삭제 | branch picker footer | **Review.** Alternative: 이미지 캐시 지우기. |
| Back up (save data) | 백업 | | Loanword. |
| Restore Backup | 백업 복원 | picker, docs | Already used. |
| Copy Profile | 프로필 복제 | picker, docs | Already used. |
| Keep Local / Keep Cloud | 로컬 유지 / 클라우드 유지 | conflict dialog | Already used and clear. |
| Newer (badge) | 최근 | conflict card | Already used. |
| Last modified | 마지막 수정 | conflict card | Was "파일 생성 시간", which means "creation time"; the value is the modified time. |
| Total playtime | 총 플레이 시간 | conflict card | Steam Korean client says "플레이 시간". |
| Leave / Stay (Mod Hub exit) | 나가기 / 계속 다운로드 | confirm | Verb pair that answers the question. |
| Import / Not now | 가져오기 / 나중에 | legacy import prompt | |
| Log in again / Continue offline | 다시 로그인 / 오프라인으로 계속 | token expired | Already used. |
| Overwrite | 덮어쓰기 | | Already used. |
| Mod Hub confirm buttons | 구독 취소 / 닫기, 삭제 / 취소, 덮어쓰기 / 취소, 교체 / 취소, 받기 / 나중에, 구독 / 취소 | Mod Hub confirmations | Passed through `ConfirmHandler`'s okLabel / cancelLabel. An unsubscribe prompt must not offer a bare 취소, which reads as "cancel the subscription". |

### Mod Hub

| English | Korean | Where | Reason, alternatives |
|---|---|---|---|
| Workshop | 창작마당 | tabs, hints | Steam Korean client wording. |
| WORKSHOP / SUBSCRIBED / LOCAL / DOWNLOADS (tabs) | 창작마당 / 구독 중 / 로컬 / 다운로드 | tab bar | Four tabs must fit a phone width; each is at most 4 syllables. |
| SEARCH | 검색 | | |
| TAGS | 태그 | | |
| LOAD MORE | 더 보기 | | |
| SUBSCRIBE / UNSUBSCRIBE | 구독 / 구독 취소 | cards, rows, detail | Steam Korean client wording. |
| DETAIL | 자세히 | cards, rows | 상세 alone reads like a cut-off noun on a button; 자세히 is the common Korean app label and is as short. The page it opens is still called 상세 페이지 in the docs. |
| DISABLE / ENABLE | 비활성화 / 활성화 | rows, detail | **Review.** The mod folder moves to `ModsDisabled/` (a "stash"), so it is not a delete. Alternatives: 끄기 / 켜기 (shorter, but implies the mod stays in place), 보관 / 복구. |
| Disabled (badge) | 비활성 | | |
| Installed / Update available / Subscribed (badges) | 설치됨 / 업데이트 있음 / 구독 중 | | |
| CANCEL ALL | 모두 취소 | | |
| Import Mod (.zip)... | 모드 가져오기 (.zip) | LOCAL tab | |
| Import from StS2LauncherMM... | StS2LauncherMM에서 가져오기 | LOCAL tab | Folder name stays Latin. "에서" is unaffected by the final consonant. |
| Grant Storage Permission | 저장소 권한 허용 | LOCAL tab | 허용 is Android's wording. |
| Remove Mod | 모드 삭제 | detail | |
| Sort: Popular / Newest / Trending / Last Updated / Top Rated | 인기순 / 최신순 / 급상승 / 최근 업데이트순 / 평점순 | Workshop sort dropdown | **Review** for 급상승 (Steam Korean says "최근 인기"). |
| Landscape / Portrait toggle | ⤢ 가로 / ⤢ 세로 | Mod Hub | Already used. |
| Queued / Downloading / Completed / Failed | 대기 중 / 다운로드 중 / 완료 / 실패 | downloads | |

### Multiplayer

| English | Korean | Where | Reason, alternatives |
|---|---|---|---|
| JOIN | 참가 | LAN panel | Alternatives: 접속, 입장. 참가 is used for joining a game session. |
| Enter host IP address | 호스트 IP 주소 입력 | LAN placeholder | |

### Added while localizing the Mod Hub (review)

| English | Korean | Where | Reason, alternatives |
|---|---|---|---|
| subscribers / rated | 구독자 N명 / 평점 N% | Workshop card | **Review.** Steam's own wording is "긍정적 평가 N%". |
| Unmanaged, root files (badge) | 관리 안 됨 · 루트 파일 | LOCAL row | 미관리 is rare outside formal writing. |
| USE WORKSHOP | 창작마당 버전 사용 | subscribed conflict row | **Review.** Alternatives: 창작마당 사용 (shorter), 창작마당으로 교체. |
| manually installed | 수동 설치 | | Alternative: 직접 설치. |
| Workshop item | 아이템 | | Steam Korean client wording. Alternative: 항목. |
| by Author | 제작자: Author | | Alternative: 제작. |
| Min game version / Path | 최소 게임 버전 / 경로 | detail | |
| Change notes (detail tab) | 변경 사항 | detail page | Shorter for the compact tab bar and, to our knowledge, Steam's Korean Workshop wording (not verified online). Was 업데이트 노트. |
| Back-press toast | 한 번 더 누르면 종료됩니다 | Android toast | |
| (private) branch suffix | (비공개) | branch picker | |

### Particle and unit notes for new strings

- A version string such as `v0.5.1` is read "일", so it takes 이 / 은 / 을. Prefer wording that avoids a particle after it ("런처 v0.5.1 업데이트가 있습니다").
- `Loc.Size` and `SaveProgressSummary.FormatSize` print "1.5MB" in Korean and "1.5 MB" in English.
