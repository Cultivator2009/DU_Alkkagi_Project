using System.Collections.Generic;
using System.Linq;

// The match's rules in a few plain lines, for the in-game menu's Rules
// (PauseMenu): how to win, what a battle of health costs, the board and
// pieces, the clocks and what ends the match.
public static class RulesSummary
{
    public static string Text(MatchSettings rules, int sides, bool teams)
    {
        return string.Join("\n", Lines(rules, sides, teams).Select(line => "· " + line));
    }

    private static IEnumerable<string> Lines(MatchSettings rules, int sides, bool teams)
    {
        if (rules.RankedMode) yield return Loc.Get("rules.ranked");
        yield return Loc.Get(teams ? "rules.winTeams" : sides > 2 ? "rules.winMulti" : "rules.win");
        if (rules.Variant == GameVariant.Health)
        {
            yield return rules.HealthRule == HealthRule.PerPiece
                ? Loc.Get("rules.healthPiece", rules.PieceHealth, HealthRuleset.MaxDamage)
                : Loc.Get("rules.healthSide", rules.SideHealth, HealthRuleset.MaxDamage, HealthRuleset.FallDamage);
            if (rules.Walled) yield return Loc.Get("rules.walls");
        }
        var board = MatchSettings.Defs[(int)MatchSettingId.BoardType].Format((int)rules.BoardType);
        var pieces = MatchSettings.Defs[(int)MatchSettingId.PieceType].Format((int)rules.PieceType);
        var counts = MatchSettings.StoneIds.Take(sides).Select(rules.Get).ToList();
        yield return counts.Distinct().Count() == 1
            ? Loc.Get("rules.board", board, pieces, Loc.Get("option.stones", counts[0]))
            : Loc.Get("rules.boardBySide", board, pieces, string.Join(" · ", counts.Select((n, side) => SideStyle.Name(side, rules.PieceType) + " " + n)));
        if (rules.SpawnMode == SpawnMode.Placement) yield return Loc.Get("rules.placement", MatchSettings.Defs[(int)MatchSettingId.PlacementStyle].Format((int)rules.PlacementStyle));
        yield return rules.TurnSeconds > 0 ? Loc.Get("rules.turn", rules.TurnSeconds) : Loc.Get("rules.turnFree");
        if (rules.Zone) yield return Loc.Get("rules.zone");
        if (rules.RoundLimit > 0) yield return Loc.Get("rules.rounds", rules.RoundLimit);
        yield return Loc.Get("rules.bothOut", MatchSettings.Defs[(int)MatchSettingId.BothOutRule].Format((int)rules.BothOutRule));
        if (!rules.AimGuide) yield return Loc.Get("rules.noGuide");
    }
}
