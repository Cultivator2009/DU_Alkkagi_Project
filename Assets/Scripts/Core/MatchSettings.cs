using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// What an online lobby plays for. The ranked modes (Normal, and Item once it
// exists) fix the rules that sway the balance (MatchSettingDef.RankedValues)
// and move everyone's rating; Custom opens every rule and isn't rated.
public enum MatchMode : byte
{
    Normal,
    Item,  // reserved for the item mode; not offered yet
    Custom
}

// Random is a lobby or setup choice only: MatchSettings.Resolve rolls it
// before a match, so the board never sees it.
public enum BoardType : byte
{
    Go,
    Janggi, // folding board: the hinges across the middle are obstacles
    Random
}

public enum PieceType : byte
{
    GoStones,
    JanggiPieces,
    Random
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

// Index into MatchSettings.Defs - keep the two in the same order (it's also
// the order the rows are shown in).
public enum MatchSettingId : byte
{
    Mode, // first: the other rules are checked against it
    BoardType,
    PieceType,
    AimGuide,
    Seats,
    BlackStones,
    WhiteStones,
    BlueStones,
    RedStones,
    SpawnMode,
    PlacementStyle,
    PlacementSeconds,
    TurnSeconds,
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
    public readonly bool OnlineOnly; // a lobby rule: local games are always two at one screen
    // What the ranked modes allow, in display order: one value fixes the
    // rule, null leaves it open. RankedFallback is where they put it.
    public readonly int[] RankedValues;
    public readonly int RankedFallback;
    private readonly Func<int, string> format;

    public MatchSettingDef(MatchSettingId id, string key, string labelKey, int[] values, int defaultValue, Func<int, string> format, Func<MatchSettings, bool> isRelevant = null, bool onlineOnly = false, int[] ranked = null, int? rankedFallback = null)
    {
        Id = id;
        Key = key;
        LabelKey = labelKey;
        Values = values;
        Default = defaultValue;
        this.format = format;
        IsRelevant = isRelevant;
        OnlineOnly = onlineOnly;
        RankedValues = ranked == null ? null : values.Where(ranked.Contains).ToArray();
        RankedFallback = rankedFallback ?? (ranked != null ? ranked[0] : defaultValue);
    }

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
    // Ranked: six stones a side, as laid out, the shooter losing a both-out,
    // and a turn clock (a player who stops playing can't hold the others,
    // who would lose rating by leaving). Board, pieces, aim guide, seats and
    // the clock's length stay the host's.
    private static readonly int[] RankedStones = { 6 };

