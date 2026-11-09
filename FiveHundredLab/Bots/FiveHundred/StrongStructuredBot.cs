using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>All32 search with a matched-size, structurally diverse discard shortlist.</summary>
public sealed class StrongStructuredBot : FiveHundredRuleBot
{
    private const int SearchSamples = 32;
    private readonly DefaultPolicy defaultPolicy;
    public SearchReport? LastDefaultSearch => defaultPolicy.LastSearch;
    public sealed record DiscardPoolReport(int CandidateCount, int OriginalCount, int Structures, int OriginalStructures);
    public DiscardPoolReport? LastDefaultDiscardPool => defaultPolicy.LastDiscardPool;
    public StrongStructuredBot(int seed = 1, Rules? rules = null) : base("strong-structured-v1", seed, rules) => defaultPolicy = new(seed);
    public static StrongStructuredBot FromConfiguration(string? path = null, int seed = 1) => new(seed, RuleConfiguration.Load(path));
    public override Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        // Configured bidding remains the existing score heuristic; no strategy upgrade in this migration.
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
        var choices = DiscardCandidates(view, rules, count, 12);
        return SelectByRollout(view, rules, choices, (world, move) => ConfiguredRollout.Discard(view, rules, world, move));
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
        // Preserve baseline-first tie handling while searching every legal action.
        var choices = legal.OrderBy(p => p == baseline ? 0 : 1).Take(legal.Count).ToArray();
        return SelectByRollout(view, rules, choices, (world, move) => ConfiguredRollout.Play(view, rules, world, move));
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
        var ranked = HandEvaluator.DiscardSets(view.Hand, count).OrderByDescending(Value).ToArray();
        return SelectStructuredDiscards(view, ranked, ranked[0], limit, Value);
    }

    private T SelectByRollout<T>(Observation view, ConfiguredRules rules, T[] choices, Func<SampledDeal, T, double> evaluate)
    {
        if (choices.Length == 1)
            return choices[0];
        int count = SearchSamples;
        // All candidates share the same hidden worlds; first-candidate ties remain stable.
        var worlds = RuleSamples.Sample(view, rules, count, random);
        var values = new double[choices.Length];
        foreach (var world in worlds)
            for (int i = 0; i < choices.Length; i++)
            {
                values[i] += evaluate(world, choices[i]);
                LastRollouts++;
            }
        return choices[Array.IndexOf(values, values.Max())];
    }

    // Complete original Strong selection logic, now using the Medium policy owned by MediumBot.cs.
    private sealed class DefaultPolicy : IPlayer
    {
        private readonly MediumBot.DefaultAdapter medium = new();
        private readonly SeededRandom random;
        private readonly StrongBotOptions options;
        public DefaultPolicy() : this(1) { }
        public DefaultPolicy(int seed, StrongBotOptions? options = null)
        {
            this.options = options ?? new() { Samples = 32 };
            this.options.Validate();
            random = new(seed);
        }
        public string Name => $"strong-v1-s{options.Samples}-d{options.DiscardCandidates}-m{options.ImprovementMargin.ToString(System.Globalization.CultureInfo.InvariantCulture)}-z{options.StandardErrorMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        public DiscardPoolReport? LastDiscardPool { get; private set; }
        public SearchReport? LastSearch
        {
            get; private set;
        }
        public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
        {
            var baseline = medium.Bid(view, legal);
            var candidates = new List<Contract?> { baseline, null };
            foreach (var group in legal.Where(c => c is { IsMisere: false }).Select(c => c!).GroupBy(c => c.Trump))
            {
                // Include competitive minimums, feasible higher contracts, and the cheapest potential game winner.
                candidates.Add(group.First());
                double estimate = HandEvaluator.HandValue(view.Hand, group.First(), view.Rules) + 2.3;
                var higher = group.LastOrDefault(c => c.Tricks <= estimate);
                if (higher != null)
                    candidates.Add(higher);
                var winning = group.FirstOrDefault(c => view.Scores[view.Seat % 2] + view.Rules.Value(c) >= view.Rules.WinScore && c.Tricks <= estimate + .5);
                if (winning != null)
                    candidates.Add(winning);
            }
            candidates.AddRange(legal.Where(c => c?.IsMisere == true && SafeMisereCandidate(view, c)));
            return Select(view, candidates.Distinct().ToArray(), baseline, (model, bid) => model.Bid(bid), EqualityComparer<Contract?>.Default);
        }
        private static bool SafeMisereCandidate(Observation v, Contract c)
        {
            if (v.Hand.Any(x => x.IsJoker))
                return false;
            // A structural safety prior prevents a small sample from inventing a high-stakes Misere.
            int allowedGaps = c.Kind == ContractKind.OpenMisere ? 0 : 2;
            var deck = v.Rules.Deck();
            return v.Hand.All(card => deck.Count(x => x.Suit == card.Suit && x.Rank < card.Rank && !v.Hand.Contains(x)) <= allowedGaps);
        }
        public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
        {
            var baseline = medium.Discard(view, count).ToArray();
            double Value(PlayingCard[] discard) => HandEvaluator.RetainedValue(view.Hand.Except(discard).ToArray(), view.Contract!, view.Rules);
            var ranked = HandEvaluator.DiscardSets(view.Hand, count).OrderByDescending(Value).ToArray();
            var candidates = SelectStructuredDiscards(view, ranked, baseline, options.DiscardCandidates, Value);
            var original = ranked.Take(options.DiscardCandidates).Prepend(baseline).Distinct(CardSetComparer.Instance).ToArray();
            LastDiscardPool = new(candidates.Length, original.Length,
                candidates.Select(d => DiscardStructure(view, d)).Distinct().Count(),
                original.Select(d => DiscardStructure(view, d)).Distinct().Count());
            return Select(view, candidates, baseline, (model, cards) => model.Discard(cards), CardSetComparer.Instance);
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        {
            var baseline = medium.Play(view, legal);
            return Select(view, legal, baseline, (model, play) => model.Play(play), EqualityComparer<PlayAction>.Default);
        }
        private T Select<T>(Observation view, IReadOnlyList<T> candidates, T baseline, Func<DefaultRollout, T, double> evaluate, IEqualityComparer<T> comparer)
        {
            if (candidates.Count == 1)
            {
                LastSearch = new(0, 1, 0, 0, false);
                return baseline;
            }
            var worlds = BeliefSampler.Sample(view, options.Samples, random);
            double sumWeight = worlds.Sum(w => w.Weight), effectiveN = sumWeight * sumWeight / worlds.Sum(w => w.Weight * w.Weight);
            double[] Values(T choice) => worlds.Select(w => evaluate(new DefaultRollout(view, w, () => new MediumBot.DefaultAdapter()), choice)).ToArray();
            var baseValues = Values(baseline);
            double baseMean = worlds.Select((w, i) => w.Weight * baseValues[i]).Sum() / sumWeight;
            T selected = baseline;
            double bestImprovement = 0, chosenMean = baseMean;
            foreach (var candidate in candidates)
            {
                if (comparer.Equals(candidate, baseline))
                    continue;
                var values = Values(candidate);
                var differences = values.Select((v, i) => v - baseValues[i]).ToArray();
                double mean = worlds.Select((w, i) => w.Weight * differences[i]).Sum() / sumWeight;
                double variance = worlds.Select((w, i) => w.Weight * Math.Pow(differences[i] - mean, 2)).Sum() / sumWeight;
                double standardError = Math.Sqrt(variance / Math.Max(1, effectiveN - 1));
                double evidence = mean - options.ImprovementMargin - options.StandardErrorMultiplier * standardError;
                if (evidence > bestImprovement)
                {
                    bestImprovement = evidence;
                    selected = candidate;
                    chosenMean = baseMean + mean;
                }
            }
            LastSearch = new(worlds.Count, candidates.Count, baseMean, chosenMean, !comparer.Equals(selected, baseline));
            return selected;
        }
        private sealed class CardSetComparer : IEqualityComparer<PlayingCard[]>
        {
            internal static readonly CardSetComparer Instance = new();
            public bool Equals(PlayingCard[]? x, PlayingCard[]? y) => x != null && y != null && x.Length == y.Length && x.ToHashSet().SetEquals(y);
            public int GetHashCode(PlayingCard[] cards) => cards.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
        }
    }

    // The existing All32 shortlist is top-12 plus Medium; match its exact number of distinct sets.
    // Keep four best ranked sets, then represent different suit lengths and protected-honor shapes.
    internal static PlayingCard[][] SelectStructuredDiscards(Observation view, PlayingCard[][] ranked,
        PlayingCard[] baseline, int limit, Func<PlayingCard[], double> value)
    {
        if (ranked.Length == 0) return [baseline];
        bool Same(PlayingCard[] a, PlayingCard[] b) => a.Length == b.Length && a.ToHashSet().SetEquals(b);
        var original = ranked.Take(limit).Prepend(baseline).DistinctBy(d => string.Join("/", d.OrderBy(c => c.Suit).ThenBy(c => c.Rank).Select(c => $"{(int)c.Suit}:{c.Rank}"))).ToArray();
        int target = original.Length;
        var chosen = new List<PlayingCard[]> { baseline };
        var represented = new HashSet<string> { DiscardStructure(view, baseline) };
        void Add(PlayingCard[] discard)
        {
            if (chosen.Count >= target || chosen.Any(d => Same(d, discard))) return;
            chosen.Add(discard); represented.Add(DiscardStructure(view, discard));
        }
        foreach (var discard in ranked.Take(4)) Add(discard);
        double floor = value(ranked[0]) - (view.Contract!.IsMisere ? 3.0 : 1.0);
        foreach (var discard in ranked)
        {
            if (chosen.Count == target) break;
            if (value(discard) >= floor && !represented.Contains(DiscardStructure(view, discard))) Add(discard);
        }
        // Fall back to ranking when structural alternatives are exhausted; never pad with duplicates.
        foreach (var discard in ranked)
        {
            if (chosen.Count == target) break;
            Add(discard);
        }
        var positions = ranked.Select((discard, index) => (discard, index)).ToDictionary(x => x.discard, x => x.index);
        return chosen.OrderBy(d => Same(d, baseline) ? -1 : positions[d]).ToArray();
    }
    internal static string DiscardStructure(Observation view, PlayingCard[] discard)
    {
        var kept = view.Hand.Except(discard).ToArray(); var rules = view.Rules; var contract = view.Contract!;
        var lengths = Rules.Suits.Select(s => kept.Count(c => rules.EffectiveSuit(c, contract) == s)).ToArray();
        int protectedKings = 0, shortHighSuits = 0;
        for (int i = 0; i < Rules.Suits.Count(); i++)
        {
            var cards = kept.Where(c => !c.IsJoker && rules.EffectiveSuit(c, contract) == Rules.Suits[i]).ToArray();
            if (cards.Length >= 2 && cards.Any(c => c.Rank == 13)) protectedKings |= 1 << i;
            if (cards.Length <= 2 && cards.Any(c => c.Rank >= 12)) shortHighSuits |= 1 << i;
        }
        return string.Join(",", lengths) + ":" + protectedKings + ":" + shortHighSuits + ":" + kept.Count(c => c.IsJoker);
    }
}
