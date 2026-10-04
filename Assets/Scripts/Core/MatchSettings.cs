using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// What an online lobby plays for. Normal plays the standard rules (those
// that sway the balance fixed, MatchSettingDef.NormalValues; the board and
// pieces the host's) for no rating; Custom opens every rule; Ranked is
// the competitive match - every rule fixed (RankedValues), one on one, the
// best of three (RankedSeries) - and the only one that moves the rating.
// A ranked lobby only comes from the ranked quick match.
public enum MatchMode : byte
{
    Normal,
    Item,  // reserved for the item mode; not offered yet
    Custom,
    Ranked
}

// Random is a lobby or setup choice only: MatchSettings.Resolve rolls it
// before a match, so the board never sees it. It keeps its number (saved
// choices and lobby data are numbers); what came later goes after it.
public enum BoardType : byte
{
    Go,
    Janggi,  // folding board: the hinges across the middle are obstacles
    Random,
    Chess,
    Hexagon, // three sides, a flat edge each (two across from each other); not for four
    Cross    // four sides, one at the end of each arm
}

public enum PieceType : byte
{
    GoStones,
    JanggiPieces,
    Random,
    ChessPieces, // standing pieces that topple and roll
    GonggiStones // plastic filled with steel shot: dead when they knock
}

public enum SpawnMode : byte
{
    Preset,    // stones start on the layout mapped in GameScene (BoardSetup)
    Placement  // each side places its own stones before the first turn
}

public enum PlacementStyle : byte
{
    Hidden,     // both place at once; the other side's stones show at the end
    Live,       // both place at once, in full view
    Alternating // one stone each, taking turns
}

public enum BothOutRule : byte
{
    ShooterLoses,
    ShooterWins,
    Draw
}

// What a match is played for (HealthRuleset): alkkagi as it is, or a
// battle of health where knocks cost health, as in a shooting game.
public enum GameVariant : byte
{
    Classic,
    Health
}

// A battle of health's two ways (the lobby picks):
public enum HealthRule : byte
{
    PerPiece, // A: every piece has its own; one with none left breaks; a side is out with no pieces
    Side      // B: a side shares one; a side is out when it's gone (or its pieces are)
}

// Index into MatchSettings.Defs - keep the two in the same order (it's also
// the order the rows are shown in).
public enum MatchSettingId : byte
{
    Mode, // first: the other rules are checked against it
    Variant,
    HealthRule,
    PieceHealth,
    SideHealth,
    Barrier,
    BoardType,
    PieceType,
    AimGuide,
    Seats,
    Teams,
    BlackStones,
    WhiteStones,
    BlueStones,
    RedStones,
    SpawnMode,
    PlacementStyle,
    PlacementSeconds,
    TurnSeconds,
    Zone,
    RoundLimit,
    BothOutRule
}

// One lobby-level match rule: the values it may take and how to show them.
// Every setting is an int drawn from a fixed list, so the lobby/setup UI,
// lobby data, PlayerPrefs and the network message all handle them the same
// way without knowing what any single one means.
public sealed class MatchSettingDef
{
    public readonly MatchSettingId Id;
    public readonly string Key;       // lobby data and PlayerPrefs key
    public readonly string LabelKey;  // Loc key of the row label
    public readonly int[] Values;     // allowed values, in display order
    public readonly int Default;
    public readonly Func<MatchSettings, bool> IsRelevant; // greys the row out when false; null = always
    // Whether a value can be played with the other rules as they are (a
    // board only for the sides it has edges for); null = always.
    public readonly Func<MatchSettings, int, bool> IsAvailable;
    public readonly bool OnlineOnly; // a lobby rule: local games are always two at one screen
    // What Normal and Ranked allow, in display order: one value fixes the
    // rule, null leaves it open. The fallback is where the mode puts it.
    public readonly int[] NormalValues;
    public readonly int NormalFallback;
    public readonly int[] RankedValues;
    public readonly int RankedFallback;
    private readonly Func<int, string> format;

