# 세션 인계 노트 (최종 갱신 2026-09-22, Mac mini M4)

Claude Code로 진행한 작업의 인계용 요약입니다. 새 머신이나 새 세션에서 이 파일을 먼저 읽으면 맥락을 빠르게 따라잡을 수 있습니다. 설계 문서가 따로 없으므로 이 파일이 사실상 유일한 설계 문서입니다.

## 프로젝트 개요

한국 전통 알까기를 모티브로 한 3D 물리 기반 1:1 대전 게임입니다. 2023년 초기 프로토타입에서 중단되었다가 2026-07에 재개했습니다. Steam 출시가 목표입니다.

## 환경

- **Unity 6000.4.6f1** (Apple Silicon 네이티브). 원래 2022.3.1f1로 시작했고, 도중에 자동 마이그레이션된 뒤 Unity 6을 유지하기로 했습니다. 씬 3개는 `320ae1a`에서 Unity 6 포맷으로 다시 저장했습니다.
- 주 개발 머신은 **Mac mini M4**입니다. 경로에 공백이 없어야 합니다 (`~/UnityProject/DU_Alkkagi_Project`). 기존 **Windows PC**는 P2P 테스트용 두 번째 머신입니다.
- 원격 저장소: `https://github.com/Cultivator2009/DU_Alkkagi_Project.git`, 브랜치 `main`
- `steam_appid.txt`는 gitignore 대상이라 머신마다 루트에 직접 만들어야 합니다 (`480` 한 줄, `steam_appid.txt.example` 참고).

### 버전 관리에서 주의할 점 (과거에 실제로 사고가 났던 부분)

- `.gitignore`는 toptal VisualStudio 템플릿에서 왔습니다. 그 템플릿의 규칙이 Unity 파일을 두 번 삼킨 적이 있습니다. `*.meta`는 `c8cb3b8`에서 고쳤고, `**/[Pp]ackages/*`는 `f385e50`에서 고쳤습니다. 규칙을 추가할 때는 `git check-ignore -v <경로>`로 확인하세요.
- `Packages/manifest.json`이 추적되지 않던 시절에 **ugui와 Cinemachine이 조용히 빠졌습니다.** ugui는 컴파일 에러로 드러났지만, Cinemachine은 에러 없이 카메라만 고정되었습니다. Cinemachine은 **반드시 2.x(현재 2.10.7)**를 유지해야 합니다. 씬의 컴포넌트 GUID가 2.x 클래스를 가리키고, 3.x에서는 그 클래스들이 Deprecated로 밀려납니다.

## 게임 구조 요약

- `GameManager`는 MainMenuScene에서 생성되는 `DontDestroyOnLoad` 싱글톤입니다. GameScene을 떠날 때는 **반드시 `EndMatch()`를 호출**해야 합니다. 호출하지 않으면 다음 판이 이전 판의 플레이어, 돌, TurnController를 물려받습니다.
- **Player 0 = 흑 = 화면 아래 = 선공**, Player 1 = 백입니다. 흑/백 판정은 `PlayersManager.color`가 아니라 돌의 `playerIndex`로 합니다 (color는 어디서도 설정되지 않습니다).
- 플레이 테스트는 **MainMenuScene에서 시작**해야 합니다. GameScene에서 바로 Play하면 `SceneMgmt`가 `GameReadyProcess`로 설정한 직후 `GameManager.Start()`가 `Mainmenu`로 덮어써서 매치가 시작되지 않습니다 (기존 동작, 미수정).
- 돌의 `pieceID`는 흑 A–F, 백 G–L로 고유합니다 (`27aa744`). 네트워크 메시지가 이 ID로 돌을 찾으므로, 돌을 추가하거나 복제할 때 중복되지 않게 해야 합니다. 중복되면 `NetworkMatchBridge`가 에러 로그를 남깁니다.

## 완료된 작업

### 2026-07 (Windows)

