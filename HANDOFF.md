# 세션 인계 노트 (최종 갱신 2026-09-23, Mac mini M4)

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
- **돌은 씬에 직접 배치하지 않고 매치 시작 때 생성합니다** (`6125930`). `Board_GO`의 `BoardSetup`이 `PieceTemplates` 아래의 비활성 템플릿(흑/백 1개씩)을 복제합니다. `pieceID`는 흑 `A`부터, 백 `a`부터입니다. 네트워크 메시지가 이 ID로 돌을 찾으므로 두 머신이 같은 규칙으로 생성해야 합니다. 개수가 다르면 게스트가 에러 로그를 남깁니다.
- **온라인 턴 흐름** (`31f7a49`): 물리 시뮬레이션과 `TurnController`는 호스트만 돌립니다. 게스트의 튕기기(`FlickCommand`)는 `TurnController.TryApplyExternalFlick`으로 **일반 턴과 같은 흐름**을 탑니다. 턴이 끝날 때마다 `TurnResult`가 **남은 돌 전체의 최종 위치**를 담아 보내므로, 게스트 화면은 매 턴 호스트와 일치하게 수렴합니다. 게스트 돌은 kinematic 상태에서 `MovePosition`으로 움직입니다.
- **종료 규칙**: 한쪽 돌이 모두 나가면 녹아웃. 양쪽 마지막 돌이 같은 샷에 나가면 대전 규칙에 따라 쏜 쪽 패배(기본) / 쏜 쪽 승리 / 무승부입니다. 무승부는 승자 `null`(네트워크에서는 `-1`)로 표현합니다. 온라인에서 상대가 나가거나 연결이 끊기면 남은 쪽 **기권승**입니다. 사유는 `MatchEndReason`으로 표현합니다.
- 연속 전적은 `MatchSeries`가 셉니다 (온라인은 로비별, 로컬은 메인 메뉴에서 새로 시작할 때까지, 무승부 포함).
- **온라인 시작 순서**: 호스트는 게스트의 `ClientReady`를 받은 뒤에야 배치 단계나 첫 턴을 시작합니다. 전에는 게스트가 로딩 중일 때 호스트가 먼저 쏠 수 있었습니다.
- 튕기기 힘은 `당긴 거리 / maxDragDistance(1.0)` × `maxForce(50)`입니다. 이전 조작감과 같은 곡선이고, 조정하려면 `GamePieceDragAndReleaseForce.maxDragDistance`를 바꾸세요. 3% 미만으로 놓으면 튕기지 않고 취소됩니다.
- **프로토콜 호환성**: `LoadGameScene`(규칙 포함)과 `TurnResult`(턴 종료 방식 포함) 형식이 또 바뀌었고, 배치와 스킵 메시지가 추가되었습니다. 두 머신이 같은 커밋이 아니면 온라인 대전이 깨집니다.

## 대전 규칙 (`MatchSettings`, 2026-09-23)

규칙은 모두 로비 단위입니다. 온라인은 호스트가 로비에서 정하고, 로컬은 "로컬 대전"을 누르면 뜨는 대전 설정 카드에서 정합니다. 클라이언트 설정 화면에는 언어만 남았습니다.

| 규칙 | 값 | 기본값 |
|---|---|---|
| 조준 가이드 | 켜기 / 끄기 | 켜기 |
| 흑 돌 개수 / 백 돌 개수 | 각각 1–12 (비대칭 가능) | 6 / 6 |
| 돌 배치 | 기본 배치 / 직접 배치 | 기본 배치 |
| 배치 방식 (직접 배치일 때) | 동시·비공개 / 동시·공개 / 번갈아 한 개씩 | 동시·비공개 |
| 배치 시간 (직접 배치일 때) | 30 / 45 / 60 / 90 / 120초 | 60초 |
| 턴 제한시간 | 제한 없음 / 10 / 15 / 20 / 30 / 60초 | 제한 없음 |
| 동시 소진 시 | 쏜 쪽 패배 / 쏜 쪽 승리 / 무승부 | 쏜 쪽 패배 |

