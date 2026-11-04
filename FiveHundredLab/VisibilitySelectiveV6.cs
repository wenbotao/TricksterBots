using FiveHundredArena;
namespace FiveHundredLab;

// Full information-limited continuations are retained; only candidates, samples and easy positions are pruned.
internal sealed class VisibilitySelectiveCoreV6 : IPlayer
{
    private readonly VisibilityMediumV5 medium = new();
    private readonly SeededRandom random;
    private readonly SelectiveProfileV1 profile;
    public VisibilitySelectiveCoreV6(int seed,SelectiveProfileV1 profile)
    {
        if(profile!=SelectiveProfileV1.FastV4 && profile!=SelectiveProfileV1.QualityV2)throw new ArgumentException("Unknown versioned visibility profile");
        this.profile=profile;random=new(seed);
    }
    public string Name=>profile==SelectiveProfileV1.FastV4?"visibility-fast-v6":"visibility-quality-v7";
    public AdaptiveSearchV1? LastSearch{get;private set;}
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
    {
        var baseline=medium.Bid(v,legal);
        if(profile.BidSamples==0 || legal.Count==1)return baseline;
        double Estimate(Contract c)
        {
            double own=LegacyFeaturesV1.HandValue(v.Hand,c,v.Rules)+2.3;
            double p=1/(1+Math.Exp(-(own-c.Tricks+.1)/.8));
            return v.Rules.Value(c)*(2*p-1);
        }
        var choices=legal.Where(c=>c is {IsMisere:false}).Select(c=>c!).GroupBy(c=>c.Trump).Select(g=>g.First()).OrderByDescending(Estimate).Take(3).Cast<Contract?>().ToList();
        choices.Remove(baseline);choices.Insert(0,baseline);if(!choices.Contains(null))choices.Add(null);
        return Evaluate(v,choices,baseline,profile.BidSamples,(m,c)=>m.Bid(c),"bid");
    }
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count)
    {
        var baseline=medium.Discard(v,count).ToArray();
        if(!profile.SearchDiscard)return baseline;
        var choices=LegacyFeaturesV1.DiscardSets(v.Hand,count).OrderByDescending(d=>LegacyFeaturesV1.RetainedValue(v.Hand.Except(d).ToArray(),v.Contract!,v.Rules)).Take(profile.DiscardCandidates).ToList();
        choices.RemoveAll(d=>d.ToHashSet().SetEquals(baseline));choices.Insert(0,baseline);
        return Evaluate(v,choices,baseline,profile.DiscardSamples,(m,d)=>m.Discard(d),"discard");
    }
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)
    {
        var baseline=medium.Play(v,legal);
        if(legal.Count==1 || v.Trick.Count==(v.Contract!.IsMisere?2:3)) {LastSearch=new(1,0,0,true,"play");return baseline;}
        var frame=new CardFrameV1(v);var candidates=frame.Representatives(legal,baseline).ToList();
        candidates.Remove(baseline);candidates.Insert(0,baseline);
        int budget=v.Hand.Count>=7?profile.EarlySamples:profile.LateSamples;
        return Evaluate(v,candidates,baseline,budget,(m,p)=>m.Play(p),"play");
    }
    private T Evaluate<T>(Observation v,IReadOnlyList<T> choices,T baseline,int count,Func<VisibilityForwardV5,T,double> evaluate,string phase)
    {
        if(choices.Count<2){LastSearch=new(1,0,0,true,phase);return baseline;}
        var samples=profile.CycleBelief?BeliefCyclesV2.Sample(v,count,random):BeliefSampler.Sample(v,count,random);double weight=samples.Sum(w=>w.Weight);
        var baseValues=samples.Select(w=>evaluate(new(v,w),baseline)).ToArray();
        double best=0;T chosen=baseline;
        foreach(var candidate in choices.Skip(1))
        {
            var delta=samples.Select((w,i)=>evaluate(new(v,w),candidate)-baseValues[i]).ToArray();
            double mean=samples.Select((w,i)=>w.Weight*delta[i]).Sum()/weight;
            double variance=samples.Select((w,i)=>w.Weight*Math.Pow(delta[i]-mean,2)).Sum()/weight;
            double evidence=mean-8-.75*Math.Sqrt(variance/Math.Max(1,count-1));
            if(evidence>best){best=evidence;chosen=candidate;}
        }
        LastSearch=new(choices.Count,count,choices.Count*count,false,phase);return chosen;
    }
}

public sealed class VisibilityFastV6(int seed) : IPlayer
{
    private readonly IPlayer inner=new VisibilitySelectiveCoreV6(seed,SelectiveProfileV1.FastV4);
    public string Name=>inner.Name;
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>inner.Bid(v,legal);
    public IReadOnlyList<PlayingCard> Discard(Observation v,int n)=>inner.Discard(v,n);
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
}
public sealed class VisibilityQualityV7(int seed) : IPlayer
{
    private readonly IPlayer inner=new VisibilitySelectiveCoreV6(seed,SelectiveProfileV1.QualityV2);
    public string Name=>inner.Name;
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>inner.Bid(v,legal);
    public IReadOnlyList<PlayingCard> Discard(Observation v,int n)=>inner.Discard(v,n);
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
}
