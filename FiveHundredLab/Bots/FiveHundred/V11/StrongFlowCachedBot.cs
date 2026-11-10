using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred.V11;

/// <summary>Incremental strategy with decision-local Medium caches and direct continuation inputs.
/// Search policy, budgets, thresholds and configured-rule decisions match the incremental baseline.</summary>
public sealed class StrongFlowCachedBot : FiveHundredRuleBot
{
    private const int SearchSamples = 32;
    private readonly DefaultPolicy defaultPolicy;
    private readonly PlayRolloutCounter playWork = new();
    public sealed record PlayWork(long Evaluations, long ForcedMoves, long PolicyMoves);
    public PlayWork? LastPlayWork => LastPhase == "play" ? new(playWork.Evaluations, playWork.ForcedMoves, playWork.PolicyMoves) : null;
    public sealed record DataWork(int EncodedCards, int StrategyInstances, long AdapterCalls, long DirectPlayCalls);
    public DataWork? LastDataWork => defaultPolicy.LastDataContext is { } data
        ? new(data.EncodedCards, data.StrategyInstances, data.AdapterCalls, data.DirectPlayCalls) : null;
    private VariantFlowData? flowData;
    private int configuredRankEvaluations;
    public const string Profile = "strong-flow-cached-v1.1";
    public SearchReport? LastDefaultSearch => defaultPolicy.LastSearch;
    public string? LastPhase { get; private set; }
    public sealed record TacticalReport(string? Reason, int OriginalCandidates, int RetainedCandidates, int ProofNodes);
    public TacticalReport? LastTactics { get; private set; }
    public sealed record DiscardGuardReport(bool Changed, PlayingCard[] Original, PlayingCard[] Selected, string Reason, int Edits);
    public DiscardGuardReport? LastDiscardGuard { get; private set; }
    public sealed record AuditReport(bool Reviewed, bool Vetoed, int Samples, double Mean, double StandardError, string Reason);
    public AuditReport? LastAudit { get; private set; }
    public sealed record WorkReport(int StaticEvaluations, int FeatureEvaluations, int StaticCacheHits, int FeatureCacheHits);
    public WorkReport? LastWork { get; private set; }
    public StrongFlowCachedBot(int seed = 1, Rules? rules = null) : base(Profile, seed, rules)
    { defaultPolicy = new(seed, playWork); }
    public static StrongFlowCachedBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));
    private void Begin(string phase)
    { LastPhase=phase; playWork.Reset(); LastRollouts=0; LastV11Search=null; configuredRankEvaluations=0; LastTactics=null; LastDiscardGuard=null; LastAudit=null; LastWork=null; defaultPolicy.ClearSearch(); }
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
        Begin("bid");
        if(rules!=null)return SearchBidV11(view,rules,legal);
        var action=defaultPolicy.Bid(view, legal);
        LastRollouts=defaultPolicy.SearchRollouts;
        return action;
    }
    public override IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var rules=RequireMatchingRules(view); Begin("discard");flowData=rules==null?null:new(rules);
        var pool=IsNormalNoTrump(view) && count>0 ? new DiscardPool(view,count) : null;
        PlayingCard[] original;
        if(rules==null) { original=defaultPolicy.Discard(view,count,pool).ToArray();LastRollouts=defaultPolicy.SearchRollouts; }
        else if(count==0) original=[];
        else
        {
            var choices=pool!=null ? pool.Ranked.Take(12).Select(e=>e.Discard).ToArray() : DiscardCandidates(view,rules,count,12);
            if(pool!=null)pool.StaticCacheHits+=pool.Rows.Length;
            original=SelectByRollout(view,rules,choices,(world,move)=>VariantRollout.Discard(view,rules,world,move,flowData));
        }
        var result=RefineCore(view,original,pool);
        if(LastWork!=null) LastWork=LastWork with{StaticEvaluations=LastWork.StaticEvaluations+defaultPolicy.DiscardStaticEvaluations+configuredRankEvaluations};
        return result;
    }
    // Review a caller's legal pickup discard without rerunning the original root search.
    // Useful for isolated strategy validation; it does not advance the main search stream.
    public IReadOnlyList<PlayingCard> RefineProposedDiscard(Observation view,IReadOnlyList<PlayingCard> proposal)
    {
        RequireMatchingRules(view);Begin("discard-review");
        if(view.Contract==null||view.Declarer!=view.Seat||proposal.Count!=view.Hand.Count-10||proposal.Distinct().Count()!=proposal.Count||proposal.Any(c=>!view.Hand.Contains(c)))
            throw new ArgumentException("Expected the declarer's legal pickup discard");
        return RefineCore(view,proposal.ToArray(),null);
    }
    private static bool IsNormalNoTrump(Observation v)=>v.Contract is {IsMisere:false,Trump:Suit.Unknown};
    private PlayingCard[] RefineCore(Observation view, PlayingCard[] original, DiscardPool? pool)
    {
        var guarded = GuardedSuits(view);
        var kept = view.Hand.Except(original).ToArray();
        bool Unsupported(PlayingCard[] hand) => guarded.Any(s =>
            hand.Any(c => !c.IsJoker && c.Suit == s && c.Rank == 13) &&
            hand.Count(c => !c.IsJoker && c.Suit == s) == 1);
        string reason = "not-applicable";
        PlayingCard[] selected = original;
        int edits = 0;
        // No structural penalties or audit: this path preserves the chosen v1 repair exactly.
        LastAudit = new(false, false, 0, 0, 0, "not-required");
        if (IsNormalNoTrump(view) && original.Length > 0 && Unsupported(kept))
        {
            pool ??= new(view, original.Length);
            var before = pool.Find(original);
            var controls = kept.Where(c => c.IsJoker || c.Rank == 14).ToHashSet();
            double floor = before.Value - .5;
            DiscardEntry? best = null;
            double bestValue = double.NegativeInfinity;
            // Preserve enumeration order and strict comparison: ties must keep the first candidate.
            foreach (var entry in pool.Rows)
            {
                if (entry.Value < floor || entry.Discard.Any(controls.Contains) || Unsupported(entry.Kept))
                    continue;
                if (entry.Value > bestValue) { best = entry; bestValue = entry.Value; }
            }
            if (best != null)
            {
                selected = best.Discard;
                edits = selected.Count(c => !original.Contains(c));
                reason = "legacy-king-support";
            }
            else reason = "no-shape-safe-repair"; // Preserve the existing diagnostic contract.
        }
        LastWork = pool == null ? new(0, 0, 0, 0) : new(pool.StaticEvaluations, 0, pool.StaticCacheHits, 0);
        LastDiscardGuard = new(!original.ToHashSet().SetEquals(selected), original, selected, reason, edits);
        return selected;
    }
    private static Suit[] GuardedSuits(Observation view)
    {
        if (!IsNormalNoTrump(view)) return [];
        var dead = view.History.Select(p => p.Action.Card).Concat(view.Trick.Select(p => p.Action.Card))
            .Concat(view.OwnDiscards).ToHashSet();
        // Keep the selected baseline's exact guard, including its behavior for custom decks.
        return Rules.Suits.Where(s =>
            view.Hand.Any(c => !c.IsJoker && c.Suit == s && c.Rank == 13) &&
            view.Hand.Any(c => !c.IsJoker && c.Suit == s && c.Rank < 13) &&
            !view.Hand.Any(c => !c.IsJoker && c.Suit == s && c.Rank == 14) &&
            !dead.Contains(new(s, 14))).ToArray();
    }
    internal sealed record DiscardEntry(PlayingCard[] Discard, PlayingCard[] Kept, double Value);
    internal sealed class DiscardPool
    {
        public readonly DiscardEntry[] Rows;
        private DiscardEntry[]? ranked;
        // Root search needs ranking; isolated repair does not. Preserve stable descending ties.
        public DiscardEntry[] Ranked => ranked ??= Rows.OrderByDescending(e => e.Value).ToArray();
        public int StaticEvaluations, StaticCacheHits;
        public DiscardPool(Observation view, int count)
        {
            Rows = HandEvaluator.DiscardSets(view.Hand, count).Select(discard =>
            {
                var kept = view.Hand.Except(discard).ToArray();
                StaticEvaluations++;
                double value = view.Rules is ConfiguredRules rules
                    ? RuleBotEvaluation.RetainedValue(kept, view.Contract!, rules)
                    : HandEvaluator.RetainedValue(kept, view.Contract!, view.Rules);
                return new DiscardEntry(discard, kept, value);
            }).ToArray();
        }
        public DiscardEntry Find(PlayingCard[] discard)
        {
            // At most one lookup per repair. Avoid sorting/stringifying every candidate for an index.
            var cards = discard.ToHashSet();
            return Rows.First(e => e.Discard.Length == discard.Length && e.Discard.All(cards.Contains));
        }
    }

    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        Begin("play");
        flowData=rules==null?null:new(rules);
        var tactics = ApplyPublicTactics(view, legal);
        LastTactics = new(tactics.Reason, legal.Count, tactics.Choices.Length, tactics.Nodes);
        legal = tactics.Choices;
        if (legal.Count == 1)
            return legal[0];
        if (rules == null)
        { var action=defaultPolicy.Play(view,legal);LastRollouts=defaultPolicy.SearchRollouts;return action; }
        var baseline = MediumBot.PlayConfiguredTactics(view, rules, legal);
        // Preserve baseline-first tie handling while searching every legal action.
        var choices = legal.OrderBy(p => p == baseline ? 0 : 1).Take(legal.Count).ToArray();
        return SelectByRollout(view, rules, choices, (world, move) => VariantRollout.Play(view, rules, world, move, flowData));
    }
    // Prove only the current trick. Unknown hands are supersets of all remaining public possibilities,
    // never sampled allocations. Exhaustion of the node budget means abstain, not a successful proof.
    internal static (PlayAction[] Choices, string? Reason, int Nodes) ApplyPublicTactics(
        Observation view, IReadOnlyList<PlayAction> legal, int maxNodes = 8192)
    {
        var original = legal.ToArray();
        if (legal.Count <= 1 || view.Contract is not { } contract || view.Declarer is not int declarer)
            return (original, null, 0);
        if (!contract.IsMisere || contract.Kind == ContractKind.GrandNullo) return (original, null, 0);
        int team = view.Seat % 2;
        bool declarerSide = team == declarer % 2;
        if (contract.IsMisere && view.Players[declarer].Tricks > 0)
            return (original, null, 0);
        bool allFour=view.Rules is ConfiguredRules configured && ConfiguredRules.Text(configured.ContractOptions(contract),"activeSeats")=="all_four";
        var active = Enumerable.Range(0, 4).Where(s => allFour || s != (declarer + 2) % 4).ToArray();
        var unknown = view.Rules.Deck().Except(view.Hand).Except(view.OwnDiscards)
            .Except(view.History.Select(p => p.Action.Card)).Except(view.Trick.Select(p => p.Action.Card))
            .Except(view.Players.SelectMany(p => p.ExposedHand ?? [])).ToArray();
        var prefix = view.Trick.ToList();
        int nodes = 0;
        bool Prove(Func<int, bool> goal)
        {
            if (nodes >= maxNodes) return false;
            nodes++;
            if (prefix.Count == active.Length) return goal(view.Rules.Winner(prefix, contract));
            int next = (prefix[^1].Seat + 1) % 4;
            while (!active.Contains(next)) next = (next + 1) % 4;
            IReadOnlyList<PlayAction> replies;
            if (view.Players[next].ExposedHand is { } exposed)
            {
                var remaining = exposed.Where(c => !prefix.Any(p => p.Action.Card == c)).ToArray();
                // Configured follow/Joker legality consults history and current seat. Use a detached copy.
                Rules replyRules = view.Rules is ConfiguredRules rules ? rules with
                {
                    CurrentSeat = next, DeclarerSeat = declarer,
                    History = view.History.Take(Math.Max(0, view.History.Count - view.Trick.Count)).Concat(prefix).ToArray(),
                    Declarations = new(rules.Declarations)
                } : view.Rules;
                if (remaining.Length == 0) return false;
                try { replies = replyRules.LegalPlays(remaining, prefix, contract); }
                catch (InvalidOperationException) { return false; }
            }
            else
            {
                // Permit even off-suit cards from unknown hands. No guessed following/void assumptions.
                // Prefix is nonempty, so following Jokers never require a new lead declaration.
                replies = unknown.Where(c => !prefix.Any(p => p.Action.Card == c)).Select(c => new PlayAction(c)).ToArray();
            }
            if (replies.Count == 0) return false;
            foreach (var reply in replies)
            {
                prefix.Add(new(next, reply));
                bool proven = Prove(goal);
                prefix.RemoveAt(prefix.Count - 1);
                if (!proven) return false;
            }
            return true;
        }
        bool After(PlayAction move, Func<int, bool> goal)
        {
            prefix.Add(new(view.Seat, move));
            bool result = Prove(goal);
            prefix.RemoveAt(prefix.Count - 1);
            return result;
        }
        if (contract.IsMisere)
        {
            if (!declarerSide)
            {
                // Any certain declarer trick defeats this contract; cheapest proof is sufficient.
                foreach (var move in legal.OrderBy(p => view.Rules.Rank(p.Card, contract)).ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit))
                    if (After(move, winner => winner == declarer))
                        return ([move], "misere-certain-set", nodes);
            }
            else
            {
                var choices = legal.Where(move => !After(move, winner => winner == declarer)).ToArray();
                if (choices.Length > 0 && choices.Length < legal.Count)
                    return (choices, "misere-avoid-certain-loss", nodes);
            }
        }
        return (original, null, nodes);
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
    private PlayingCard[][] DiscardCandidates(Observation view, ConfiguredRules rules, int count, int limit)
    {
        var danger = view.Hand.ToDictionary(card => card, card => RuleBotEvaluation.Danger(view, card));
        double Value(PlayingCard[] discard)
        {
            configuredRankEvaluations++;
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
            double evidence=mean-8-0.75*se;
            if(evidence>best){best=evidence;selected=i;selectedMean=baseMean+mean;}
        }
        LastV11Search=new(worlds.Count,choices.Length,baseMean,selectedMean,selected!=0);
        return choices[selected];
    }

    // Complete original Strong selection logic, now using the Medium policy owned by MediumBot.cs.
    private sealed class DefaultPolicy : IPlayer
    {
        private FlowPolicyCache medium = null!;
        private readonly SeededRandom random;
        private readonly StrongBotOptions options;
        private readonly PlayRolloutCounter playWork;
        public DefaultPolicy(int seed, PlayRolloutCounter playWork, StrongBotOptions? options = null)
        {
            this.playWork = playWork;
            this.options = options ?? new() { Samples = 32 };
            this.options.Validate();
            random = new(seed);
        }
        public string Name => $"strong-v1-s{options.Samples}-d{options.DiscardCandidates}-m{options.ImprovementMargin.ToString(System.Globalization.CultureInfo.InvariantCulture)}-z{options.StandardErrorMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        public SearchReport? LastSearch
        {
            get; private set;
        }
        public FlowPolicyCache? LastDataContext {get;private set;}
        public int SearchRollouts {get;private set;}
        public int DiscardStaticEvaluations {get;private set;}
        public void ClearSearch(){LastDataContext=null;LastSearch=null;SearchRollouts=0;DiscardStaticEvaluations=0;}
        public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
        {
            medium = LastDataContext = new FlowPolicyCache(view);
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
            return Select(view, candidates.Distinct().ToArray(), baseline, (world, bid) => new FlowCachedRollout(view, world, medium, playWork).Bid(bid), EqualityComparer<Contract?>.Default);
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
        public IReadOnlyList<PlayingCard> Discard(Observation view,int count)=>Discard(view,count,null);
        internal IReadOnlyList<PlayingCard> Discard(Observation view,int count,DiscardPool? pool)
        {
            medium = LastDataContext = new FlowPolicyCache(view);
            var baseline=medium.Discard(view,count).ToArray();
            double Value(PlayingCard[] d){DiscardStaticEvaluations++;return HandEvaluator.RetainedValue(view.Hand.Except(d).ToArray(),view.Contract!,view.Rules);}
            var candidates=(pool!=null?pool.Ranked.Select(e=>e.Discard):HandEvaluator.DiscardSets(view.Hand,count).OrderByDescending(Value)).Take(options.DiscardCandidates).ToList();
            if(pool!=null)pool.StaticCacheHits+=pool.Rows.Length;
            candidates.Insert(0,baseline);
            return Select(view,candidates.Distinct(CardSetComparer.Instance).ToArray(),baseline,(world,cards)=>new FlowCachedRollout(view, world, medium, playWork).Discard(cards),CardSetComparer.Instance);
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        {
            medium = LastDataContext = new FlowPolicyCache(view);
            var baseline = medium.Play(view, legal);
            return Select(view, legal, baseline, (world, play) => new FlowCachedRollout(view, world, medium, playWork).Play(play), EqualityComparer<PlayAction>.Default);
        }
        private T Select<T>(Observation view, IReadOnlyList<T> candidates, T baseline, Func<SampledDeal, T, double> evaluate, IEqualityComparer<T> comparer)
        {
            if (candidates.Count == 1)
            {
                LastSearch = new(0, 1, 0, 0, false);
                return baseline;
            }
            var worlds = BeliefSampler.Sample(view, options.Samples, random);
            double sumWeight = worlds.Sum(w => w.Weight), effectiveN = sumWeight * sumWeight / worlds.Sum(w => w.Weight * w.Weight);
            double[] Values(T choice) => worlds.Select(w => { SearchRollouts++;return evaluate(w, choice); }).ToArray();
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
        var data=new VariantFlowData(rules);
        double[] Values(Contract? choice)=>worlds.Select(w=>{LastRollouts++;return new VariantRollout(view,rules,w,data).Bid(choice);}).ToArray();
        var original=Values(baseline);double mean=original.Average(),best=0,chosenMean=mean;Contract? selected=baseline;
        foreach(var candidate in choices)
        {
            if(candidate==baseline)continue;
            var values=Values(candidate);var differences=values.Select((v,i)=>v-original[i]).ToArray();
            double improvement=differences.Average();double variance=differences.Select(v=>(v-improvement)*(v-improvement)).Average();
            double se=Math.Sqrt(variance/Math.Max(1,worlds.Count-1));double evidence=improvement-8-0.75*se;
            if(evidence>best){best=evidence;selected=candidate;chosenMean=mean+improvement;}
        }
        LastV11Search=new(worlds.Count,choices.Length,mean,chosenMean,selected!=baseline);return selected;
    }
}
