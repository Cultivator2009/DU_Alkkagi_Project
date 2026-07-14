# 세션 인계 노트 (2026-07 Windows → Mac mini M4)

Claude Code로 진행한 작업의 인계용 요약입니다. 새 머신/새 세션에서 이 파일을 먼저 읽으면 맥락을 빠르게 따라잡을 수 있습니다.

## 프로젝트 개요

한국 전통 알까기를 모티브로 한 3D 물리 기반 1:1 대전 게임. 2023년 초기 프로토타입에서 중단되었다가 2026-07에 재개. 계획 원문은 `.claude`가 아니라 이 세션의 대화 기록에만 있었으므로, 아래 요약이 사실상 유일한 설계 문서입니다.

## 환경

- **Unity 버전: 6000.4.6f1** (원래 2022.3.1f1로 시작했으나, 세션 도중 알 수 없는 외부 프로세스가 프로젝트를 열어 자동 마이그레이션됨 — 되돌릴지 물었고 **사용자가 Unity 6 유지를 선택**함)
- `Rigidbody.velocity` → `Rigidbody.linearVelocity`로 이미 전환 완료 (Unity 6 네이밍)
- `.gitignore`에 `*.meta`를 무시하는 잘못된 규칙이 있어서 **961개 `.meta` 파일이 git에 전혀 추적되지 않고 있던 버그를 발견/수정함** (커밋 `c8cb3b8`). 새 clone에서는 문제없음.
- 원격: `https://github.com/Cultivator2009/DU_Alkkagi_Project.git`, 브랜치 `main`

## 완료된 작업 (Phase 1~4, 커밋 6개)

