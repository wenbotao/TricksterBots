using FiveHundredArena;
namespace FiveHundredLab;

public sealed record AdaptiveProfileV1(string Id, int MinSamples, int MaxSamples, int CandidateLimit, int HorizonTricks, bool UseModel)
{
    public static AdaptiveProfileV1 Sprint { get; } = new("adaptive-sprint-v1", 2, 4, 3, 2, false);
    public static AdaptiveProfileV1 Balanced { get; } = new("adaptive-balanced-v1", 4, 12, 4, 3, false);
    public static AdaptiveProfileV1 DistilledPhaseV2 { get; } = new("distilled-phase-v2", 2, 6, 3, 2, true);
    public static AdaptiveProfileV1 Distilled { get; } = new("distilled-v1", 2, 6, 3, 2, true);
}
public sealed record AdaptiveSearchV1(int Candidates, int SamplesUsed, int Rollouts, bool Direct, string Phase);
public sealed class AdaptiveV1 : IPlayer
{
    private readonly int seed;
    private readonly AdaptiveProfileV1 profile;
    private readonly ValueModelV1? model;
    private readonly SeededRandom random;
    private readonly CompactStrongV1 bidSearch;
    private readonly TacticianV1 tactical = new();
    public AdaptiveV1(int seed, AdaptiveProfileV1 profile, ValueModelV1? model = null)
    {
        if (!new[] { AdaptiveProfileV1.Sprint, AdaptiveProfileV1.Balanced, AdaptiveProfileV1.Distilled, AdaptiveProfileV1.DistilledPhaseV2 }.Contains(profile)) throw new ArgumentException("Use a versioned named profile");
        if (profile.UseModel && model == null) throw new ArgumentException("A trained model is required");
        this.seed = seed; this.profile = profile; this.model = model; random = new(seed); bidSearch = new(seed);
    }
    public string Name => profile.Id + (model == null ? "" : "@" + model.Digest);
    public AdaptiveSearchV1? LastSearch { get; private set; }
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal)
    {
        if (model != null && legal.Count > 1)
        {
            var baseline = new CompactMediumV1().Bid(v, legal);
            var ranked = legal.OrderByDescending(c => model.Predict(v, "bid", c).Utility).Take(2).ToList();
            if (!ranked.Contains(baseline)) ranked.Insert(0, baseline); else { ranked.Remove(baseline); ranked.Insert(0, baseline); }
            var worlds = BeliefSampler.Sample(v, profile.MaxSamples, random);
            double Estimate(Contract? c) => worlds.Sum(w => w.Weight * new CompactForwardV1(v, w).Bid(c)) / worlds.Sum(w => w.Weight);
            double baseValue = Estimate(baseline), bestValue = baseValue; var choice = baseline;
            foreach (var c in ranked.Where(c => c != baseline)) { double value = Estimate(c); if(value > bestValue + 20){bestValue=value;choice=c;} }
            LastSearch = new(ranked.Count,worlds.Count,ranked.Count*worlds.Count,false,"bid"); return choice;
        }
        if (profile == AdaptiveProfileV1.Sprint) { LastSearch = new(1,0,0,true,"bid"); return tactical.Bid(v,legal); }
        LastSearch = new(legal.Count, 8, 0, legal.Count == 1, "bid");
        return bidSearch.Bid(v, legal);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count)
    {
        var baseline = tactical.Discard(v, count).ToArray();
        var ranked = LegacyFeaturesV1.DiscardSets(v.Hand, count).Select(d => (Cards: d, Value: TacticianV1.Retained(v.Hand.Except(d).ToArray(), v.Contract!, v.Rules)))
            .OrderByDescending(x => x.Value).ToArray();
        if (ranked.Length == 1 || ranked[0].Value - ranked[Math.Min(1, ranked.Length - 1)].Value > 1.5)
        { LastSearch = new(1, 0, 0, true, "discard"); return ranked[0].Cards; }
        var candidates = new List<PlayingCard[]> { baseline };
        foreach (var x in ranked.Take(profile.CandidateLimit)) if (!candidates.Any(c => c.ToHashSet().SetEquals(x.Cards))) candidates.Add(x.Cards);
        return Choose(v, candidates, baseline, (m, a) => m.Discard(a), "discard");
    }
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal)
    {
        var baseline = tactical.Play(v, legal);
        if (legal.Count == 1 || v.Trick.Count == (v.Contract!.IsMisere ? 2 : 3))
        { LastSearch = new(1, 0, 0, true, "play"); return baseline; }
        var f = new CardFrameV1(v);
        var representatives = f.Representatives(legal, baseline);
        var candidates = representatives.OrderByDescending(p => p == baseline).ThenBy(p => f.Table.Strength(p.Card)).Take(profile.CandidateLimit).ToList();
        return Choose(v, candidates, baseline, (m, a) => m.Play(a), "play");
    }
    private T Choose<T>(Observation v, IReadOnlyList<T> candidates, T baseline, Func<ShortForwardV1, T, RolloutValueV1> evaluate, string phase)
    {
        if (candidates.Count < 2) { LastSearch = new(1, 0, 0, true, phase); return baseline; }
        var worlds = BeliefSampler.Sample(v, profile.MaxSamples, random);
        var sums = new double[candidates.Count]; var squares = new double[candidates.Count]; var alive = Enumerable.Repeat(true, candidates.Count).ToArray();
        int calls = 0, used = 0; double weight = 0;
        for (int sample = 0; sample < worlds.Count; sample++)
        {
            var world = worlds[sample]; used++; weight += world.Weight;
            for (int a = 0; a < candidates.Count; a++)
            {
                if (!alive[a]) continue;
                var simulation = new ShortForwardV1(v, world, profile.HorizonTricks, model, unchecked(seed + sample * 101), ContinuationPoolV1.Infer(v, sample));
                double value = evaluate(simulation, candidates[a]).Utility; calls++;
                sums[a] += world.Weight * value; squares[a] += world.Weight * value * value;
            }
            if (used < profile.MinSamples) continue;
            int best = Enumerable.Range(0, candidates.Count).Where(a => alive[a]).MaxBy(a => sums[a] / weight);
            double Uncertainty(int a) => 1.25 * Math.Sqrt(Math.Max(0, squares[a] / weight - Math.Pow(sums[a] / weight, 2)) / Math.Max(1, used - 1));
            double lower = sums[best] / weight - Uncertainty(best);
            for (int a = 1; a < candidates.Count; a++) if (alive[a] && sums[a] / weight + Uncertainty(a) < lower - 8) alive[a] = false;
            if (Enumerable.Range(0, candidates.Count).Count(a => alive[a]) == 1 || (used >= profile.MinSamples && Enumerable.Range(0, candidates.Count).Where(a => alive[a]).Max(a => sums[a] / weight) - sums[0] / weight < 4)) break;
        }
        int choice = Enumerable.Range(0, candidates.Count).Where(a => alive[a]).MaxBy(a => sums[a]);
        LastSearch = new(candidates.Count, used, calls, false, phase);
        return sums[choice] / weight > sums[0] / weight + 12 ? candidates[choice] : baseline;
    }
}
