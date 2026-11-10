using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred.V11;

/// <summary>Speed-first decisions: baseline tactics, small candidates and bounded continuations.</summary>
public sealed class FastBot : FiveHundredRuleBot
{
    private const int SearchSamples = 4;
    private readonly DefaultPolicy defaultPolicy;
    internal sealed record DefaultSearchReport(int Candidates, int SamplesUsed, int Rollouts, bool Direct, string Phase);
    internal DefaultSearchReport? DefaultSearch => defaultPolicy.LastSearch;
    public FastBot(int seed = 1, Rules? rules = null) : base("fast-rules-v1", seed, rules) => defaultPolicy = new(seed);
    public static FastBot FromConfiguration(string? path = null, int seed = 1) => new(seed, RuleConfiguration.Load(path));
    // Solo declaration is deliberately conservative; the partner will stop playing.
    public override bool DeclareGrandSlam(Observation view)
    {
        var rules=RequireMatchingRules(view);
        if(rules==null||view.Contract is not {Kind:ContractKind.Normal} contract)return false;
        bool controls=view.Hand.Any(c=>c.IsJoker) && (contract.Trump==Suit.Unknown ? view.Hand.Count(c=>c.Rank==14)>=3 : view.Hand.Count(c=>rules.EffectiveSuit(c,contract)==contract.Trump)>=7);
        return controls && RuleBotEvaluation.HandValue(view.Hand,contract,rules)>=10.2;
    }
    public override Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        return LegalBid(rules == null ? defaultPolicy.Bid(view, legal) : BidConfigured(view, rules, legal), legal);
    }
    public override IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return defaultPolicy.Discard(view, count);
        if (count == 0)
            return Array.Empty<PlayingCard>();
        var choices = DiscardCandidates(view, rules, count, 4);
        return SelectByRollout(view, rules, choices, (world, move) => VariantRollout.Discard(view, rules, world, move));
    }
    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return defaultPolicy.Play(view, legal);
        if (legal.Count == 1)
            return legal[0];
        var baseline = MediumBot.PlayConfiguredTactics(view, rules, legal);
        var choices = legal.OrderBy(p => p == baseline ? 0 : 1).Take(4).ToArray();
        return SelectByRollout(view, rules, choices, (world, move) => VariantRollout.Play(view, rules, world, move));
    }
    // Estimate contract returns under the active scoring rules; Inkle never becomes a contract.
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
            {
                double risk = view.Hand.Sum(c => RuleBotEvaluation.Danger(view, c) / (1 + view.Hand.Count(x => x.Suit == c.Suit && RuleBotEvaluation.Danger(view, x) < RuleBotEvaluation.Danger(view, c))));
                if (risk > 27)
                    continue;
                double p = Math.Clamp(.85 - risk / 100, .05, .95);
                var made = new int[4];
                made[(view.Seat + 1) % 4] = 10;
                var failed = new int[4];
                failed[view.Seat] = 1;
                failed[(view.Seat + 1) % 4] = 9;
                double misereUtility = p * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, made) + (1 - p) * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, failed);
                if (misereUtility > bestUtility)
                {
                    bestUtility = misereUtility;
                    best = bid;
                }
                continue;
            }
            double estimate = RuleBotEvaluation.HandValue(view.Hand, bid, rules) + 2.1;
            var support = view.Auction.LastOrDefault(a => a.Seat == (view.Seat + 2) % 4 && a.Bid?.Kind == ContractKind.Normal)?.Bid;
            if (support?.Trump == bid.Trump)
                estimate += .8;
            if (estimate < bid.Tricks)
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
    private static PlayingCard[][] DiscardCandidates(Observation view, ConfiguredRules rules, int count, int limit)
    {
        var danger = view.Hand.ToDictionary(card => card, card => RuleBotEvaluation.Danger(view, card));
        double Value(PlayingCard[] discard)
        {
            var kept = view.Hand.Except(discard).ToArray();
            return view.Contract!.IsMisere ? -kept.Sum(c => danger[c] / Math.Max(1, kept.Count(x => x.Suit == c.Suit && danger[x] <= danger[c]))) : RuleBotEvaluation.RetainedValue(kept, view.Contract, rules);
        }
        return HandEvaluator.DiscardSets(view.Hand, count).OrderByDescending(Value).Take(limit).ToArray();
    }

    private T SelectByRollout<T>(Observation view, ConfiguredRules rules, T[] choices, Func<SampledDeal, T, double> evaluate)
    {
        if (choices.Length == 1)
            return choices[0];
        int count = SearchSamples;
        // All candidates share the same hidden worlds; first-candidate ties remain stable.
        var worlds = VariantSamples.Sample(view, rules, count, random);
        var values = new double[choices.Length];
        foreach (var world in worlds)
            for (int i = 0; i < choices.Length; i++)
            {
                values[i] += evaluate(world, choices[i]);
                LastRollouts++;
            }
        return choices[Array.IndexOf(values, values.Max())];
    }

    // Historical default policy retains its phase-dependent budgets and weighted evidence threshold.
    private sealed class DefaultPolicy(int seed) : IPlayer
    {
        private readonly MediumBot.VisibilityAdapter medium = new();
        private readonly SeededRandom random = new(seed);
        public string Name => "visibility-fast-v8";
        public DefaultSearchReport? LastSearch
        {
            get; private set;
        }

        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal)
        {
            LastSearch = new(1, 0, 0, true, "bid");
            return medium.Bid(v, legal);
        }
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count)
        {
            var baseline = medium.Discard(v, count).ToArray();
            var choices = HandEvaluator.DiscardSets(v.Hand, count)
                .OrderByDescending(d => HandEvaluator.RetainedValue(v.Hand.Except(d).ToArray(), v.Contract!, v.Rules)).Take(3).ToList();
            choices.RemoveAll(d => d.ToHashSet().SetEquals(baseline));
            choices.Insert(0, baseline);
            return Evaluate(v, choices, baseline, 4, (m, d) => m.Discard(d), "discard");
        }
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal)
        {
            var baseline = medium.Play(v, legal);
            if (legal.Count == 1)
            {
                LastSearch = new(1, 0, 0, true, "play");
                return baseline;
            }
            bool last = v.Trick.Count == (v.Contract!.IsMisere ? 2 : 3);
            var choices = (last ? Pruning.LastSeatChoices(v, legal, baseline) : Pruning.Representatives(v, legal, baseline)).ToList();
            choices.Remove(baseline);
            choices.Insert(0, baseline);
            return Evaluate(v, choices, baseline, last ? 2 : v.Hand.Count >= 7 ? 4 : 8, (m, p) => m.Play(p), "play");
        }
        private T Evaluate<T>(Observation v, IReadOnlyList<T> choices, T baseline, int count, Func<DefaultRollout, T, double> evaluate, string phase)
        {
            if (choices.Count < 2)
            {
                LastSearch = new(1, 0, 0, true, phase);
                return baseline;
            }
            var worlds = BeliefSampler.Sample(v, count, random);
            var weights = worlds.Select(w => w.Weight).ToArray();
            var original = worlds.Select(w => evaluate(new(v, w, () => new MediumBot.VisibilityAdapter()), baseline)).ToArray();
            T best = baseline;
            double improvement = 0;
            foreach (var choice in choices.Skip(1))
            {
                var differences = worlds.Select((w, i) => evaluate(new(v, w, () => new MediumBot.VisibilityAdapter()), choice) - original[i]).ToArray();
                var evidence = RiskEstimate.Evaluate(differences, weights);
                if (evidence.Improvement > improvement)
                {
                    improvement = evidence.Improvement;
                    best = choice;
                }
            }
            LastSearch = new(choices.Count, count, choices.Count * count, false, phase);
            return best;
        }
    }
    private static class Pruning
    {
        internal static int WinnerAfter(Observation v, PlayAction move) =>
            CardTableV1.Get(v.Rules, v.Contract!).Winner(v.Trick.Append(new PublicPlay(v.Seat, move)).ToArray(), v.Contract!);

        internal static IReadOnlyList<PlayAction> Representatives(Observation v, IReadOnlyList<PlayAction> legal, PlayAction baseline)
        {
            if (v.Contract!.IsMisere)
                return legal;
            var frame = new CardFrameV1(v);
            ulong barriers = frame.Unseen;
            foreach (var played in v.Trick)
                barriers |= frame.Table.Bit(played.Action.Card);
            var groups = new List<List<PlayAction>>();
            foreach (var move in legal.OrderBy(p => frame.Table.Suit(p.Card)).ThenBy(p => frame.Table.Strength(p.Card)))
            {
                var group = groups.LastOrDefault();
                bool merge = group != null && !move.Card.IsJoker && !group[^1].Card.IsJoker && frame.Table.Suit(group[^1].Card) == frame.Table.Suit(move.Card);
                if (merge)
                {
                    int lo = frame.Table.Strength(group![^1].Card), hi = frame.Table.Strength(move.Card);
                    merge = !frame.Table.Deck.Any(c => (barriers & frame.Table.Bit(c)) != 0 && frame.Table.Suit(c) == frame.Table.Suit(move.Card) && frame.Table.Strength(c) > lo && frame.Table.Strength(c) < hi);
                    if (merge && v.Trick.Count > 0)
                        merge = WinnerAfter(v, group[^1]) == WinnerAfter(v, move);
                }
                if (merge)
                    group!.Add(move);
                else
                    groups.Add([move]);
            }
            return groups.Select(g => g.Contains(baseline) ? baseline : g[0]).ToArray();
        }

        internal static IReadOnlyList<PlayAction> LastSeatChoices(Observation v, IReadOnlyList<PlayAction> legal, PlayAction baseline)
        {
            // Retain the cheap specialist in Misere, and never search a forced move.
            if (v.Contract!.IsMisere || v.Hand.Count <= 1 || legal.Count <= 1)
                return [baseline];
            var frame = new CardFrameV1(v);
            int declaringTeam = v.Declarer!.Value % 2;
            int won = v.Players.Where(p => p.Seat % 2 == declaringTeam).Sum(p => p.Tricks);
            int needed = v.Contract.Tricks - won;
            bool close = needed is > 0 and <= 2 || v.Hand.Count <= 3;
            bool entry = v.Hand.GroupBy(frame.Table.Suit).Any(g => g.Key != (int)v.Contract.Trump && g.Count() >= 3 && g.Count(frame.Boss) >= 2);
            if (!close && !entry)
                return [baseline];
            int winner = WinnerAfter(v, baseline);
            double Cost(PlayAction p) => p.Card.IsJoker ? 1000 : frame.Table.Suit(p.Card) == (int)v.Contract.Trump ? 200 + frame.Table.Strength(p.Card) : frame.Table.Strength(p.Card);
            var alternative = legal.Where(p => WinnerAfter(v, p) != winner).OrderBy(Cost).FirstOrDefault();
            return alternative == null ? [baseline] : [baseline, alternative];
        }
    }

    private sealed record WeightedEvidence(double Mean, double EffectiveSamples, double StandardError, double Improvement);
    private static class RiskEstimate
    {
        internal static WeightedEvidence Evaluate(IReadOnlyList<double> deltas, IReadOnlyList<double> weights)
        {
            if (deltas.Count == 0 || deltas.Count != weights.Count || deltas.Any(x => !double.IsFinite(x)) || weights.Any(w => !double.IsFinite(w) || w <= 0))
                throw new ArgumentException("Finite deltas and positive weights required");
            double sum = weights.Sum(), effective = sum * sum / weights.Sum(w => w * w);
            double mean = deltas.Select((d, i) => d * weights[i]).Sum() / sum;
            double variance = deltas.Select((d, i) => weights[i] * Math.Pow(d - mean, 2)).Sum() / sum;
            double error = Math.Sqrt(variance / Math.Max(1, effective - 1));
            // A heavily dominated sample should not overturn the cheap baseline.
            double improvement = effective < 1.5 ? double.NegativeInfinity : mean - 8 - .75 * error;
            return new(mean, effective, error, improvement);
        }
    }
}
