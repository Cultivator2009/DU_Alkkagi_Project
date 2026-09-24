using System;
using System.Collections.Generic;
using UnityEngine;

public enum Language
{
    Korean,
    English
}

// Minimal Korean/English string table. Deliberately not the Unity
// Localization package: the whole game has a few dozen strings, and this
// keeps it free of the Addressables dependency that package pulls in.
public static class Loc
{
    private const string PrefsKey = "language";

    public static event Action OnLanguageChanged;
    public static Language Current { get; private set; }

    static Loc()
    {
        var fallback = Application.systemLanguage == SystemLanguage.Korean ? Language.Korean : Language.English;
        Current = (Language)PlayerPrefs.GetInt(PrefsKey, (int)fallback);
    }

    public static void Set(Language language)
    {
        if (language == Current) return;
        Current = language;
        PlayerPrefs.SetInt(PrefsKey, (int)language);
        OnLanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        if (!Table.TryGetValue(key, out var entry))
        {
            Debug.LogWarning($"Loc: missing string '{key}'");
            return key;
        }
        return Current == Language.Korean ? entry.ko : entry.en;
    }

    public static string Get(string key, params object[] args) => string.Format(Get(key), args);

    private static readonly Dictionary<string, (string ko, string en)> Table = new Dictionary<string, (string ko, string en)>
    {
        // Player 0 always owns the black stones and moves first (see GameScene).
        { "player.black", ("흑", "Black") },
        { "player.white", ("백", "White") },
        { "player.number", ("플레이어 {0}", "Player {0}") },
        // With janggi pieces the sides are Cho (moves first, like black) and Han.
        { "player.cho", ("초", "Cho") },
        { "player.han", ("한", "Han") },
        // The letters on janggi pieces, the same in both languages.
        { "piece.cho", ("초", "초") },
        { "piece.han", ("한", "한") },
        { "piece.cha", ("차", "차") },
        { "piece.po", ("포", "포") },
        { "piece.ma", ("마", "마") },
        { "piece.sang", ("상", "상") },
        { "piece.sa", ("사", "사") },
        { "piece.jol", ("졸", "졸") },
        { "piece.byeong", ("병", "병") },

        { "hud.turn", ("{0} 차례", "{0}'s turn") },
        { "hud.turnBadge", ("차례", "Turn") },
        { "hud.remaining", ("남은 알", "Pieces left") },
        { "hud.captured", ("잡은 알 {0}", "Captured {0}") },
        { "hud.turnTimed", ("{0} 차례 · {1}", "{0}'s turn · {1}") },
        { "hud.skip", ("턴 넘기기", "Skip turn") },
        { "hud.timeout", ("{0} 시간 초과 · 턴을 넘겨요", "{0} ran out of time") },
        { "hud.skipped", ("{0} 쪽이 턴을 넘겼어요", "{0} skipped the turn") },
        { "hud.menu", ("메뉴", "Menu") },
        { "pause.title", ("메뉴", "Menu") },
        { "pause.resume", ("계속하기", "Resume") },
        { "pause.concede", ("기권", "Concede") },
        { "pause.concedeSide", ("{0} 기권", "{0} concedes") },
        { "pause.mainMenu", ("메인 메뉴로", "Main menu") },
        { "pause.confirm", ("한 번 더 누르면 확정", "Press again to confirm") },
        { "pause.captionLocal", ("Esc로 닫기 · 게임이 멈춰 있어요", "Esc closes · the game is paused") },
        { "pause.captionOnline", ("Esc로 닫기 · 온라인 대전은 멈추지 않아요", "Esc closes · online matches don't pause") },

        { "placement.title", ("알 배치", "Place pieces") },
        { "placement.hint", ("내 진영을 눌러 알을 놓으세요\n남은 알 {0}개", "Click inside your zone\n{0} left to place") },
        { "placement.hintMove", ("알을 끌어 옮길 수 있어요", "Drag a piece to move it") },
        { "placement.hintDone", ("모두 놓았어요", "All pieces placed") },
        { "placement.turn", ("{0} 배치 차례", "{0} to place") },
        { "placement.waiting", ("상대가 배치하는 중", "Opponent is placing") },
        { "placement.opponentReady", ("상대 준비 완료", "Opponent is ready") },
        { "placement.readyDone", ("준비 완료 · 상대를 기다려요", "Ready · waiting for opponent") },
        { "placement.clock", ("{0} {1}", "{0} {1}") },
        { "placement.timeoutNote", ("시간이 끝나면 남은 알은 무작위로 놓여요", "Time up places the rest at random") },
        { "placement.ready", ("준비 완료", "Ready") },

        { "win.stamp", ("승", "Win") },
        { "win.title", ("{0} 승리", "{0} wins") },
        { "win.detail", ("{0} · 남은 돌 {1}개", "{0} · {1} stones left") },
        { "win.rematch", ("다시 하기", "Play again") },
        { "win.menu", ("메인 메뉴", "Main menu") },

        { "result.win", ("승리", "Victory") },
        { "result.lose", ("패배", "Defeat") },
        { "result.stampLose", ("패", "Loss") },
        { "reason.knockout", ("{0}의 알이 모두 떨어졌어요", "All of {0}'s pieces are off the board") },
        { "result.draw", ("무승부", "Draw") },
        { "result.stampDraw", ("무", "Draw") },
        { "reason.bothOut", ("마지막 알이 함께 떨어져 쏜 {0} 쪽이 졌어요", "Both last pieces went out, so {0} loses for shooting") },
        { "reason.bothOutWin", ("마지막 알이 함께 떨어져 쏜 {0} 쪽이 이겼어요", "Both last pieces went out, so {0} wins for shooting") },
        { "reason.surrender", ("{0} 쪽이 기권했어요", "{0} conceded") },
        { "reason.bothOutDraw", ("마지막 알이 함께 떨어져 무승부예요", "Both last pieces went out: a draw") },
        { "reason.opponentLeft", ("상대가 게임을 나갔어요", "Your opponent left the game") },
        { "stats.kills", ("킬", "Kills") },
        { "stats.nongae", ("논개", "Nongae") },
        { "stats.suicides", ("자살", "Suicides") },
        { "stats.teamKills", ("팀킬", "Team kills") },
        { "kill.nongae", ("논개", "Nongae") },
        { "kill.suicide", ("자살", "Suicide") },
        { "kill.teamKill", ("팀킬", "Team kill") },
        { "stats.shots", ("튕긴 횟수", "Shots") },
        { "stats.time", ("경기 시간 {0}", "Match time {0}") },
        { "series.score", ("연속 전적  {0} {1} : {2} {3}", "Series  {0} {1} : {2} {3}") },
        { "series.draws", (" · 무 {0}", " · {0} drawn") },
        { "series.panel", ("{0} · {1}승", "{0} · {1}W") },
        { "rematch.request", ("재대결", "Rematch") },
        { "rematch.accept", ("재대결 수락", "Accept rematch") },
        { "rematch.waiting", ("상대 기다리는 중…", "Waiting…") },
        { "rematch.opponentWants", ("상대가 재대결을 원해요", "Your opponent wants a rematch") },
        { "rematch.opponentLobby", ("상대가 로비로 돌아갔어요", "Your opponent went back to the lobby") },
        { "rematch.opponentLeft", ("상대가 나갔어요", "Your opponent left") },
        { "gameover.lobby", ("로비로", "Lobby") },

        { "menu.title", ("알까기", "Alkkagi") },
        { "menu.local", ("로컬 대전", "Local match") },
        { "menu.localSub", ("한 PC에서 2인, 또는 AI와", "Two players, or against the AI") },
        { "setup.opponent", ("상대", "Opponent") },
        { "opponent.Human", ("2인", "2 players") },
        { "opponent.AIEasy", ("AI 쉬움", "AI easy") },
        { "opponent.AINormal", ("AI 보통", "AI normal") },
        { "opponent.AIHard", ("AI 어려움", "AI hard") },
        { "menu.online", ("온라인 대전", "Online match") },
        { "menu.onlineSub", ("Steam 친구나 공개 로비에서", "With a friend or a public lobby") },
        { "menu.settings", ("설정", "Settings") },
        { "menu.quit", ("종료", "Quit") },

        { "settings.title", ("설정", "Settings") },
        { "settings.language", ("언어", "Language") },
        { "settings.close", ("닫기", "Close") },
        { "settings.sound", ("소리", "Sound") },
        { "settings.masterVolume", ("전체 음량", "Master volume") },
        { "settings.interfaceVolume", ("인터페이스 음량", "Interface volume") },
        { "settings.controls", ("조작", "Controls") },
        { "settings.reset", ("기본값으로", "Reset") },
        { "settings.janggiLetters", ("장기말 글자", "Janggi letters") },
        { "settings.hangul", ("한글", "Hangul") },
        { "settings.hanja", ("한자", "Hanja") },
        { "bind.CameraView", ("시점 전환 (누르고 있기)", "Camera view (hold)") },
        { "bind.CancelAim", ("조준 취소", "Cancel aim") },
        { "bind.LookAround", ("시점 돌리기 (누르고 끌기)", "Look around (hold and drag)") },
        { "bind.press", ("키를 누르세요 · Esc 취소", "Press a key · Esc cancels") },
        { "key.mouseRight", ("마우스 오른쪽", "Right mouse") },
        { "key.mouseMiddle", ("마우스 가운데", "Middle mouse") },
        { "key.mouseN", ("마우스 {0}", "Mouse {0}") },
        { "key.left", ("왼쪽 {0}", "Left {0}") },
        { "key.right", ("오른쪽 {0}", "Right {0}") },
        { "key.space", ("스페이스", "Space") },
        { "option.on", ("켜기", "On") },
        { "option.off", ("끄기", "Off") },
        { "option.stones", ("{0}개", "{0}") },
        { "option.seconds", ("{0}초", "{0}s") },
        { "option.noLimit", ("제한 없음", "None") },

        // Match rules (MatchSettings.Defs), shown in the lobby and the local setup screen.
        { "match.title", ("대전 규칙", "Match rules") },
        { "match.board", ("판", "Board") },
        { "match.pieces", ("알", "Pieces") },
        { "board.Go", ("바둑판", "Go board") },
        { "board.Janggi", ("장기판 (경첩)", "Janggi (hinged)") },
        { "pieces.GoStones", ("바둑알", "Go stones") },
        { "pieces.JanggiPieces", ("장기말", "Janggi pieces") },
        { "match.aimGuide", ("조준 가이드", "Aim guide") },
        { "match.blackStones", ("흑·초 알 개수", "Black/Cho pieces") },
        { "match.whiteStones", ("백·한 알 개수", "White/Han pieces") },
        { "match.spawn", ("알 배치", "Setup") },
        { "match.placementStyle", ("배치 방식", "Placement") },
        { "match.placementTime", ("배치 시간", "Setup time") },
        { "match.turnTime", ("턴 제한시간", "Turn timer") },
        { "match.bothOut", ("동시 소진 시", "If both run out") },
        { "spawn.preset", ("기본 배치", "Preset") },
        { "spawn.placement", ("직접 배치", "Place freely") },
        { "placement.styleHidden", ("동시 · 비공개", "Hidden") },
        { "placement.styleLive", ("동시 · 공개", "Open") },
        { "placement.styleAlternating", ("번갈아 한 개씩", "Take turns") },
        { "bothOut.ShooterLoses", ("쏜 쪽 패배", "Shooter loses") },
        { "bothOut.ShooterWins", ("쏜 쪽 승리", "Shooter wins") },
        { "bothOut.Draw", ("무승부", "Draw") },
        { "setup.start", ("시작", "Start") },
        { "setup.cancel", ("취소", "Cancel") },

        { "lobby.title", ("온라인 대전", "Online match") },
        { "lobby.create", ("로비 만들기", "Create lobby") },
        { "lobby.createSub", ("호스트가 되어 친구를 초대", "Host and invite a friend") },
        { "lobby.joinLabel", ("로비 코드로 참가", "Join with a lobby code") },
        { "lobby.codePlaceholder", ("로비 코드 입력", "Enter lobby code") },
        { "lobby.join", ("참가", "Join") },
        { "lobby.back", ("뒤로", "Back") },
        { "lobby.code", ("로비 코드", "Lobby code") },
        { "lobby.copy", ("복사", "Copy") },
        { "lobby.copied", ("복사됨", "Copied") },
        { "lobby.invite", ("Steam 친구 초대", "Invite a Steam friend") },
        { "lobby.emptySlot", ("빈 자리", "Open slot") },
        { "lobby.host", ("호스트", "Host") },
        { "lobby.guest", ("게스트", "Guest") },
        { "lobby.leave", ("나가기", "Leave") },
        { "lobby.quick", ("빠른 대전", "Quick match") },
        { "lobby.quickSub", ("빈 공개 로비에 바로 참가", "Jump into an open public lobby") },
        { "lobby.browser", ("공개 로비", "Public lobbies") },
        { "lobby.refresh", ("새로고침", "Refresh") },
        { "lobby.browserEmpty", ("열린 공개 로비가 없어요", "No open public lobbies") },
        { "lobby.browserSearching", ("찾는 중…", "Searching…") },
        { "lobby.browserCaption", ("같은 버전의 게임에서 만든 로비만 보여요", "Only lobbies on this game version show up") },
        { "lobby.rowRules", ("{0} · {1} · {2} 대 {3}", "{0} · {1} · {2} v {3}") },
        { "lobby.visibility", ("공개 범위", "Who can join") },
        { "lobby.vis.public", ("공개", "Public") },
        { "lobby.vis.friends", ("친구", "Friends") },
        { "lobby.vis.private", ("비공개", "Private") },
        { "lobby.kick", ("내보내기", "Remove") },
        { "lobby.rulesHost", ("매치를 시작하면 이 규칙으로 대전해요", "The match is played with these rules") },
        { "lobby.rulesGuest", ("호스트가 정한 규칙이에요", "The host sets the rules") },
        { "lobby.start", ("매치 시작", "Start match") },
        { "lobby.status.noSteam", ("Steam에 연결할 수 없어요", "Can't reach Steam") },
        { "lobby.status.idle", ("로비를 만들거나 참가하세요", "Create or join a lobby") },
        { "lobby.status.creating", ("로비 만드는 중…", "Creating lobby…") },
        { "lobby.status.joining", ("참가하는 중…", "Joining…") },
        { "lobby.status.waitingOpponent", ("상대 기다리는 중", "Waiting for an opponent") },
        { "lobby.status.waitingHost", ("호스트의 시작을 기다리는 중", "Waiting for the host") },
        { "lobby.status.ready", ("시작할 수 있어요", "Ready to start") },
        { "lobby.status.badCode", ("올바른 로비 코드를 입력하세요", "Enter a valid lobby code") },
        { "lobby.status.failed", ("로비에 들어가지 못했어요", "Couldn't enter the lobby") },
        { "lobby.status.searching", ("공개 로비 찾는 중…", "Looking for a public lobby…") },
        { "lobby.status.version", ("게임 버전이 달라 참가할 수 없어요", "That lobby runs a different game version") },
        { "lobby.status.kicked", ("호스트가 로비에서 내보냈어요", "The host removed you from the lobby") },
    };
}