- **규칙 추가 방법**: `MatchSettingId`에 항목 추가 → `MatchSettings.Defs`에 정의 한 줄(키, 라벨, 허용값, 기본값, 표시 형식, 필요하면 적용 조건) → 타입 있는 접근자 → `Loc`에 문자열 → `Tools > Alkkagi UI`의 1, 3, 4번을 다시 실행합니다. 로비 데이터(`rule.<key>`), PlayerPrefs(`match.<key>`), 네트워크(`LoadGameScene`)는 자동으로 따라옵니다.
- **적용 시점**: 호스트가 매치를 시작할 때 규칙 전체를 `LoadGameScene`에 실어 보냅니다. 재대결도 같습니다. 게임 코드는 `MatchSettings.Current`만 읽습니다.
- **사전 배치**: `Board_GO/SpawnLayouts/<개수>/` 아래 점들이 흑의 배치입니다. Scene 뷰에서 끌어서 조정하면 되고, 백은 판 중심을 기준으로 180° 돌려서 씁니다. `Tools > Alkkagi > Reset spawn layouts`로 기본값을 다시 만들 수 있습니다 (7개까지 한 줄, 8개부터 두 줄, 6개는 예전 배치와 같음).
- **직접 배치** (`PlacementPhase` 규칙, `PlacementController` 입력/화면, `PlacementHud` 카드): 자기 진영(`BoardSetup.blackZone`, 백은 대칭)을 클릭해 놓고, 동시 방식에서는 준비 완료 전까지 끌어서 옮길 수 있습니다. 각자 시계가 있고, 0이 되면 남은 돌을 진영 안에 무작위로(겹치지 않게) 놓습니다. 로컬 핫시트는 동시 방식도 흑 → 백 순서로 진행합니다. 온라인은 호스트가 규칙을 판정합니다. 게스트는 자기 사본에 먼저 놓아 보이고, 호스트의 `PlacementState`가 확정하거나 되돌립니다.
- **턴 제한시간/스킵**: 시간이 다 되면 턴을 넘깁니다. 자기 차례에는 패널 옆 "턴 넘기기" 버튼으로 직접 넘길 수 있습니다. `TurnController.OnTurnPassed`와 `TurnResult`의 `TurnEnd`(Shot / Timeout / Skipped)로 구분하고, 넘긴 턴은 튕긴 횟수에 세지 않습니다.

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

### 2026-09-23 (첫 2대 실전 테스트 후 수정)

첫 Mac(호스트)–Windows(게스트) 테스트에서 발견한 문제를 고쳤습니다: 백 턴이 끝나지 않음, 게스트 화면이 움직이지 않음, 중도 퇴장 무반응, 끝나면 멈춤.

| 커밋 | 내용 |
|---|---|
| `31f7a49` | 게스트 튕기기가 턴으로 처리되지 않던 문제, 턴 종료 확정 동기화, 게스트 이동 방식, 튕긴 직후 정지 판정 레이스, Steam 반쯤 초기화 상태 방어 (이전 세션 NRE 47,036회) |
| `5918abc` | 킷 폰트를 GUID를 유지한 채 다시 굽기 (다시 만들면 다른 prefab 폰트가 깨지던 문제) |
| `801aa95` | 게임 오버 화면(결과, 사유, 점수판, 경기 시간, 연속 전적), 온라인 재대결, 로비 복귀, 기권승, 동시 소진 규칙 |
| `80d9e34` | 조준 UI(링 게이지, 화살표, %, 첫 충돌 가이드), 가이드 설정(로컬 설정 / 로비 호스트) |
| `6125930` | 대전 규칙 체계(`MatchSettings`): 돌 개수(비대칭), 사전 배치/직접 배치(3가지 방식, 시간 초과 시 무작위), 턴 제한시간 + 스킵, 동시 소진 규칙 선택(무승부 포함), 게스트 로딩 전 매치 시작 방지 |

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

2026-09-23 수정 사항은 아래 "네트워크 로직을 에디터 한 대로 테스트하기" 방식으로 메시지 단위까지 검증했습니다. 하지만 **실제 두 대에서는 다시 확인해야 합니다**:

- 로비 규칙 카드: 호스트가 바꾼 값이 게스트 카드에 바로 보이는지, 매치에 그대로 적용되는지 (비대칭 돌 개수로 확인하기 쉬움)
- 직접 배치 3가지 방식 전부. 특히 비공개 방식에서 게스트 화면에 호스트 돌이 끝날 때까지 안 보이는지, 게스트가 놓은 돌이 튀지 않는지, 시간 초과 시 무작위 배치
- 턴 제한시간의 게스트 쪽 카운트다운과 호스트 판정이 체감상 맞는지 (게스트는 자기 시계로 표시하고, 판정은 호스트가 합니다)
- 게스트의 턴 넘기기 버튼
- 게스트 턴 진행과 게스트 화면 동기화 (튕기는 동안의 스냅샷, 턴 종료 시 위치 확정)
- 중도 퇴장 (앱 종료, 네트워크 끊김) 시 상대 쪽 기권승 화면. 앱을 종료하면 로비를 떠나므로 바로 전달돼야 하고, 크래시는 Steam 타임아웃 뒤에 전달됩니다
- 온라인 재대결 핸드셰이크, 로비 복귀, 로비에서 새 판 시작
- **친구 초대**: Steam 오버레이가 필요해서 Steam으로 실행한 빌드에서만 동작합니다 (에디터 불가). `+connect_lobby`(게임이 꺼진 상태에서 초대 수락)도 미검증입니다.