1. **로컬 코어 재작성** (`9ae7891`) — `GameManager`의 폴링식 상태머신을 `TurnController`(순수 C#, 이벤트 기반) + `IRuleset`/`ClassicRuleset`(승패·점수 로직, 이전엔 아예 없었음) + `PieceSelector`(입력)로 분리. 정지 감지 버그 수정(`OnCollisionStay` 의존 → `FixedUpdate` 속도 폴링). `ItemMode/ITurnAction.cs`는 향후 아이템전을 위한 빈 훅.
2. **.gitignore 수정** (`c8cb3b8`) — 위 `*.meta` 버그.
3. **Steam P2P 네트워킹** (`44b98c0`, `de476b0`) — Facepunch.Steamworks를 `Assets/Plugins/`에 수동 설치(공식 배포 방식). `Assets/Scripts/Net/`: `ISessionTransport`(추상화) → `SteamTransport`(P2P 패킷) / `SteamLobbyManager`(로비=유일한 서버) / `NetMessage`(바이너리 프로토콜) / `NetworkMatchBridge`(호스트 권위 구조 — 호스트만 물리 시뮬레이션, 게스트는 kinematic+보간) / `LobbySceneUI`(런타임 코드로 UI 생성, 손으로 씬 YAML 안 만듦). `LobbyScene.unity`를 실제 로비 씬으로 새로 구축(기존 `MainLobby_Ui`/`Login_UI` 프리팹은 사용 안 함, 처리는 보류 상태).
4. **Steam 출시 준비** (`27c10b6`) — App ID를 코드가 아니라 실행 파일 옆 `steam_appid.txt`에서 런타임에 읽도록 변경(실제 App ID가 커밋될 일 없음). `steam_appid.txt`는 gitignore되어 있고 `steam_appid.txt.example`(더미 480)만 커밋됨. `applicationIdentifier`의 `DefaultCompany` 플레이스홀더 수정.
5. **UI 훅 정리** (`be41cf0`) — `MainGameUIController`의 구독 타이밍 버그 수정, 게스트 화면용 턴/점수/승패 이벤트(`NetworkMatchBridge.OnGuestTurnChanged`/`OnGuestMatchEnded`) 연결, 아이템 바 placeholder(`ShowAvailableItems`) 추가.

## 아직 실제로 테스트 안 된 것

- **호스트/게스트 P2P 실전 테스트 전무** — 코드 리뷰와 Unity 에디터 로그 기반 컴파일 확인만 했음. 실제로 Steam 클라이언트 두 개(다른 계정)로 로비 생성→참가→매치 시작→턴 동기화까지 플레이해본 적 없음.
- `MainGame_UI.prefab`에 `MainGameUIController` 컴포넌트를 아직 안 붙임 — turnText/scoreTexts/winPanel/winText 필드 연결 필요.
- `LobbyScene`은 런타임 생성 UI라 비주얼이 매우 투박함(사용자가 스타일링할 부분).

## 미결정/보류 사항

- `Assets/UI/Login_UI.prefab`, `MainLobby_Ui.prefab` (MainMenuScene 안, 빈 껍데기) — 유지할지 삭제할지 미결정.
- 아이템전 실제 아이템 콘텐츠 — 훅만 있고 미구현.
- 스크립팅 백엔드는 Mono 유지 중 — macOS Standalone 빌드를 실제로 낼 계획이면 **IL2CPP로 전환 필요**(Mono는 macOS Standalone 빌드 미지원).

## Unity-MCP (Claude ↔ Unity Editor 직접 연동) 관련

여러 후보(CoplayDev/unity-mcp, IvanMurzak/Unity-MCP, CoderGamester/mcp-unity) 비교 후 **IvanMurzak/Unity-MCP**을 선택해 설치 시도함:

- `Packages/manifest.json`에 git URL 의존성 + OpenUPM scoped registry(`extensions.unity` 스코프) 추가 완료, Unity 쪽 패키지 리졸브는 성공.
- **Windows 머신에서는 여기서 막힘**: 이 플러그인의 실제 도구 호출이 `unity-mcp-cli`(npm 패키지)를 통해 이루어지는데, 이 머신에 Node.js/npm이 아예 없어서 `.claude/skills/`에 생성된 70여 개 스킬이 전부 미작동 상태.
- `.claude/skills/`는 **git에 커밋하지 않았음**(로컬 전용, MCP 설치 시점 이후 변경이라 이번 커밋 범위에서 제외).
- **Mac에서는 `brew install node`로 훨씬 쉽게 풀릴 것으로 예상** — 이어서 진행하면 됨.
- 참고: IvanMurzak 쪽은 프로젝트 경로에 공백이 있으면 깨지는 알려진 이슈가 있어서, Windows에서는 `E:\Unity Projects\DU_Alkkagi_Project` → `E:\DU_Alkkagi_Project` 정션(junction)을 만들어 우회함. **Mac에서 새로 clone할 때는 애초에 공백 없는 경로**(예: `~/Projects/DU_Alkkagi_Project`)를 쓰면 이 문제 자체가 발생하지 않음.
- 보안 참고: 커뮤니티 Unity MCP 서버들은 전반적으로 "로컬 프로세스가 인증 없이 에디터에 코드 실행 명령을 보낼 수 있는 구조"라는 지적이 Unity 공식 포럼에 있었음(주로 CoplayDev 구현 대상). 로컬 전용으로만 쓰고 텔레메트리는 꺼둘 것.

## Mac mini M4로 넘어갈 때 할 일

1. 이 레포를 **공백 없는 경로**에 clone
2. Unity Hub에서 Apple Silicon 네이티브 6000.4.x 에디터 설치 후 해당 경로로 프로젝트 열기
3. 프로젝트 루트에 `steam_appid.txt` 재생성 (`480` 한 줄, `steam_appid.txt.example` 참고) — gitignore되어 clone에는 없음
4. Facepunch.Steamworks는 이미 macOS용 바이너리(`Facepunch.Steamworks.Posix.dll`, `redistributable_bin/osx/libsteam_api.dylib`)가 올바른 플랫폼 태그로 포함되어 있어 별도 작업 불필요
5. `brew install node` 후 Unity 에디터의 `Window → AI Game Developer`에서 MCP 설정 재진행
6. 실제 Steam 두 계정으로 호스트/게스트 플레이 테스트 (지금까지 한 번도 안 해봄 — 최우선 순위)
