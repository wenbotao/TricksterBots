using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Pure hand features and scoring; action selection stays in each bot.</summary>
internal static class RuleBotEvaluation
{
    internal static double HandValue(IReadOnlyList<PlayingCard> hand, Contract c, ConfiguredRules rules)
    {
        return AdjustJokerValue(hand, c, rules, HandEvaluator.HandValue(hand, c, rules));
    }
    private static double AdjustJokerValue(IReadOnlyList<PlayingCard> hand, Contract c, ConfiguredRules rules, double value)
    {
        if (!c.IsMisere && c.Trump == Suit.Unknown && hand.Any(x => x.IsJoker))
        {
            value -= 1.2;
            bool high = ConfiguredRules.Text(rules.ContractOptions(c).GetProperty("joker"), "unassignedRank") == "highest";
            value += hand.Where(x => x.IsJoker).Sum(x => rules.Declarations.ContainsKey(x) ? .9 : high ? 1.2 : 0);
        }
        return value;
    }
    internal static double RetainedValue(IReadOnlyList<PlayingCard> hand, Contract c, ConfiguredRules rules)
    {
        // The same base estimate was previously recomputed while applying the Joker correction.
        double baseValue = HandEvaluator.HandValue(hand, c, rules);
        return HandEvaluator.RetainedValue(hand, c, rules) - baseValue + AdjustJokerValue(hand, c, rules, baseValue);
    }
    internal static double Danger(Observation view, PlayingCard c)
    {
        if (c.IsJoker && view.Rules is ConfiguredRules rules && ConfiguredRules.Text(rules.ContractOptions(view.Contract ?? new(0, Suit.Unknown, ContractKind.Misere)).GetProperty("joker"), "unassignedRank") == "lowest" && !rules.Declarations.ContainsKey(c))
            return 0;
        return c.IsJoker ? 30 : c.Rank;
    }
    internal static double Utility(Observation view, ConfiguredRules r, Contract contract, int declarer, int[] tricks)
    {
        var (made, delta) = r.Score(contract, declarer, tricks);
        int defending = 1 - declarer % 2;
        var cap = r.Section("scoring").GetProperty("defensiveScoreCap");
        if (cap.ValueKind != System.Text.Json.JsonValueKind.Null)
            delta[defending] = Math.Min(delta[defending], Math.Max(0, cap.GetInt32() - view.Scores[defending]));
        var scores = view.Scores.Select((x, i) => x + delta[i]).ToArray();
        var winner = r.WinningTeam(scores, declarer % 2, made);
        int team = view.Seat % 2;
        return winner.HasValue ? (winner == team ? 1 : -1) * 4.0 * r.WinScore : delta[team] - delta[1 - team];
    }
}