## 네트워크 로직을 에디터 한 대로 테스트하기

`NetworkMatchBridge`는 `ISessionTransport` 인터페이스로 통신하므로, **보낸 메시지를 기록하는 가짜 transport를 주입하면** Steam이나 두 번째 머신 없이 호스트와 게스트 로직을 검증할 수 있습니다 (2026-09-23 수정은 이렇게 검증했습니다):

1. GameScene에서 Play → `MatchSettings.Current`에 원하는 규칙을 넣고 `GameManager.manager.gameState = GameReadyProcess`로 매치 시작 (규칙은 도메인 리로드 때 초기화되므로 Play 뒤에 넣으세요)
2. 로비가 없으니 `GamePreparation`은 로컬 경로로 바로 시작해 버립니다. 온라인 시작 경로를 시험하려면 되돌리세요: 배치 모드는 `Placement` 속성을 리플렉션으로 `null`로, 기본 배치는 시작하지 않은 새 `TurnController`로 교체하고, `gameState = WaitingForPlayers`로 둡니다
3. 빈 GameObject에 `NetworkMatchBridge` 추가. 로비가 없으니 `Start`에서 휴면 상태가 됩니다
4. 리플렉션으로 `transport`(가짜), `gameManager`, `isHost`, `hostId`, `opponentId`를 넣고 `BuildPieceLookup()` + `InitHost()`(또는 `InitGuest()`) 호출, `enabled = true`
5. `HandleMessage(senderId, NetMessage.Write...(...))`로 상대 메시지를 주입하고, 가짜 transport에 기록된 메시지와 게임 상태를 확인. 호스트는 `ClientReady`를 받아야 매치를 시작합니다
6. HUD는 매치 시작 시점에 브리지를 찾으므로, 나중에 붙인 브리지에는 `MainGameUIController`의 `networkBridge` 필드와 `SubscribeNetwork`로 수동 연결

게스트 테스트는 `GameManager.SkipLocalTurnProcessing = true`로 두고 `StartMatch` → (배치 모드면 `PlacementState`) → 스냅샷 → `TurnResult` 순으로 주입하면 됩니다. 실제 턴을 돌리려면 `TurnController.TryApplyExternalFlick`로 튕기면 됩니다 (로컬 매치에서도 동작). 배치는 `GameManager.Placement.TryPlace/TryReady`로 직접 진행할 수 있습니다. 화면 반영(`PlacementController.LateUpdate`)은 다음 프레임에 일어납니다.

## 미결정/보류 사항

- 아이템전 콘텐츠: 코드 훅(`ITurnAction`, `MainGameUIController.ShowAvailableItems`)만 있고, UI 슬롯은 오버홀 때 제거했습니다.
- 설정 화면: 언어만 있습니다 (조준 가이드는 대전 규칙으로 옮겼습니다).
- **턴 넘기기 교착**: 양쪽이 계속 넘기면 게임이 끝나지 않습니다. 연속 스킵 제한이나 무승부 처리 같은 규칙이 필요할 수 있습니다 (미정).
- 체이스 카메라(`CM vcam_chasecam`의 `TargetGroup1`)는 예전에 씬에 있던 흑 돌을 따라갔는데, 그 돌이 지금은 비활성 템플릿입니다. 돌이 런타임에 생성되므로, 쓰려면 매치 시작 때 대상을 지정해야 합니다.
- 씬의 `Players_ref`(P1–P4)와 루트의 비활성 `GamePiece_GO`는 코드에서 쓰지 않는 예전 오브젝트입니다 (그대로 둠). 쓰지 않던 `SpawnPointPresets`는 새 `SpawnLayouts`로 대체했습니다.
- 튕기기 힘 조정: 기본값은 이전과 같습니다. 체감을 보고 `maxDragDistance`를 조정하세요.
- 로비에서 호스트가 나가면 Steam이 게스트에게 방장을 넘기고, 게스트는 혼자 남은 로비의 호스트가 됩니다 (다른 친구를 초대할 수 있음). 그대로 둘지는 미정입니다.
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
