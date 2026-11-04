using FiveHundredArena;
namespace FiveHundredLab;

// Fixed speed-first revision: same bidding and normal budgets as V6; bounded last-seat exceptions.
public sealed class VisibilityFastV8(int seed) : IPlayer
{
    private readonly VisibilityMediumV5 medium = new();
    private readonly SeededRandom random = new(seed);
    public string Name => "visibility-fast-v8";
    public AdaptiveSearchV1? LastSearch { get; private set; }

    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal)
    {
        LastSearch = new(1, 0, 0, true, "bid");
        return medium.Bid(v, legal);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count)
    {
        var baseline = medium.Discard(v, count).ToArray();
        var choices = LegacyFeaturesV1.DiscardSets(v.Hand, count)
            .OrderByDescending(d => LegacyFeaturesV1.RetainedValue(v.Hand.Except(d).ToArray(), v.Contract!, v.Rules)).Take(3).ToList();
        choices.RemoveAll(d => d.ToHashSet().SetEquals(baseline)); choices.Insert(0, baseline);
        return Evaluate(v, choices, baseline, 4, (m, d) => m.Discard(d), "discard");
    }
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal)
    {
        var baseline = medium.Play(v, legal);
        if (legal.Count == 1) { LastSearch = new(1, 0, 0, true, "play"); return baseline; }
        bool last = v.Trick.Count == (v.Contract!.IsMisere ? 2 : 3);
        var choices = (last ? FastPruningV8.LastSeatChoices(v, legal, baseline) : FastPruningV8.Representatives(v, legal, baseline)).ToList();
        choices.Remove(baseline); choices.Insert(0, baseline);
        return Evaluate(v, choices, baseline, last ? 2 : v.Hand.Count >= 7 ? 4 : 8, (m, p) => m.Play(p), "play");
    }
    private T Evaluate<T>(Observation v, IReadOnlyList<T> choices, T baseline, int count, Func<VisibilityForwardV5, T, double> evaluate, string phase)
    {
        if (choices.Count < 2) { LastSearch = new(1, 0, 0, true, phase); return baseline; }
        var worlds = BeliefSampler.Sample(v, count, random);
        var weights = worlds.Select(w => w.Weight).ToArray();
        var original = worlds.Select(w => evaluate(new(v, w), baseline)).ToArray();
        T best = baseline; double improvement = 0;
        foreach (var choice in choices.Skip(1))
        {
            var differences = worlds.Select((w, i) => evaluate(new(v, w), choice) - original[i]).ToArray();
            var evidence = RiskEstimateV8.Evaluate(differences, weights);
            if (evidence.Improvement > improvement) { improvement = evidence.Improvement; best = choice; }
        }
        LastSearch = new(choices.Count, count, choices.Count * count, false, phase); return best;
    }
}