    public MatchSettingDef(MatchSettingId id, string key, string labelKey, int[] values, int defaultValue, Func<int, string> format, Func<MatchSettings, bool> isRelevant = null, bool onlineOnly = false, int[] normal = null, int? normalFallback = null, Func<MatchSettings, int, bool> isAvailable = null, int[] ranked = null, int? rankedFallback = null)
    {
        IsAvailable = isAvailable;
        Id = id;
        Key = key;
        LabelKey = labelKey;
        Values = values;
        Default = defaultValue;
        this.format = format;
        IsRelevant = isRelevant;
        OnlineOnly = onlineOnly;
        NormalValues = normal == null ? null : values.Where(normal.Contains).ToArray();
        NormalFallback = normalFallback ?? (normal != null ? normal[0] : defaultValue);
        RankedValues = ranked == null ? null : values.Where(ranked.Contains).ToArray();
        RankedFallback = rankedFallback ?? (ranked != null ? ranked[0] : defaultValue);
    }

    // What a lobby's mode allows (null: anything) and where it puts the rule.
    public int[] FixedValues(MatchMode mode) => mode == MatchMode.Ranked ? RankedValues : mode == MatchMode.Custom ? null : NormalValues;
    public int Fallback(MatchMode mode) => mode == MatchMode.Ranked ? RankedFallback : NormalFallback;

    public string Format(int value) => format(value);
}

// The rules one match is played under. Online the lobby host picks them and
// sends them with LoadGameScene, so host and guest always start the same
// match; locally they come from the setup screen. To add a rule: a
// MatchSettingId, a Defs entry, and a typed accessor below.
public sealed class MatchSettings
{
    private const string PrefsPrefix = "match.";
    private static readonly int[] StoneCounts = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    // Normal: six stones a side, as laid out, the shooter losing a both-out,
    // the edge crumbling and a round limit behind it, and a turn clock (a
    // player who stops playing can't hold the others). Board, pieces, aim
    // guide, seats and the clock's length stay the host's.
    private static readonly int[] NormalStones = { 6 };
    // Ranked: classic alkkagi to the letter - go stones or janggi pieces on
    // any board (both rolled for every game), ten a side placed one at a
    // time in turn, no aim guide, 20 seconds a turn and 20 rounds, no
    // crumbling edge, one on one.
    private static readonly int[] RankedStones = { 10 };

