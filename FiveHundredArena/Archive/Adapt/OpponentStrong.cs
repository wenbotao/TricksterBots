using Trickster.cloud;

namespace FiveHundredArena;

public enum OpponentAdaptationStage { Bidding=1, Belief=2, Play=3 }

/// <summary>Root Monte Carlo policy improvement over Medium, with legal-information-only continuations.</summary>
public sealed partial class OpponentStrong : IPlayer, IJokerDeclarationPlayer
{
    private readonly Rules bound;
    private readonly bool learningEnabled,fixedMedium;
    private RuleAwarePlayer? configuredMedium;
    private IPlayer Baseline(Observation v)=>v.Rules is ConfiguredRules?configuredMedium??=new RuleAwarePlayer(RuleBotLevel.Medium,1,medium,v.Rules):medium;
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view){Observe(view);return [];}
    private readonly OpponentAdaptationStage stage;
    private readonly SeededRandom learningRandom;
    public OpponentBeliefs Beliefs {get;private set;}=new();
    private int observedRound=-1,bidsSeen;
    private PlayingCard[]? initialHand;
    private readonly MediumBotPlayer medium = new();
    private readonly SeededRandom random;
    private readonly StrongBotOptions options;
    
    public OpponentStrong(int seed, OpponentAdaptationStage stage=OpponentAdaptationStage.Bidding, StrongBotOptions? options = null, Rules? rules=null,bool learn=true,bool fixedMedium=false)
    {
        if(!Enum.IsDefined(stage))throw new ArgumentOutOfRangeException(nameof(stage));
        this.fixedMedium=fixedMedium;learningEnabled=learn&&!fixedMedium;
        bound=rules??new Rules();bound.Validate();
        this.stage=stage;learningRandom=new(unchecked(seed^0x537194));
        this.options = options ?? new(); this.options.Validate(); random = new(seed);
    }
    public string Name => $"opponent-strong-{stage.ToString().ToLowerInvariant()}-v1-s{options.Samples}-d{options.DiscardCandidates}-m{options.ImprovementMargin.ToString(System.Globalization.CultureInfo.InvariantCulture)}-z{options.StandardErrorMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}"+(fixedMedium?"-fixed-medium":!learningEnabled?"-fixed-mixture":"");
    public static IPlayer CreateStyleOpponent(int biddingStyle,int playStyle=1)=>new OpponentStyle(biddingStyle,playStyle);
    public SearchReport? LastSearch { get; private set; }
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        phase="bid";Observe(view,true);
        var baseline = Baseline(view).Bid(view, legal);
        var candidates = new List<Contract?> { baseline, null };
        foreach (var group in legal.Where(c => c is { Kind: ContractKind.Normal }).Select(c => c!).GroupBy(c => c.Trump))
        {
            // Include competitive minimums, feasible higher contracts, and the cheapest potential game winner.
            candidates.Add(group.First());
            double estimate = OpponentRules.HandValue(view, group.First()) + 2.3;
            var higher = group.LastOrDefault(c => c.Tricks <= estimate);
            if (higher != null) candidates.Add(higher);
            var winning = group.FirstOrDefault(c => view.Scores[view.Seat % 2] + view.Rules.Value(c) >= view.Rules.WinScore && c.Tricks <= estimate + .5);
            if (winning != null) candidates.Add(winning);
        }
        candidates.AddRange(legal.Where(c => c?.IsMisere == true && SafeMisereCandidate(view, c)));
        return Select(view, candidates.Distinct().ToArray(), baseline, (model, bid) => model.Bid(bid), EqualityComparer<Contract?>.Default);
    }
    private static bool SafeMisereCandidate(Observation v, Contract c)
    {
        if (v.Hand.Any(x => x.IsJoker && (v.Rules is not ConfiguredRules r || ConfiguredRules.Text(r.ContractOptions(c).GetProperty("joker"),"unassignedRank")!="lowest"))) return false;
        // A structural safety prior prevents a small sample from inventing a high-stakes Misere.
        int allowedGaps = c.Kind == ContractKind.OpenMisere ? 0 : 2;
        var deck = v.Rules.Deck();
        return v.Hand.All(card => deck.Count(x => x.Suit == card.Suit && x.Rank < card.Rank && !v.Hand.Contains(x)) <= allowedGaps);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        phase="discard";Observe(view);
        var baseline = Baseline(view).Discard(view, count).ToArray();
        var candidates = HandEvaluator.DiscardSets(view.Hand, count)
            .OrderByDescending(d => view.Rules is ConfiguredRules cr?view.Contract!.IsMisere?-view.Hand.Except(d).Sum(c=>RuleAwarePlayer.Danger(view,c)):RuleAwarePlayer.RetainedValue(view.Hand.Except(d).ToArray(),view.Contract,cr):HandEvaluator.RetainedValue(view.Hand.Except(d).ToArray(),view.Contract!,view.Rules))
            .Take(options.DiscardCandidates).ToList();
        candidates.Insert(0, baseline);
        return Select(view, candidates.Distinct(CardSetComparer.Instance).ToArray(), baseline, (model, cards) => model.Discard(cards), CardSetComparer.Instance);
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        phase="play";Observe(view);
        var baseline = Baseline(view).Play(view, legal);
        return Select(view, legal, baseline, (model, play) => model.Play(play), EqualityComparer<PlayAction>.Default);
    }
    private T Select<T>(Observation view, IReadOnlyList<T> candidates, T baseline, Func<OpponentForward, T, double> evaluate, IEqualityComparer<T> comparer)
    {
        if (candidates.Count == 1) { LastSearch = new(0, 1, 0, 0, false); return baseline; }
        var worlds = view.Rules is ConfiguredRules cr?RuleSamples.Sample(view,cr,options.Samples,random):BeliefSampler.Sample(view, options.Samples, random);
        if(stage>=OpponentAdaptationStage.Belief)worlds=Reweight(view,worlds);
        var policies=worlds.Select(world=>Enumerable.Range(0,4).Select(seat=>(IPlayer)(seat==view.Seat?new OpponentStyle(1):new OpponentStyle(fixedMedium?1:DrawWorldStyle(view,world,seat),!fixedMedium&&stage>=OpponentAdaptationStage.Play?Beliefs.Draw(seat,random,true):1))).ToArray()).ToArray();
        double sumWeight = worlds.Sum(w => w.Weight), effectiveN = sumWeight * sumWeight / worlds.Sum(w => w.Weight * w.Weight);
        double[] Values(T choice) => worlds.Select((w,i) => evaluate(new OpponentForward(view, w,policies[i]), choice)).ToArray();
        var baseValues = Values(baseline);
        double baseMean = worlds.Select((w, i) => w.Weight * baseValues[i]).Sum() / sumWeight;
        T selected = baseline; double bestImprovement = 0, chosenMean = baseMean;
        foreach (var candidate in candidates)
        {
            if (comparer.Equals(candidate, baseline)) continue;
            var values = Values(candidate);
            var differences = values.Select((v, i) => v - baseValues[i]).ToArray();
            double mean = worlds.Select((w, i) => w.Weight * differences[i]).Sum() / sumWeight;
            double variance = worlds.Select((w, i) => w.Weight * Math.Pow(differences[i] - mean, 2)).Sum() / sumWeight;
            double standardError = Math.Sqrt(variance / Math.Max(1, effectiveN - 1));
            double evidence = mean - options.ImprovementMargin - options.StandardErrorMultiplier * standardError;
            if (evidence > bestImprovement) { bestImprovement = evidence; selected = candidate; chosenMean = baseMean + mean; }
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
