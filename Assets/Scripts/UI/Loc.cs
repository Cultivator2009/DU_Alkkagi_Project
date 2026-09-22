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

        { "hud.turn", ("{0} 차례", "{0}'s turn") },
        { "hud.turnBadge", ("차례", "Turn") },
        { "hud.remaining", ("남은 돌", "Stones left") },
        { "hud.captured", ("잡은 돌 {0}", "Captured {0}") },

        { "win.stamp", ("승", "Win") },
        { "win.title", ("{0} 승리", "{0} wins") },
        { "win.detail", ("{0} · 남은 돌 {1}개", "{0} · {1} stones left") },
        { "win.rematch", ("다시 하기", "Play again") },
        { "win.menu", ("메인 메뉴", "Main menu") },

        { "result.win", ("승리", "Victory") },
        { "result.lose", ("패배", "Defeat") },
        { "result.stampLose", ("패", "Loss") },
        { "reason.knockout", ("{0}의 돌이 모두 떨어졌어요", "All of {0}'s stones are off the board") },
        { "reason.bothOut", ("마지막 돌이 함께 떨어져 쏜 {0}이 졌어요", "Both last stones went out, so {0} loses for shooting") },
        { "reason.opponentLeft", ("상대가 게임을 나갔어요", "Your opponent left the game") },
        { "stats.captured", ("잡은 돌", "Captured") },
        { "stats.shots", ("튕긴 횟수", "Shots") },
        { "stats.time", ("경기 시간 {0}", "Match time {0}") },
        { "series.score", ("연속 전적  흑 {0} : {1} 백", "Series  Black {0} : {1} White") },
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
        { "menu.localSub", ("한 PC에서 2인", "Two players, one PC") },
        { "menu.online", ("온라인 대전", "Online match") },
        { "menu.onlineSub", ("Steam 친구와", "With a Steam friend") },
        { "menu.settings", ("설정", "Settings") },
        { "menu.quit", ("종료", "Quit") },

        { "settings.title", ("설정", "Settings") },
        { "settings.language", ("언어", "Language") },
        { "settings.close", ("닫기", "Close") },

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
        { "lobby.start", ("매치 시작", "Start match") },
        { "lobby.status.noSteam", ("Steam에 연결할 수 없어요", "Can't reach Steam") },
        { "lobby.status.idle", ("로비를 만들거나 코드로 참가하세요", "Create a lobby or join with a code") },
        { "lobby.status.creating", ("로비 만드는 중…", "Creating lobby…") },
        { "lobby.status.joining", ("참가하는 중…", "Joining…") },
        { "lobby.status.waitingOpponent", ("상대 기다리는 중", "Waiting for an opponent") },
        { "lobby.status.waitingHost", ("호스트의 시작을 기다리는 중", "Waiting for the host") },
        { "lobby.status.ready", ("시작할 수 있어요", "Ready to start") },
        { "lobby.status.badCode", ("올바른 로비 코드를 입력하세요", "Enter a valid lobby code") },
        { "lobby.status.failed", ("로비에 들어가지 못했어요", "Couldn't enter the lobby") },
    };
}