1. **로컬 코어 재작성** (`9ae7891`): `TurnController` / `IRuleset` / `ClassicRuleset` / `PieceSelector`로 분리하고, 정지 감지를 속도 폴링 방식으로 바꿨습니다.
2. **Steam P2P 네트워킹** (`44b98c0`, `de476b0`): Facepunch.Steamworks 기반의 호스트 권위 구조입니다. 호스트만 물리를 시뮬레이션하고, 게스트는 kinematic + 보간으로 따라갑니다.
3. **Steam 출시 준비** (`27c10b6`): App ID를 `steam_appid.txt`에서 런타임에 읽고, bundle id를 정리했습니다.

### 2026-09-22 (Mac 이전 + UI 오버홀)

| 커밋 | 내용 |
|---|---|
| `f385e50` | Packages 추적 복구, ugui 추가, LobbyScene을 Build Settings에 등록 |
| `5cefea3` | Cinemachine 2.10.7 복원 (GameScene 누락 스크립트 9 → 0) |
| `3eb2882` | UI 킷: Pretendard 폰트, 생성 스프라이트, 한/영 다국어 (`Loc`) |
| `320ae1a` | 씬 3개를 Unity 6 포맷으로 재저장 (내용 변경 없음) |
| `91ce20c` | 인게임 HUD 재구성 + `GameManager.EndMatch` (두 번째 판부터 플레이어가 4명이 되던 버그) |
| `97d208f` | 메인 메뉴 재구성 (가짜 로그인/로비 허브, `SceneChanger` 제거), 부팅 시 Steam 초기화 |
| `500344e` | 로비 UI 재구성, Steam 친구 초대/수락, 로비 핸들러 누수 수정 |
| `27aa744` | `pieceID` 중복 수정 (온라인에서 돌 12개 중 2개만 동기화되던 문제) |

## UI 시스템 (2026-09 오버홀)

스타일은 **한지(크림색 패널) + 먹(외곽선/텍스트) + 낙관 빨강(강조)**에 둥근 형태를 입혔습니다. 기준 해상도는 1920×1080이고, HUD는 높이 기준, 메뉴와 로비는 0.5로 맞춥니다.

- **빌더**: `Tools > Alkkagi UI`
  1. `Generate kit assets`: 스프라이트, 폰트 에셋, TMP 기본 폰트
  2. `Build HUD` → `Assets/UI/MainGame_UI.prefab` + GameScene
  3. `Build main menu` → `Assets/UI/MainMenu_UI.prefab` + MainMenuScene
  4. `Build lobby` → `Assets/UI/Lobby_UI.prefab` + LobbyScene
- **빌더를 다시 돌리면 prefab을 덮어씁니다.** 인스펙터에서 손으로 다듬기 시작한 화면은 더 이상 빌더로 다시 만들지 말고, 필요한 변경은 빌더 코드(`Assets/Editor/AlkkagiUI/`)에 옮긴 뒤 재생성하세요.
- **UI 문자열을 추가하거나 바꾸면 반드시 `1. Generate kit assets`를 다시 실행하세요.** 폰트는 정적 SDF 아틀라스(`Loc` 테이블의 모든 글자 + ASCII)에 동적 fallback을 붙인 구조입니다. 새 글자를 굽지 않으면 fallback 아틀라스에 들어가고, 에디터에서 플레이할 때마다 .asset 파일이 바뀝니다. 같은 이유로 prefab의 자리표시자 텍스트도 테이블에 있는 글자나 ASCII만 쓰세요.
- 문자열 테이블은 `Assets/Scripts/UI/Loc.cs`에 있습니다. 한/영 두 언어이고, 선택은 PlayerPrefs에 저장되며, 기본값은 OS 언어입니다. 동적 텍스트를 쓰는 컨트롤러는 `Loc.OnLanguageChanged`를 구독합니다.
- HUD의 "남은 돌"은 `totalPieceCnt`가 아니라 **실제로 남아 있는 돌을 세서** 표시합니다. 게스트 쪽에서는 `totalPieceCnt`가 줄어들지 않기 때문입니다.
- 폰트: Pretendard 1.3.9 (SIL OFL 1.1, `Assets/Fonts/Pretendard/LICENSE.txt`). 상업 배포 가능합니다.

