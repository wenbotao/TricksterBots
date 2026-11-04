using FiveHundredArena;
namespace FiveHundredLab;

public sealed record SelectiveProfileV1(string Id, bool SearchDiscard, int EarlySamples, int LateSamples, int DiscardCandidates=3, int DiscardSamples=4, int BidSamples=0, bool CycleBelief=false)
{
    public static SelectiveProfileV1 PlayOnly { get; } = new("selective-play-v1",false,4,8);
    public static SelectiveProfileV1 FastV4 { get; } = new("selective-fast-v4",true,4,8,3,4,0,false);
    public static SelectiveProfileV1 BeliefV3 { get; } = new("selective-belief-v3",true,12,20,6,8,4,true);
    public static SelectiveProfileV1 QualityV2 { get; } = new("selective-quality-v2",true,12,20,6,8,4);
    public static SelectiveProfileV1 Joint { get; } = new("selective-joint-v1",true,8,12);
}
// Full information-limited continuations are retained; only candidates, samples and easy positions are pruned.
public sealed class SelectiveFullV1 : IPlayer
{
    private readonly CachedMediumV1 medium = new();
    private readonly SeededRandom random;
    private readonly SelectiveProfileV1 profile;
    public SelectiveFullV1(int seed,SelectiveProfileV1 profile)
    {
        if(profile!=SelectiveProfileV1.PlayOnly && profile!=SelectiveProfileV1.Joint && profile!=SelectiveProfileV1.QualityV2 && profile!=SelectiveProfileV1.BeliefV3 && profile!=SelectiveProfileV1.FastV4)throw new ArgumentException("Unknown versioned profile");
        this.profile=profile;random=new(seed);
    }
    public string Name=>profile.Id;
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
    private T Evaluate<T>(Observation v,IReadOnlyList<T> choices,T baseline,int count,Func<CachedForwardV1,T,double> evaluate,string phase)
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