    public static readonly MatchSettingDef[] Defs =
    {
        // Item joins the list with the item mode.
        new MatchSettingDef(MatchSettingId.Mode, "mode", "match.mode", new[] { (int)MatchMode.Normal, (int)MatchMode.Custom }, (int)MatchMode.Normal,
            v => Loc.Get("mode." + (MatchMode)v), onlineOnly: true),
        new MatchSettingDef(MatchSettingId.BoardType, "board", "match.board", new[] { (int)global::BoardType.Go, (int)global::BoardType.Janggi, (int)global::BoardType.Random }, (int)global::BoardType.Go,
            v => Loc.Get("board." + (global::BoardType)v)),
        new MatchSettingDef(MatchSettingId.PieceType, "pieces", "match.pieces", new[] { (int)global::PieceType.GoStones, (int)global::PieceType.JanggiPieces, (int)global::PieceType.Random }, (int)global::PieceType.GoStones,
            v => Loc.Get("pieces." + (global::PieceType)v)),
        new MatchSettingDef(MatchSettingId.AimGuide, "aimGuide", "match.aimGuide", new[] { 1, 0 }, 1,
            v => Loc.Get(v == 1 ? "option.on" : "option.off")),
        // How many may join the lobby; the match is played by whoever is in
        // it when the host starts, two at least.
        new MatchSettingDef(MatchSettingId.Seats, "seats", "match.seats", new[] { 2, 3, 4 }, 2,
            v => Loc.Get("option.players", v), onlineOnly: true),
        new MatchSettingDef(MatchSettingId.BlackStones, "blackStones", "match.blackStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), ranked: RankedStones),
        new MatchSettingDef(MatchSettingId.WhiteStones, "whiteStones", "match.whiteStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), ranked: RankedStones),
        new MatchSettingDef(MatchSettingId.BlueStones, "blueStones", "match.blueStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), s => s.Seats >= 3, onlineOnly: true, ranked: RankedStones),
        new MatchSettingDef(MatchSettingId.RedStones, "redStones", "match.redStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v), s => s.Seats >= 4, onlineOnly: true, ranked: RankedStones),
        new MatchSettingDef(MatchSettingId.SpawnMode, "spawn", "match.spawn", new[] { (int)global::SpawnMode.Preset, (int)global::SpawnMode.Placement }, (int)global::SpawnMode.Preset,
            v => Loc.Get(v == (int)global::SpawnMode.Placement ? "spawn.placement" : "spawn.preset"), ranked: new[] { (int)global::SpawnMode.Preset }),
        new MatchSettingDef(MatchSettingId.PlacementStyle, "placement", "match.placementStyle",
            new[] { (int)global::PlacementStyle.Hidden, (int)global::PlacementStyle.Live, (int)global::PlacementStyle.Alternating }, (int)global::PlacementStyle.Hidden,
            v => Loc.Get("placement.style" + (global::PlacementStyle)v), s => s.SpawnMode == global::SpawnMode.Placement, ranked: new[] { (int)global::PlacementStyle.Hidden }),
        new MatchSettingDef(MatchSettingId.PlacementSeconds, "placementTime", "match.placementTime", new[] { 30, 45, 60, 90, 120 }, 60,
            v => Loc.Get("option.seconds", v), s => s.SpawnMode == global::SpawnMode.Placement, ranked: new[] { 60 }),
        new MatchSettingDef(MatchSettingId.TurnSeconds, "turnTime", "match.turnTime", new[] { 0, 10, 15, 20, 30, 60 }, 0,
            v => v == 0 ? Loc.Get("option.noLimit") : Loc.Get("option.seconds", v), ranked: new[] { 10, 15, 20, 30, 60 }, rankedFallback: 30),
        new MatchSettingDef(MatchSettingId.BothOutRule, "bothOut", "match.bothOut",
            new[] { (int)global::BothOutRule.ShooterLoses, (int)global::BothOutRule.ShooterWins, (int)global::BothOutRule.Draw }, (int)global::BothOutRule.ShooterLoses,
            v => Loc.Get("bothOut." + (global::BothOutRule)v), ranked: new[] { (int)global::BothOutRule.ShooterLoses }),
    };

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
    public bool RankedMode => Mode != MatchMode.Custom;
    public BoardType BoardType => (BoardType)Get(MatchSettingId.BoardType);
    public PieceType PieceType => (PieceType)Get(MatchSettingId.PieceType);
    public bool AimGuide => Get(MatchSettingId.AimGuide) == 1;
    public int Seats => Get(MatchSettingId.Seats);
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
    public BothOutRule BothOutRule => (BothOutRule)Get(MatchSettingId.BothOutRule);

    public MatchSettings Clone()
    {
        var copy = new MatchSettings();
        Array.Copy(values, copy.values, values.Length);
        return copy;
    }

    // ---- Modes ----

    // The values a rule may take in this lobby's mode.
    public int[] Allowed(MatchSettingDef def) => RankedMode && def.RankedValues != null ? def.RankedValues : def.Values;

    // Whether the result moves ratings: a ranked mode, played by its rules. A
    // host sending anything else plays unrated on every machine.
    public bool Rated => RankedMode && Defs.All(def => Array.IndexOf(Allowed(def), Get(def.Id)) >= 0);

    // Brings the rules a ranked mode fixes back inside it, e.g. on switching
    // a lobby from Custom to Normal.
    public void ApplyMode()
    {
        foreach (var def in Defs)
            if (Array.IndexOf(Allowed(def), Get(def.Id)) < 0) Set(def.Id, def.RankedFallback);
    }

    // A copy with Random rolled. The host rolls once per match and sends the
    // result, so every machine plays the same board.
    public MatchSettings Resolve()
    {
        var copy = Clone();
        if (BoardType == BoardType.Random) copy.Set(MatchSettingId.BoardType, UnityEngine.Random.Range((int)BoardType.Go, (int)BoardType.Random));
        if (PieceType == PieceType.Random) copy.Set(MatchSettingId.PieceType, UnityEngine.Random.Range((int)PieceType.GoStones, (int)PieceType.Random));
        return copy;
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
        foreach (var def in Defs)
            if (!lobby || !RankedMode || def.RankedValues == null) PlayerPrefs.SetInt(PrefsPrefix + def.Key, Get(def.Id));
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
    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)Defs.Length);
        foreach (var def in Defs)
        {
            writer.Write((byte)def.Id);
            writer.Write(Get(def.Id));
        }
    }

    public static MatchSettings Read(BinaryReader reader)
    {
        var settings = new MatchSettings();
        var count = reader.ReadByte();
        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadByte();
            var value = reader.ReadInt32();
            if (id < Defs.Length) settings.Set((MatchSettingId)id, value);
        }
        return settings;
    }
}