## 아직 실제로 테스트하지 않은 것

- **호스트/게스트 P2P 실전 테스트** (Mac + Windows 두 대). 개발이 더 진행된 뒤에 하기로 했습니다. 이 Mac에는 Steam 클라이언트가 없어서 로비는 "Steam 없음" 상태까지만 검증했고, 로비 안 화면은 모습만 확인했습니다.
- **친구 초대**: Steam 오버레이가 필요해서 Steam으로 실행한 빌드에서만 동작합니다 (에디터 불가). `+connect_lobby`(게임이 꺼진 상태에서 초대 수락)도 미검증입니다.
- 게스트 쪽 HUD 갱신 (턴/점수/승패가 `NetworkMatchBridge` 이벤트로 오는 경로).

## 미결정/보류 사항

- 아이템전 콘텐츠: 코드 훅(`ITurnAction`, `MainGameUIController.ShowAvailableItems`)만 있고, UI 슬롯은 오버홀 때 제거했습니다.
- 온라인 재대결: 네트워크 프로토콜이 필요해서, 지금은 승리 화면의 "다시 하기"가 로컬에서만 보입니다.
- 설정 화면: 지금은 언어 설정만 있습니다.
- 스크립팅 백엔드는 Mono입니다. **Apple Silicon macOS 빌드를 내려면 IL2CPP로 전환**해야 합니다.
- 로비 빈 좌석 테두리는 목업의 점선 대신 흐린 실선입니다 (9-slice에서는 점선이 늘어나기 때문).

## Unity-MCP (Claude ↔ Unity Editor 연동): 로컬 전용

IvanMurzak/Unity-MCP를 **이 Mac에만 설치**했고, 의도적으로 커밋하지 않았습니다.

- **transport는 stdio만 사용합니다.** `setup-mcp --transport http`를 쓰면 `https://ai-game.dev/...` **클라우드 릴레이**가 설정됩니다. ai-game.dev 계정은 등록하지 않았습니다.
- 플러그인은 처음 열 때 `connectionMode = Cloud`가 기본값입니다. `unity-mcp-cli bootstrap-local --url http://localhost:25033 --token <UserSettings/AI-Game-Developer-Config.json의 token> .`으로 Custom으로 바꾼 뒤, `Window → AI Game Developer`에서 Connect를 누르세요.
- **커밋하면 안 되는 흔적 6곳**: `Packages/manifest.json`·`packages-lock.json`의 MCP 항목, `ProjectSettings/PackageManagerSettings.asset`, `Assets/Plugins/NuGet/`, `.mcp.json`, `ProjectSettings/ProjectSettings.asset`의 `UNITY_MCP_*` scripting define. 이 파일들에 정당한 변경을 커밋해야 할 때는 해당 부분만 골라서 스테이징하세요.
- **릴리스 빌드 전에 반드시 `unity-mcp-cli remove-plugin <프로젝트 경로>`를 실행하세요.** `Assets/Plugins/NuGet/`의 DLL 42개(18MB)가 전 플랫폼 활성화 상태라 그대로 두면 빌드에 포함됩니다.
- 보안: 로컬 프로세스가 인증 없이 에디터에서 코드를 실행할 수 있는 구조입니다. 포트는 127.0.0.1에만 바인딩됩니다.

## Windows 머신 동기화 절차

1. `git pull`: 로컬의 (전에는 무시되던) `Packages/manifest.json`이 저장소 버전으로 덮어써집니다. Windows에서 MCP 설정을 했었다면 그 항목은 사라집니다.
2. 프로젝트 루트에 `steam_appid.txt` 생성 (`480`)
3. Unity로 열면 ugui, Cinemachine 2.10.7, TMP 폰트가 자동으로 들어옵니다. 컴파일 에러가 나면 매니페스트에서 빠진 패키지가 있다는 신호입니다.
4. (선택) MCP가 필요하면 Node.js를 설치한 뒤 위 "Unity-MCP" 절차를 그대로 따르세요. `UserSettings/`는 머신별이라 Cloud → Custom 설정도 다시 해야 합니다.
