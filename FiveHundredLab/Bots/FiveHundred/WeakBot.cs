using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Beginner policy: local estimates and immediate tactics, without sampled search.</summary>
public sealed class WeakBot : FiveHundredRuleBot
{
    private readonly DefaultPolicy defaultPolicy = new();
    public WeakBot(int seed = 1, Rules? rules = null) : base("weak-rules-v1", seed, rules) { }
    public static WeakBot FromConfiguration(string? path = null, int seed = 1) => new(seed, RuleConfiguration.Load(path));
    public override Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        return rules == null ? defaultPolicy.Bid(view, legal) : BidConfigured(view, rules, legal);
    }
    public override IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return defaultPolicy.Discard(view, count);
        if (count == 0)
            return Array.Empty<PlayingCard>();
        return view.Hand.OrderBy(c => view.Contract!.IsMisere ? -RuleBotEvaluation.Danger(view, c) : c.IsJoker ? (RuleBotEvaluation.Danger(view, c) > 0 ? 100 : 0) : rules.EffectiveSuit(c, view.Contract) == view.Contract.Trump ? 50 + rules.Rank(c, view.Contract) : c.Rank).Take(count).ToArray();
    }
    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return defaultPolicy.Play(view, legal);
        return legal.Count == 1 ? legal[0] : PlayConfigured(view, rules, legal);
    }
    private Contract? BidConfigured(Observation view, ConfiguredRules rules, IReadOnlyList<Contract?> legal)
    {
        if (rules.PassIsPermanent && view.Declarer == (view.Seat + 2) % 4 && view.Contract is { } current && view.Scores[view.Seat % 2] + rules.Value(current) >= rules.WinScore && view.Players.Where(p => p.Seat % 2 != view.Seat % 2).All(p => view.Auction.Any(a => a.Seat == p.Seat && a.Bid == null)))
            return null;
        Contract? best = null;
        double bestUtility = 0;
        foreach (var bid in legal.Where(b => b != null).Select(b => b!))
        {
            if (bid.Kind == ContractKind.Inkle)
                continue;
            if (bid.IsMisere)
                continue;
            double estimate = RuleBotEvaluation.HandValue(view.Hand, bid, rules) + 1.2;
            var support = view.Auction.LastOrDefault(a => a.Seat == (view.Seat + 2) % 4 && a.Bid?.Kind == ContractKind.Normal)?.Bid;
            if (support?.Trump == bid.Trump)
                estimate += .8;
            if (estimate < bid.Tricks + .6)
                continue;
            double probability = Math.Clamp(.65 + (estimate - bid.Tricks) * .12, .1, .97);
            var success = new int[4];
            success[view.Seat] = bid.Tricks;
            success[(view.Seat + 1) % 4] = 10 - bid.Tricks;
            var failure = new int[4];
            failure[view.Seat] = bid.Tricks - 1;
            failure[(view.Seat + 1) % 4] = 11 - bid.Tricks;
            double utility = probability * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, success) + (1 - probability) * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, failure);
            if (utility > bestUtility)
            {
                bestUtility = utility;
                best = bid;
            }
        }
        if (best != null)
            return best;
        var inkle = legal.Where(c => c?.Kind == ContractKind.Inkle).Select(c => c!).OrderByDescending(c => view.Hand.Count(x => rules.EffectiveSuit(x, c) == c.Trump)).FirstOrDefault();
        return inkle != null && view.Hand.Count(c => rules.EffectiveSuit(c, inkle) == inkle.Trump) >= 4 ? inkle : null;
    }
    private static PlayAction PlayConfigured(Observation view, ConfiguredRules r, IReadOnlyList<PlayAction> legal)
    {
        var c = view.Contract!;
        var sorted = legal.OrderBy(p => RuleBotEvaluation.Danger(view, p.Card)).ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit).ToArray();
        if (view.Trick.Count == 0)
        {
            if (c.IsMisere)
                return view.Declarer != view.Seat && view.Declarer % 2 == view.Seat % 2 ? sorted[^1] : sorted[0];
            return sorted[^1];
        }
        int Winner(PlayAction p) => r.Winner(view.Trick.Append(new PublicPlay(view.Seat, p)).ToArray(), c);
        if (c.IsMisere)
        {
            if (view.Declarer == view.Seat)
            {
                var duck = sorted.Where(p => Winner(p) != view.Seat).ToArray();
                return duck.Length > 0 ? duck[^1] : sorted[0];
            }
            if (view.Declarer % 2 == view.Seat % 2)
            {
                var cover = sorted.Where(p => Winner(p) != view.Declarer).ToArray();
                return cover.Length > 0 ? cover[^1] : sorted[0];
            }
            var bust = sorted.Where(p => Winner(p) == view.Declarer).ToArray();
            if (bust.Length > 0)
                return bust[0];
            return sorted[0];
        }
        if (r.Winner(view.Trick, c) % 2 == view.Seat % 2)
            return sorted.FirstOrDefault(p => Winner(p) % 2 == view.Seat % 2) ?? sorted[0];
        return sorted.FirstOrDefault(p => Winner(p) == view.Seat) ?? sorted[0];
    }
    private sealed class DefaultPolicy : IPlayer
    {
        public string Name => "weak-v1";
        public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
        {
            var best = legal.Where(c => c is { IsMisere: false, Tricks: <= 7 } && c.Trump != Suit.Unknown)
                .Where(c => view.Declarer != (view.Seat + 2) % 4 || view.Contract?.Trump != c!.Trump)
                .Select(c => (Contract: c!, Strength: Estimate(view.Hand, c!, view.Rules)))
                .Where(x => x.Strength >= x.Contract.Tricks).OrderByDescending(x => x.Strength)
                .ThenBy(x => view.Rules.Order(x.Contract)).FirstOrDefault();
            return best.Contract;
        }
        private static double Estimate(IReadOnlyList<PlayingCard> hand, Contract c, Rules r)
        {
            int trumps = hand.Count(x => r.EffectiveSuit(x, c) == c.Trump);
            return 2 + .55 * trumps + hand.Sum(x => x.IsJoker ? .8 : r.Rank(x, c) >= 98 ? .7
                : x.Rank == 14 && r.EffectiveSuit(x, c) != c.Trump ? .65 : x.Rank == 13 ? .15 : 0);
        }
        public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
        {
            var c = view.Contract!;
            var r = view.Rules;
            return view.Hand.OrderBy(x => c.IsMisere ? -r.Rank(x, c)
                : x.IsJoker ? 1000 : c.Trump != Suit.Unknown && r.EffectiveSuit(x, c) == c.Trump ? 100 + r.Rank(x, c) : r.Rank(x, c)).Take(count).ToArray();
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        {
            var c = view.Contract!;
            var r = view.Rules;
            var ordered = legal.OrderBy(p => p.Card.IsJoker ? 1000 : c.Trump != Suit.Unknown && r.EffectiveSuit(p.Card, c) == c.Trump ? 100 + r.Rank(p.Card, c) : r.Rank(p.Card, c)).ToArray();
            if (view.Trick.Count == 0)
                return c.IsMisere ? ordered[0] : ordered[^1];
            int taking = r.Winner(view.Trick, c);
            if (c.IsMisere)
                return ordered[0];
            if (taking % 2 == view.Seat % 2)
                return ordered[0];
            return ordered.FirstOrDefault(p => r.Winner(view.Trick.Append(new PublicPlay(view.Seat, p)).ToArray(), c) == view.Seat) ?? ordered[0];
        }
    }
}