    public static readonly MatchSettingDef[] Defs =
    {
        // Item joins the list with the item mode. Ranked is only ever set by
        // the ranked quick match, never picked.
        new MatchSettingDef(MatchSettingId.Mode, "mode", "match.mode", new[] { (int)MatchMode.Normal, (int)MatchMode.Custom, (int)MatchMode.Ranked }, (int)MatchMode.Normal,
            v => Loc.Get("mode." + (MatchMode)v), onlineOnly: true, isAvailable: (s, v) => v != (int)MatchMode.Ranked || s.Mode == MatchMode.Ranked),
        new MatchSettingDef(MatchSettingId.Variant, "variant", "match.variant", new[] { (int)GameVariant.Classic, (int)GameVariant.Health }, (int)GameVariant.Classic,
            v => Loc.Get("variant." + (GameVariant)v), normal: new[] { (int)GameVariant.Classic }, ranked: new[] { (int)GameVariant.Classic }),
        new MatchSettingDef(MatchSettingId.HealthRule, "healthRule", "match.healthRule", new[] { (int)global::HealthRule.PerPiece, (int)global::HealthRule.Side }, (int)global::HealthRule.PerPiece,
            v => Loc.Get("healthRule." + (global::HealthRule)v), s => s.Variant == GameVariant.Health),
        new MatchSettingDef(MatchSettingId.PieceHealth, "pieceHealth", "match.pieceHealth", new[] { 50, 100, 150, 200 }, 100,
            v => Loc.Get("option.health", v), s => s.Variant == GameVariant.Health && s.HealthRule == global::HealthRule.PerPiece),
        new MatchSettingDef(MatchSettingId.SideHealth, "sideHealth", "match.sideHealth", new[] { 400, 600, 800, 1000, 1200, 1500, 1800 }, 800,
            v => Loc.Get("option.health", v), s => s.Variant == GameVariant.Health && s.HealthRule == global::HealthRule.Side),
        // Walls round the board, so nothing falls off and the knocks decide it.
        new MatchSettingDef(MatchSettingId.Barrier, "barrier", "match.barrier", new[] { 0, 1 }, 0,
            v => Loc.Get(v == 1 ? "option.on" : "option.off"), s => s.Variant == GameVariant.Health),
        new MatchSettingDef(MatchSettingId.BoardType, "board", "match.board",
            new[] { (int)global::BoardType.Go, (int)global::BoardType.Janggi, (int)global::BoardType.Chess, (int)global::BoardType.Hexagon, (int)global::BoardType.Cross, (int)global::BoardType.Random }, (int)global::BoardType.Go,
            v => Loc.Get("board." + (global::BoardType)v), isAvailable: (s, v) => BoardFits((global::BoardType)v, s.Seats),
            ranked: new[] { (int)global::BoardType.Random, (int)global::BoardType.Go, (int)global::BoardType.Janggi, (int)global::BoardType.Chess, (int)global::BoardType.Hexagon, (int)global::BoardType.Cross }),
        new MatchSettingDef(MatchSettingId.PieceType, "pieces", "match.pieces", new[] { (int)global::PieceType.GoStones, (int)global::PieceType.JanggiPieces, (int)global::PieceType.ChessPieces, (int)global::PieceType.GonggiStones, (int)global::PieceType.Random }, (int)global::PieceType.GoStones,
            v => Loc.Get("pieces." + (global::PieceType)v), ranked: new[] { (int)global::PieceType.Random, (int)global::PieceType.GoStones, (int)global::PieceType.JanggiPieces }),
        new MatchSettingDef(MatchSettingId.AimGuide, "aimGuide", "match.aimGuide", new[] { 1, 0 }, 1,
            v => Loc.Get(v == 1 ? "option.on" : "option.off"), ranked: new[] { 0 }),
        // Online, how many may join the lobby (the match is played by whoever
        // is in it when the host starts, two at least, bots included);
        // locally, the seats at this screen and the AI's (LocalOpponent).
        new MatchSettingDef(MatchSettingId.Seats, "seats", "match.seats", new[] { 2, 3, 4 }, 2,
            v => Loc.Get("option.players", v), ranked: new[] { 2 }),
        // Four as two teams of two (TeamOf): teammates sit across from each
        // other, so the turns go team to team. Colours and seats as ever.
        new MatchSettingDef(MatchSettingId.Teams, "teams", "match.teams", new[] { 0, 1 }, 0,
            v => Loc.Get(v == 1 ? "teams.twoByTwo" : "option.off"), s => s.Seats == 4, isAvailable: (s, v) => v == 0 || s.Seats == 4, ranked: new[] { 0 }),
        new MatchSettingDef(MatchSettingId.BlackStones, "blackStones", "match.blackStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), normal: NormalStones, ranked: RankedStones),
        new MatchSettingDef(MatchSettingId.WhiteStones, "whiteStones", "match.whiteStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), normal: NormalStones, ranked: RankedStones),
        new MatchSettingDef(MatchSettingId.BlueStones, "blueStones", "match.blueStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), s => s.Seats >= 3, normal: NormalStones),
        new MatchSettingDef(MatchSettingId.RedStones, "redStones", "match.redStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), s => s.Seats >= 4, normal: NormalStones),
        new MatchSettingDef(MatchSettingId.SpawnMode, "spawn", "match.spawn", new[] { (int)global::SpawnMode.Preset, (int)global::SpawnMode.Placement }, (int)global::SpawnMode.Preset,
            v => Loc.Get(v == (int)global::SpawnMode.Placement ? "spawn.placement" : "spawn.preset"), normal: new[] { (int)global::SpawnMode.Preset }, ranked: new[] { (int)global::SpawnMode.Placement }),
        new MatchSettingDef(MatchSettingId.PlacementStyle, "placement", "match.placementStyle",
            new[] { (int)global::PlacementStyle.Hidden, (int)global::PlacementStyle.Live, (int)global::PlacementStyle.Alternating }, (int)global::PlacementStyle.Hidden,
            v => Loc.Get("placement.style" + (global::PlacementStyle)v), s => s.SpawnMode == global::SpawnMode.Placement,
            normal: new[] { (int)global::PlacementStyle.Hidden }, ranked: new[] { (int)global::PlacementStyle.Alternating }),
        new MatchSettingDef(MatchSettingId.PlacementSeconds, "placementTime", "match.placementTime", new[] { 30, 45, 60, 90, 120 }, 60,
            v => Loc.Get("option.seconds", v), s => s.SpawnMode == global::SpawnMode.Placement, normal: new[] { 60 }, ranked: new[] { 60 }),
        new MatchSettingDef(MatchSettingId.TurnSeconds, "turnTime", "match.turnTime", new[] { 0, 10, 15, 20, 30, 60 }, 0,
            v => v == 0 ? Loc.Get("option.noLimit") : Loc.Get("option.seconds", v), normal: new[] { 10, 15, 20, 30, 60 }, normalFallback: 30, ranked: new[] { 20 }),
        // The board's edge crumbling in once nothing has gone out for a
        // while (TurnController), so no one can hold a match by passing.
        // Normal: always, and a round limit behind it. Ranked: the round
        // limit alone.
        new MatchSettingDef(MatchSettingId.Zone, "zone", "match.zone", new[] { 1, 0 }, 1,
            v => Loc.Get(v == 1 ? "option.on" : "option.off"), s => !s.Walled, normal: new[] { 1 }, ranked: new[] { 0 }),
        new MatchSettingDef(MatchSettingId.RoundLimit, "roundLimit", "match.roundLimit", new[] { 0, 15, 20, 30, 40 }, 0,
            v => v == 0 ? Loc.Get("option.noLimit") : Loc.Get("option.rounds", v), normal: new[] { 20 }, ranked: new[] { 20 }),
        new MatchSettingDef(MatchSettingId.BothOutRule, "bothOut", "match.bothOut",
            new[] { (int)global::BothOutRule.ShooterLoses, (int)global::BothOutRule.ShooterWins, (int)global::BothOutRule.Draw }, (int)global::BothOutRule.ShooterLoses,
            v => Loc.Get("bothOut." + (global::BothOutRule)v), normal: new[] { (int)global::BothOutRule.ShooterLoses }, ranked: new[] { (int)global::BothOutRule.ShooterLoses }),
    };

    // How the rule lists show the rules: in groups under headings, the
    // players before the board (which boards fit depends on them). Only the
    // lists' order: Defs keep theirs, which is the ids'.
    public static readonly (string titleKey, MatchSettingId[] ids)[] Groups =
    {
        ("rules.group.game", new[] { MatchSettingId.Mode, MatchSettingId.Variant, MatchSettingId.HealthRule, MatchSettingId.PieceHealth, MatchSettingId.SideHealth, MatchSettingId.Barrier }),
        ("rules.group.players", new[] { MatchSettingId.Seats, MatchSettingId.Teams }),
        ("rules.group.board", new[] { MatchSettingId.BoardType, MatchSettingId.PieceType, MatchSettingId.BlackStones, MatchSettingId.WhiteStones, MatchSettingId.BlueStones, MatchSettingId.RedStones }),
        ("rules.group.placement", new[] { MatchSettingId.SpawnMode, MatchSettingId.PlacementStyle, MatchSettingId.PlacementSeconds }),
        ("rules.group.end", new[] { MatchSettingId.TurnSeconds, MatchSettingId.RoundLimit, MatchSettingId.Zone, MatchSettingId.BothOutRule }),
        ("rules.group.assist", new[] { MatchSettingId.AimGuide }),
    };

    // The pieces' count rules, by side (StonesFor).
    public static readonly MatchSettingId[] StoneIds = { MatchSettingId.BlackStones, MatchSettingId.WhiteStones, MatchSettingId.BlueStones, MatchSettingId.RedStones };

    // The match being played (or about to be), Random rolled. Set before
    // GameScene loads.
    public static MatchSettings Current { get; set; } = new MatchSettings();

    // The rules as picked, Random unrolled: a rematch rolls again from these.
    // Kept by whoever starts matches (the host, or this screen locally).
    public static MatchSettings Picked { get; set; } = new MatchSettings();

    private readonly int[] values = new int[Defs.Length];

    public MatchSettings()
    {
        for (var i = 0; i < Defs.Length; i++) values[i] = Defs[i].Default;
    }

    public int Get(MatchSettingId id) => values[(int)id];

    // Anything outside the allowed list (an old saved value, a newer
    // client's option) falls back to the default.
    public void Set(MatchSettingId id, int value)
    {
        var def = Defs[(int)id];
        values[(int)id] = Array.IndexOf(def.Values, value) >= 0 ? value : def.Default;
    }

    public MatchMode Mode => (MatchMode)Get(MatchSettingId.Mode);
    public bool FixedMode => Mode != MatchMode.Custom; // a mode that fixes rules
    public bool RankedMode => Mode == MatchMode.Ranked;
    public GameVariant Variant => (GameVariant)Get(MatchSettingId.Variant);
    public HealthRule HealthRule => (HealthRule)Get(MatchSettingId.HealthRule);
    public int PieceHealth => Get(MatchSettingId.PieceHealth);
    public int SideHealth => Get(MatchSettingId.SideHealth);
    // Walls round the board (a battle of health's choice): nothing falls, so the edge doesn't crumble.
    public bool Walled => Variant == GameVariant.Health && Get(MatchSettingId.Barrier) == 1;
    public BoardType BoardType => (BoardType)Get(MatchSettingId.BoardType);
    public PieceType PieceType => (PieceType)Get(MatchSettingId.PieceType);
    public bool AimGuide => Get(MatchSettingId.AimGuide) == 1;
    public int Seats => Get(MatchSettingId.Seats);
    // Two teams of two: sides 0 and 2 against 1 and 3 (with four playing).
    public bool Teams => Get(MatchSettingId.Teams) == 1 && Seats == 4;
    public int TeamOf(int playerId) => Teams ? playerId % 2 : playerId;
    public int StonesFor(int playerId) => Get(playerId switch
    {
        0 => MatchSettingId.BlackStones,
        1 => MatchSettingId.WhiteStones,
        2 => MatchSettingId.BlueStones,
        _ => MatchSettingId.RedStones,
    });
    public SpawnMode SpawnMode => (SpawnMode)Get(MatchSettingId.SpawnMode);
    public PlacementStyle PlacementStyle => (PlacementStyle)Get(MatchSettingId.PlacementStyle);
    public int PlacementSeconds => Get(MatchSettingId.PlacementSeconds);
    public int TurnSeconds => Get(MatchSettingId.TurnSeconds); // 0 = no limit
    public bool Zone => Get(MatchSettingId.Zone) == 1 && !Walled;
    public int RoundLimit => Get(MatchSettingId.RoundLimit); // 0 = none
    public BothOutRule BothOutRule => (BothOutRule)Get(MatchSettingId.BothOutRule);

    // Whether a board is fair to this many sides: every side sits as the
    // others do. Three round a rectangle leave one with no one across from
    // it; four on the janggi board have the hinges across two sides' way
    // and along the others'; the hexagon has no edge for a fourth.
    public static bool BoardFits(BoardType board, int players) => board switch
    {
        BoardType.Go or BoardType.Chess => players != 3,
        BoardType.Janggi => players <= 2,
        BoardType.Hexagon => players <= 3,
        _ => true,
    };

    public MatchSettings Clone()
    {
        var copy = new MatchSettings();
        Array.Copy(values, copy.values, values.Length);
        return copy;
    }

    // ---- Modes ----

    // The values a rule may take with the others as they are.
    public int[] Available(MatchSettingDef def) => def.IsAvailable == null ? def.Values : def.Values.Where(v => def.IsAvailable(this, v)).ToArray();

    // The values a rule may take in this lobby's mode, with the others as they are.
    public int[] Allowed(MatchSettingDef def)
    {
        var values = def.FixedValues(Mode) ?? def.Values;
        return def.IsAvailable == null ? values : values.Where(v => def.IsAvailable(this, v)).ToArray();
    }

    // Whether the result moves ratings: Ranked, played by its rules. A host
    // sending anything else plays unrated on every machine.
    public bool Rated => RankedMode && Defs.All(def => Array.IndexOf(Allowed(def), Get(def.Id)) >= 0);

    // A ranked lobby's rules: every one where Ranked puts it.
    public static MatchSettings ForRanked()
    {
        var settings = new MatchSettings();
        settings.Set(MatchSettingId.Mode, (int)MatchMode.Ranked);
        foreach (var def in Defs)
            if (def.RankedValues != null) settings.Set(def.Id, def.RankedFallback);
        return settings;
    }

    // Brings the rules a mode fixes back inside it, e.g. on switching a
    // lobby from Custom to Normal, and any rule the others no longer allow
    // back to one they do.
    public void ApplyMode() => Normalize(modes: true);

    // modes: the lobby's (a mode may fix rules); the local setup has none.
    public void Normalize(bool modes)
    {
        foreach (var def in Defs)
        {
            var allowed = modes ? Allowed(def) : Available(def);
            if (allowed.Length == 0 || Array.IndexOf(allowed, Get(def.Id)) >= 0) continue;
            var fallback = def.Fallback(Mode);
            Set(def.Id, Array.IndexOf(allowed, fallback) >= 0 ? fallback : allowed[0]);
        }
    }

    // A copy with Random rolled. The host rolls once per match and sends the
    // result, so every machine plays the same board. players: how many
    // actually play (a lobby needn't be full), when it's not Seats.
    public MatchSettings Resolve(int players = 0)
    {
        var copy = Clone();
        var playing = Clone();
        if (players > 0) playing.values[(int)MatchSettingId.Seats] = players;
        if (BoardType == BoardType.Random) copy.Set(MatchSettingId.BoardType, playing.Roll(MatchSettingId.BoardType, (int)BoardType.Random));
        if (PieceType == PieceType.Random) copy.Set(MatchSettingId.PieceType, playing.Roll(MatchSettingId.PieceType, (int)PieceType.Random));
        return copy;
    }

    // Any of the rule's values the mode and the others allow but Random
    // itself (Ranked: go stones or janggi pieces).
    private int Roll(MatchSettingId id, int random)
    {
        var choices = Allowed(Defs[(int)id]).Where(v => v != random).ToArray();
        return choices[UnityEngine.Random.Range(0, choices.Length)];
    }

    // ---- Storage ----

    // The last rules this player picked, as the starting point for the next
    // local setup or hosted lobby.
    public static MatchSettings LoadPrefs()
    {
        var settings = new MatchSettings();
        foreach (var def in Defs) settings.Set(def.Id, PlayerPrefs.GetInt(PrefsPrefix + def.Key, def.Default));
        return settings;
    }

    // lobby: a ranked lobby's rules leave out what its mode sets, so hosting
    // one doesn't overwrite the player's own picks (the local setup starts
    // from these too).
    public void SavePrefs(bool lobby = false)
    {
        if (RankedMode) return; // the ranked quick match's, not this player's picks
        foreach (var def in Defs)
            if (!lobby || def.FixedValues(Mode) == null) PlayerPrefs.SetInt(PrefsPrefix + def.Key, Get(def.Id));
    }

    // Lobby data is string key/value pairs; keys the lobby doesn't have keep
    // their defaults.
    public IEnumerable<KeyValuePair<string, string>> ToPairs()
    {
        foreach (var def in Defs) yield return new KeyValuePair<string, string>(def.Key, Get(def.Id).ToString());
    }

    public static MatchSettings FromPairs(Func<string, string> lookup)
    {
        var settings = new MatchSettings();
        foreach (var def in Defs)
            if (int.TryParse(lookup(def.Key), out var value)) settings.Set(def.Id, value);
        return settings;
    }

    // Id/value pairs, so a peer ignores ids it doesn't know rather than
    // misreading everything after them.
    public void Write(NetWriter writer)
    {
        writer.UInt((uint)Defs.Length);
        foreach (var def in Defs)
        {
            writer.Byte((byte)def.Id);
            writer.Int(Get(def.Id));
        }
    }

    public static MatchSettings Read(NetReader reader)
    {
        var settings = new MatchSettings();
        var count = reader.UInt();
        for (var i = 0; i < count && !reader.AtEnd; i++)
        {
            var id = reader.Byte();
            var value = reader.Int();
            if (id < Defs.Length) settings.Set((MatchSettingId)id, value);
        }
        return settings;
    }
}
