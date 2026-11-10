using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred.V11;

/// <summary>Strength-first revision: larger shared samples, wider discards and public Misere proofs.</summary>
public sealed class StrongPlusBot : FiveHundredRuleBot
{
    private const int SearchSamples = 32;
    private readonly DefaultPolicy defaultPolicy;
    public SearchReport? LastDefaultSearch => defaultPolicy.LastSearch;
    public StrongPlusBot(int seed = 1, Rules? rules = null) : base("strong-plus-v1.1", seed, rules) => defaultPolicy = new(seed);
    public static StrongPlusBot FromConfiguration(string? path = null, int seed = 1) => new(seed, RuleConfiguration.Load(path));
    // Solo declaration is deliberately conservative; the partner will stop playing.
    public override bool DeclareGrandSlam(Observation view)
    {
        var rules=RequireMatchingRules(view);
        if(rules==null||view.Contract is not {Kind:ContractKind.Normal} contract)return false;
        bool controls=view.Hand.Any(c=>c.IsJoker) && (contract.Trump==Suit.Unknown ? view.Hand.Count(c=>c.Rank==14)>=3 : view.Hand.Count(c=>rules.EffectiveSuit(c,contract)==contract.Trump)>=7);
        return controls && RuleBotEvaluation.HandValue(view.Hand,contract,rules)>=9.8;
    }
    public override Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0; LastV11Search=null;
        // Configured bidding remains the existing score heuristic; no strategy upgrade in this migration.
        return rules == null ? defaultPolicy.Bid(view, legal) : SearchBidV11(view, rules, legal);
    }
    public override IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0; LastV11Search=null;
        if (rules == null)
            return defaultPolicy.Discard(view, count);
        if (count == 0)
            return Array.Empty<PlayingCard>();
        var choices = DiscardCandidates(view, rules, count, 24);
        return SelectByRollout(view, rules, choices, (world, move) => VariantRollout.Discard(view, rules, world, move));
    }
    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0; LastV11Search=null;
        if (rules == null)
        {
            if (view.Contract!.IsMisere)
            {
                if (MisereProof.ImmediateMiserePlay(view, legal) is { } proof)
                    return proof;
                if (view.Declarer == view.Seat && view.Trick.Count == 0)
                {
                    var safe = legal.Where(p => !MisereProof.CertainLeadWin(view, p)).ToArray();
                    if (safe.Length > 0) legal = safe;
                }
            }
            return defaultPolicy.Play(view, legal);
        }
        if(view.Contract!.IsMisere)
            legal = StrongFlowCachedBot.ApplyPublicTactics(view, legal).Choices;
        if (legal.Count == 1)
            return legal[0];
        var baseline = MediumBot.PlayConfiguredTactics(view, rules, legal);
        // Preserve baseline-first tie handling while searching every legal action.
        var choices = legal.OrderBy(p => p == baseline ? 0 : 1).Take(legal.Count).ToArray();
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
        double[][] values=choices.Select(choice=>worlds.Select(world=>{LastRollouts++;return evaluate(world,choice);}).ToArray()).ToArray();
        double baseMean=values[0].Average(),selectedMean=baseMean,best=0;int selected=0;
        for(int i=1;i<choices.Length;i++)
        {
            var difference=values[i].Select((v,j)=>v-values[0][j]).ToArray();double mean=difference.Average();
            double variance=difference.Select(v=>(v-mean)*(v-mean)).Average();double se=Math.Sqrt(variance/Math.Max(1,worlds.Count-1));
            double evidence=mean-6-1.0*se;
            if(evidence>best){best=evidence;selected=i;selectedMean=baseMean+mean;}
        }
        LastV11Search=new(worlds.Count,choices.Length,baseMean,selectedMean,selected!=0);
        return choices[selected];
    }

    // Complete original Strong selection logic, now using the Medium policy owned by MediumBot.cs.
    private sealed class DefaultPolicy : IPlayer
    {
        private readonly MediumBot.VisibilityAdapter medium = new();
        private readonly SeededRandom random;
        private readonly StrongBotOptions options;
        public DefaultPolicy() : this(1) { }
        public DefaultPolicy(int seed, StrongBotOptions? options = null)
        {
            this.options = options ?? new() { Samples = 32, DiscardCandidates = 24, ImprovementMargin = 6, StandardErrorMultiplier = 1.0 };
            this.options.Validate();
            random = new(seed);
        }
        public string Name => $"strong-v1-s{options.Samples}-d{options.DiscardCandidates}-m{options.ImprovementMargin.ToString(System.Globalization.CultureInfo.InvariantCulture)}-z{options.StandardErrorMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
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
            var candidates = HandEvaluator.DiscardSets(view.Hand, count)
                .OrderByDescending(d => HandEvaluator.RetainedValue(view.Hand.Except(d).ToArray(), view.Contract!, view.Rules))
                .Take(options.DiscardCandidates).ToList();
            candidates.Insert(0, baseline);
            return Select(view, candidates.Distinct(CardSetComparer.Instance).ToArray(), baseline, (model, cards) => model.Discard(cards), CardSetComparer.Instance);
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
            double[] Values(T choice) => worlds.Select(w => evaluate(new DefaultRollout(view, w, () => new MediumBot.VisibilityAdapter()), choice)).ToArray();
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

    // Public cards only; no audit hands or actual hidden deals enter these proofs.
    private static class MisereProof
    {
        private static int Partner(Observation v) => (v.Seat + 2) % 4;
        private static PlayingCard[] Unknown(Observation v) => v.Rules.Deck()
            .Except(v.Hand).Except(v.OwnDiscards).Except(v.History.Select(p => p.Action.Card))
            .Except(v.Trick.Select(p => p.Action.Card)).Except(v.Players.SelectMany(p => p.ExposedHand ?? [])).ToArray();
        private static IEnumerable<PlayingCard> Possible(Observation v, int seat, PlayingCard[] unknown) => v.Players[seat].ExposedHand ?? unknown;
        private static PlayAction Cheapest(Observation v, IEnumerable<PlayAction> actions) => actions
            .OrderBy(p => p.Card.IsJoker ? 1000 : v.Rules.EffectiveSuit(p.Card, v.Contract!) == v.Contract!.Trump ? 100 + v.Rules.Rank(p.Card, v.Contract) : v.Rules.Rank(p.Card, v.Contract!))
            .ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit).First();

        internal static bool CertainLeadWin(Observation v, PlayAction lead)
        {
            var prefix = new[] { new PublicPlay(v.Seat, lead) };
            var unknown = Unknown(v);
            return v.Players.Where(p => p.Seat != v.Seat && (!v.Contract!.IsMisere || p.Seat != (v.Declarer + 2) % 4))
                .All(p => Possible(v, p.Seat, unknown).All(c => v.Rules.Winner([prefix[0], new(p.Seat, new(c))], v.Contract!) == v.Seat));
        }

        internal static PlayAction? ImmediateMiserePlay(Observation v, IReadOnlyList<PlayAction> legal)
        {
            if (v.Contract?.IsMisere != true || v.Declarer == null || v.Players[v.Declarer.Value].Tricks > 0)
                return null;
            int declarer = v.Declarer.Value;
            if (v.Trick.Count == 2)
            {
                var relevant = legal.Where(p =>
                {
                    int winner = v.Rules.Winner(v.Trick.Append(new PublicPlay(v.Seat, p)).ToArray(), v.Contract);
                    return declarer == v.Seat ? winner != declarer : winner == declarer;
                }).ToArray();
                if (relevant.Length > 0)
                    return declarer == v.Seat
                    ? relevant.OrderByDescending(p => v.Rules.Rank(p.Card, v.Contract)).ThenBy(p => p.Card.Suit).First()
                    : Cheapest(v, relevant);
            }
            if (declarer == v.Seat || v.Players[declarer].ExposedHand == null)
                return null;
            var unknown = Unknown(v);
            foreach (var move in legal.OrderBy(p => v.Rules.Rank(p.Card, v.Contract)).ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit))
            {
                var prefix = v.Trick.Append(new PublicPlay(v.Seat, move)).ToList();
                if (AllRepliesBust(prefix))
                    return move;
            }
            return null;

            bool AllRepliesBust(List<PublicPlay> prefix)
            {
                if (prefix.Count == 3)
                    return v.Rules.Winner(prefix, v.Contract!) == declarer;
                int next = (prefix[^1].Seat + 1) % 4;
                if (next == (declarer + 2) % 4)
                    next = (next + 1) % 4;
                var exposed = v.Players[next].ExposedHand;
                // Unknown hands are deliberately over-approximated: even off-suit replies are allowed.
                // A proof that survives this superset cannot depend on a guessed hidden allocation.
                var replies = exposed != null ? v.Rules.LegalPlays(exposed, prefix, v.Contract!)
                    : unknown.Where(c => !prefix.Any(p => p.Action.Card == c)).Select(c => new PlayAction(c)).ToList();
                if (replies.Count == 0)
                    return false;
                foreach (var reply in replies)
                {
                    prefix.Add(new(next, reply));
                    bool bust = AllRepliesBust(prefix);
                    prefix.RemoveAt(prefix.Count - 1);
                    if (!bust)
                        return false;
                }
                return true;
            }
        }

    }

    // Same-world paired auction continuation: keep searched bidding under v1.1 rules.
    private Contract? SearchBidV11(Observation view, ConfiguredRules rules, IReadOnlyList<Contract?> legal)
    {
        LastRollouts=0;LastV11Search=null;
        var baseline=LegalBid(BidConfigured(view,rules,legal),legal);
        var candidates=new List<Contract?>{baseline};if(legal.Contains(null))candidates.Add(null);
        foreach(var group in legal.Where(c=>c is {Kind:ContractKind.Normal}).Select(c=>c!).GroupBy(c=>c.Trump))
        {
            candidates.Add(group.First());
            double estimate=RuleBotEvaluation.HandValue(view.Hand,group.First(),rules)+2.3;
            var higher=group.LastOrDefault(c=>c.Tricks<=estimate);if(higher!=null)candidates.Add(higher);
            var winning=group.FirstOrDefault(c=>view.Scores[view.Seat%2]+rules.Value(c)>=rules.WinScore&&c.Tricks<=estimate+.5);if(winning!=null)candidates.Add(winning);
        }
        foreach(var bid in legal.Where(c=>c?.IsMisere==true).Select(c=>c!))
        {
            if(view.Hand.Any(c=>c.IsJoker) && ConfiguredRules.Text(rules.ContractOptions(bid).GetProperty("joker"),"unassignedRank")!="lowest")continue;
            int gaps=bid.Kind==ContractKind.OpenMisere?0:2;
            if(view.Hand.All(card=>rules.Deck().Count(c=>c.Suit==card.Suit&&c.Rank<card.Rank&&!view.Hand.Contains(c))<=gaps))candidates.Add(bid);
        }
        var choices=candidates.Distinct().ToArray();if(choices.Length==1){LastV11Search=new(0,1,0,0,false);return baseline;}
        var worlds=VariantSamples.Sample(view,rules,32,random);
        var data=(VariantFlowData?)null;
        double[] Values(Contract? choice)=>worlds.Select(w=>{LastRollouts++;return new VariantRollout(view,rules,w,data).Bid(choice);}).ToArray();
        var original=Values(baseline);double mean=original.Average(),best=0,chosenMean=mean;Contract? selected=baseline;
        foreach(var candidate in choices)
        {
            if(candidate==baseline)continue;
            var values=Values(candidate);var differences=values.Select((v,i)=>v-original[i]).ToArray();
            double improvement=differences.Average();double variance=differences.Select(v=>(v-improvement)*(v-improvement)).Average();
            double se=Math.Sqrt(variance/Math.Max(1,worlds.Count-1));double evidence=improvement-6-1.0*se;
            if(evidence>best){best=evidence;selected=candidate;chosenMean=mean+improvement;}
        }
        LastV11Search=new(worlds.Count,choices.Length,mean,chosenMean,selected!=baseline);return selected;
    }
}
