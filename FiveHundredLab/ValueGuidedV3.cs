using FiveHundredArena;
namespace FiveHundredLab;

// Learned values rank/prioritize work; final choices use full, information-limited rollouts.
public sealed class ValueGuidedV3 : IPlayer
{
    private readonly CachedMediumV1 medium=new();
    private readonly ValueModelV1 model;
    private readonly SeededRandom random;
    public ValueGuidedV3(int seed,ValueModelV1 model)
    {
        if(model.Teacher!="belief-full16-diverse-phase-v2"||model.PhaseModels.Count==0)throw new ArgumentException("Requires the separately versioned phase-v2 model");
        this.model=model;random=new(seed);
    }
    public string Name=>"value-guided-v3@"+model.Digest;
    public AdaptiveSearchV1? LastSearch{get;private set;}
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
    {
        var baseline=medium.Bid(v,legal);
        var feasible=legal.Where(c=>c!=null&&!c.IsMisere&&LegacyFeaturesV1.HandValue(v.Hand,c,v.Rules)+2.5>=c.Tricks)
            .OrderByDescending(c=>model.Predict(v,"bid",c).Utility).Take(2).ToList();
        feasible.Remove(baseline);feasible.Insert(0,baseline);if(!feasible.Contains(null))feasible.Add(null);
        return Choose(v,feasible,baseline,4,(f,c)=>f.Bid(c),"bid");
    }
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count)
    {
        var baseline=medium.Discard(v,count).ToArray();
        var prior=LegacyFeaturesV1.DiscardSets(v.Hand,count).OrderByDescending(d=>LegacyFeaturesV1.RetainedValue(v.Hand.Except(d).ToArray(),v.Contract!,v.Rules)).Take(24).ToArray();
        double Value(PlayingCard[] d)
        {
            var retained=v.Hand.Except(d).ToArray();
            var players=v.Players.Select(p=>p.Seat==v.Seat?p with{CardCount=retained.Length}:p).ToArray();
            return model.Predict(v with{Hand=retained,Players=players,OwnDiscards=d},"play").Utility;
        }
        var choices=prior.OrderByDescending(Value).Take(3).Concat(prior.Take(2)).ToList();
        choices.RemoveAll(d=>d.ToHashSet().SetEquals(baseline));choices.Insert(0,baseline);
        var unique=new List<PlayingCard[]>();foreach(var d in choices)if(!unique.Any(x=>x.ToHashSet().SetEquals(d)))unique.Add(d);
        return Choose(v,unique,baseline,4,(f,d)=>f.Discard(d),"discard");
    }
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)
    {
        var baseline=medium.Play(v,legal);
        if(legal.Count==1||v.Trick.Count==(v.Contract!.IsMisere?2:3)){LastSearch=new(1,0,0,true,"play");return baseline;}
        var choices=new CardFrameV1(v).Representatives(legal,baseline).ToList();choices.Remove(baseline);choices.Insert(0,baseline);
        double p=model.Predict(v,"play").MadeProbability;
        int samples=v.Contract.IsMisere||p is >.2 and <.8?8:4;
        return Choose(v,choices,baseline,samples,(f,c)=>f.Play(c),"play");
    }
    private T Choose<T>(Observation v,IReadOnlyList<T> choices,T baseline,int count,Func<CachedForwardV1,T,double> evaluate,string phase)
    {
        if(choices.Count==1){LastSearch=new(1,0,0,true,phase);return baseline;}
        var worlds=BeliefSampler.Sample(v,count,random);double weight=worlds.Sum(w=>w.Weight);
        var values=worlds.Select(w=>evaluate(new(v,w),baseline)).ToArray();T chosen=baseline;double best=0;
        foreach(var candidate in choices.Skip(1))
        {
            var delta=worlds.Select((w,i)=>evaluate(new(v,w),candidate)-values[i]).ToArray();
            double mean=worlds.Select((w,i)=>w.Weight*delta[i]).Sum()/weight;
            double variance=worlds.Select((w,i)=>w.Weight*Math.Pow(delta[i]-mean,2)).Sum()/weight;
            double improvement=mean-12-Math.Sqrt(variance/Math.Max(1,count-1));
            if(improvement>best){best=improvement;chosen=candidate;}
        }
        LastSearch=new(choices.Count,count,choices.Count*count,false,phase);return chosen;
    }
}
