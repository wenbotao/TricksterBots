using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab;
public sealed record RolloutValueV1(double Utility, double MadeProbability, double DeclarerTricks, bool Complete);
public enum OpponentPolicyV1 { Medium, Tactical, Weak, Random }
public static class ContinuationPoolV1
{
    public static OpponentPolicyV1 Infer(Observation v, int sample)
    {
        // Public evidence only. Broad, bounded mixture; no hard assumption that a bid proves a hand.
        bool aggressive = v.Auction.Any(a => a.Seat % 2 != v.Seat % 2 && a.Bid is { Tricks: 10 });
        return (sample % 6) switch { 2 or 4 => OpponentPolicyV1.Tactical, 3 => OpponentPolicyV1.Weak, 5 when aggressive => OpponentPolicyV1.Random, _ => OpponentPolicyV1.Medium };
    }
    public static IPlayer Create(OpponentPolicyV1 type, int seed) => type switch
    { OpponentPolicyV1.Tactical => new TacticianV1(), OpponentPolicyV1.Weak => new WeakBot(), OpponentPolicyV1.Random => new RandomLegalBot(seed), _ => new CompactMediumV1() };
}
public static class LeafValueV1
{
    public static RolloutValueV1 Estimate(Observation v, ValueModelV1? model = null)
    {
        var c = v.Contract!; int team = v.Declarer!.Value % 2;
        int won = c.IsMisere ? v.Players[v.Declarer.Value].Tricks : v.Players.Where(p => p.Seat % 2 == team).Sum(p => p.Tricks);
        int remaining = 10 - v.Players.Sum(p => p.Tricks);
        double strength = Math.Clamp(LegacyFeaturesV1.HandValue(v.Hand, c, v.Rules), 0, remaining);
        double expected = Math.Clamp(won + (v.Seat % 2 == team ? strength + .42 * remaining : remaining - strength - .42 * remaining), won, won + remaining);
        double made;
        if (c.IsMisere)
        {
            double risk = v.Seat == v.Declarer ? Math.Max(0, -LegacyFeaturesV1.RetainedValue(v.Hand, c, v.Rules)) : 3.5;
            made = won > 0 ? 0 : Math.Exp(-.18 * risk * remaining / 10.0);
            expected = won > 0 ? Math.Max(1, won + remaining * .15) : (1 - made) * 1.5;
        }
        else made = won >= c.Tricks ? 1 : won + remaining < c.Tricks ? 0 : 1 / (1 + Math.Exp(-(expected - c.Tricks + .5) / Math.Max(.6, Math.Sqrt(remaining * .3))));
        double? learnedUtility = null;
        if (model != null)
        {
            var prediction = model.Predict(v, "play");
            made = .6 * prediction.MadeProbability + .4 * made;
            expected = Math.Clamp(.6 * prediction.DeclarerTricks + .4 * expected, won, won + remaining);
            learnedUtility = prediction.Utility;
            if (c.IsMisere && won > 0 || !c.IsMisere && won + remaining < c.Tricks) made = 0;
            if (!c.IsMisere && won >= c.Tricks) made = 1;
        }
        double utility = Outcome(v, true, expected) * made + Outcome(v, false, expected) * (1 - made);
        if (learnedUtility.HasValue) utility = .85 * utility + .15 * learnedUtility.Value;
        return new(utility, made, expected, false);
    }
    private static double Outcome(Observation v, bool success, double expected)
    {
        var c = v.Contract!; var tricks = new int[4]; int d = v.Declarer!.Value;
        int n = c.IsMisere ? success ? 0 : Math.Max(1, (int)Math.Round(expected)) : success ? Math.Max(c.Tricks, (int)Math.Round(expected)) : Math.Min(c.Tricks - 1, (int)Math.Round(expected));
        n = Math.Clamp(n, 0, 10); tricks[d] = n; tricks[(d + 1) % 4] = 10 - n;
        return Score(v, tricks).Utility;
    }
    public static RolloutValueV1 Score(Observation v, int[] tricks)
    {
        int d = v.Declarer!.Value, team = v.Seat % 2;
        var (made, delta) = v.Rules.Score(v.Contract!, d, tricks);
        var scores = new[] { checked(v.Scores[0] + delta[0]), checked(v.Scores[1] + delta[1]) };
        int? winner = v.Rules.WinningTeam(scores, d % 2, made);
        double utility = winner.HasValue ? (winner == team ? 1 : -1) * 4.0 * v.Rules.WinScore : delta[team] - delta[1 - team];
        return new(utility, made ? 1 : 0, tricks[d] + (v.Contract!.IsMisere ? 0 : tricks[(d + 2) % 4]), true);
    }
}
