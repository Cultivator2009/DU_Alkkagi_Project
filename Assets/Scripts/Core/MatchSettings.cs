using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum BoardType : byte
{
    Go,
    Janggi // folding board: the hinges across the middle are obstacles
}

public enum PieceType : byte
{
    GoStones,
    JanggiPieces
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
    BoardType,
    PieceType,
    AimGuide,
    BlackStones,
    WhiteStones,
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
    private readonly Func<int, string> format;

    public MatchSettingDef(MatchSettingId id, string key, string labelKey, int[] values, int defaultValue, Func<int, string> format, Func<MatchSettings, bool> isRelevant = null)
    {
        Id = id;
        Key = key;
        LabelKey = labelKey;
        Values = values;
        Default = defaultValue;
        this.format = format;
        IsRelevant = isRelevant;
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

    public static readonly MatchSettingDef[] Defs =
    {
        new MatchSettingDef(MatchSettingId.BoardType, "board", "match.board", new[] { (int)global::BoardType.Go, (int)global::BoardType.Janggi }, (int)global::BoardType.Go,
            v => Loc.Get("board." + (global::BoardType)v)),
        new MatchSettingDef(MatchSettingId.PieceType, "pieces", "match.pieces", new[] { (int)global::PieceType.GoStones, (int)global::PieceType.JanggiPieces }, (int)global::PieceType.GoStones,
            v => Loc.Get("pieces." + (global::PieceType)v)),
        new MatchSettingDef(MatchSettingId.AimGuide, "aimGuide", "match.aimGuide", new[] { 1, 0 }, 1,
            v => Loc.Get(v == 1 ? "option.on" : "option.off")),
        new MatchSettingDef(MatchSettingId.BlackStones, "blackStones", "match.blackStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v)),
        new MatchSettingDef(MatchSettingId.WhiteStones, "whiteStones", "match.whiteStones", StoneCounts, 6,
            v => Loc.Get("option.stones", v)),
        new MatchSettingDef(MatchSettingId.SpawnMode, "spawn", "match.spawn", new[] { (int)global::SpawnMode.Preset, (int)global::SpawnMode.Placement }, (int)global::SpawnMode.Preset,
            v => Loc.Get(v == (int)global::SpawnMode.Placement ? "spawn.placement" : "spawn.preset")),
        new MatchSettingDef(MatchSettingId.PlacementStyle, "placement", "match.placementStyle",
            new[] { (int)global::PlacementStyle.Hidden, (int)global::PlacementStyle.Live, (int)global::PlacementStyle.Alternating }, (int)global::PlacementStyle.Hidden,
            v => Loc.Get("placement.style" + (global::PlacementStyle)v), s => s.SpawnMode == global::SpawnMode.Placement),
        new MatchSettingDef(MatchSettingId.PlacementSeconds, "placementTime", "match.placementTime", new[] { 30, 45, 60, 90, 120 }, 60,
            v => Loc.Get("option.seconds", v), s => s.SpawnMode == global::SpawnMode.Placement),
        new MatchSettingDef(MatchSettingId.TurnSeconds, "turnTime", "match.turnTime", new[] { 0, 10, 15, 20, 30, 60 }, 0,
            v => v == 0 ? Loc.Get("option.noLimit") : Loc.Get("option.seconds", v)),
        new MatchSettingDef(MatchSettingId.BothOutRule, "bothOut", "match.bothOut",
            new[] { (int)global::BothOutRule.ShooterLoses, (int)global::BothOutRule.ShooterWins, (int)global::BothOutRule.Draw }, (int)global::BothOutRule.ShooterLoses,
            v => Loc.Get("bothOut." + (global::BothOutRule)v)),
    };

    // The match being played (or about to be). Set before GameScene loads.
    public static MatchSettings Current { get; set; } = new MatchSettings();

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

    public BoardType BoardType => (BoardType)Get(MatchSettingId.BoardType);
    public PieceType PieceType => (PieceType)Get(MatchSettingId.PieceType);
    public bool AimGuide => Get(MatchSettingId.AimGuide) == 1;
    public int StonesFor(int playerId) => Get(playerId == 0 ? MatchSettingId.BlackStones : MatchSettingId.WhiteStones);
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

    // ---- Storage ----

    // The last rules this player picked, as the starting point for the next
    // local setup or hosted lobby.
    public static MatchSettings LoadPrefs()
    {
        var settings = new MatchSettings();
        foreach (var def in Defs) settings.Set(def.Id, PlayerPrefs.GetInt(PrefsPrefix + def.Key, def.Default));
        return settings;
    }

    public void SavePrefs()
    {
        foreach (var def in Defs) PlayerPrefs.SetInt(PrefsPrefix + def.Key, Get(def.Id));
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
