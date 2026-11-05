using Trickster.cloud;

using FiveHundredArena;

namespace FiveHundredLab;

public sealed record CachedStrongV1Options
{
    public int Samples { get; init; } = 8;
    public int DiscardCandidates { get; init; } = 12;
    public double ImprovementMargin { get; init; } = 8;
    public double StandardErrorMultiplier { get; init; } = .75;
    public void Validate()
    {
        if (Samples is < 1 or > 256 || DiscardCandidates is < 1 or > 286 || !double.IsFinite(ImprovementMargin) || ImprovementMargin < 0 || !double.IsFinite(StandardErrorMultiplier) || StandardErrorMultiplier < 0)
            throw new ArgumentException("Invalid CachedStrongV1 search budget or confidence margin");
    }
}
public sealed record CachedSearchReport(int Samples, int Candidates, double BaselineValue, double SelectedValue, bool Changed);

/// <summary>Root Monte Carlo policy improvement over Medium, with legal-information-only continuations.</summary>
public sealed class CachedStrongV1 : IPlayer
{
    private readonly CachedMediumV1 medium = new();
    private readonly SeededRandom random;
    private readonly CachedStrongV1Options options;
    public CachedStrongV1() : this(1) { }
    public CachedStrongV1(int seed, CachedStrongV1Options? options = null)
    {
        this.options = options ?? new(); this.options.Validate(); random = new(seed);
    }
    public string Name => $"cached-strong-v1-s{options.Samples}-d{options.DiscardCandidates}-m{options.ImprovementMargin.ToString(System.Globalization.CultureInfo.InvariantCulture)}-z{options.StandardErrorMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    public CachedSearchReport? LastSearch { get; private set; }
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var baseline = medium.Bid(view, legal);
        var candidates = new List<Contract?> { baseline, null };
        foreach (var group in legal.Where(c => c is { IsMisere: false }).Select(c => c!).GroupBy(c => c.Trump))
        {
            // Include competitive minimums, feasible higher contracts, and the cheapest potential game winner.
            candidates.Add(group.First());
            double estimate = LegacyFeaturesV1.HandValue(view.Hand, group.First(), view.Rules) + 2.3;
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
        if (v.Hand.Any(x => x.IsJoker)) return false;
        // A structural safety prior prevents a small sample from inventing a high-stakes Misere.
        int allowedGaps = c.Kind == ContractKind.OpenMisere ? 0 : 2;
        var deck = v.Rules.Deck();
        return v.Hand.All(card => deck.Count(x => x.Suit == card.Suit && x.Rank < card.Rank && !v.Hand.Contains(x)) <= allowedGaps);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var baseline = medium.Discard(view, count).ToArray();
        var candidates = LegacyFeaturesV1.DiscardSets(view.Hand, count)
            .OrderByDescending(d => LegacyFeaturesV1.RetainedValue(view.Hand.Except(d).ToArray(), view.Contract!, view.Rules))
            .Take(options.DiscardCandidates).ToList();
        candidates.Insert(0, baseline);
        return Select(view, candidates.Distinct(CardSetComparer.Instance).ToArray(), baseline, (model, cards) => model.Discard(cards), CardSetComparer.Instance);
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var baseline = medium.Play(view, legal);
        return Select(view, legal, baseline, (model, play) => model.Play(play), EqualityComparer<PlayAction>.Default);
    }
    private T Select<T>(Observation view, IReadOnlyList<T> candidates, T baseline, Func<CachedForwardV1, T, double> evaluate, IEqualityComparer<T> comparer)
    {
        if (candidates.Count == 1) { LastSearch = new(0, 1, 0, 0, false); return baseline; }
        var worlds = BeliefSampler.Sample(view, options.Samples, random);
        double sumWeight = worlds.Sum(w => w.Weight), effectiveN = sumWeight * sumWeight / worlds.Sum(w => w.Weight * w.Weight);
        double[] Values(T choice) => worlds.Select(w => evaluate(new CachedForwardV1(view, w), choice)).ToArray();
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
