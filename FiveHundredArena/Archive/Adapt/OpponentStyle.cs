using Trickster.cloud;
namespace FiveHundredArena;

internal sealed class OpponentStyle(int bidStyle,int playStyle=1):IPlayer
{
    private readonly MediumBotPlayer medium=new();
    private RuleAwarePlayer? configured;
    private IPlayer Baseline(Observation v)=>v.Rules is ConfiguredRules?configured??=new RuleAwarePlayer(RuleBotLevel.Medium,1,medium,v.Rules):medium;
    public string Name=>$"style-b{bidStyle}-p{playStyle}";
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
    {
        return AdjustBid(v,legal,Baseline(v).Bid(v,legal),bidStyle);
    }
    internal static Contract?[] Predictions(Observation v,IReadOnlyList<Contract?> legal,IPlayer baseline)
    {var bid=baseline.Bid(v,legal);return Enumerable.Range(0,3).Select(s=>AdjustBid(v,legal,bid,s)).ToArray();}
    private static Contract? AdjustBid(Observation v,IReadOnlyList<Contract?> legal,Contract? baseline,int bidStyle)
    {
        if(bidStyle==1)return baseline;
        if(bidStyle==0)return baseline is {Kind:ContractKind.Normal} b?legal.LastOrDefault(c=>c?.Trump==b.Trump&&c.Kind==ContractKind.Normal&&c.Tricks<b.Tricks):null;
        if(baseline is {Kind:ContractKind.Normal} normal)return legal.FirstOrDefault(c=>c?.Kind==ContractKind.Normal&&c.Trump==normal.Trump&&c.Tricks>normal.Tricks)??baseline;
        if(baseline!=null)return baseline;
        return legal.Where(c=>c is {Kind:ContractKind.Normal}).Select(c=>c!).FirstOrDefault(c=>OpponentRules.HandValue(v,c)+2.8>=c.Tricks);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>Baseline(v).Discard(v,count);
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)
    {
        var baseline=Baseline(v).Play(v,legal);if(playStyle==1||v.Contract!.IsMisere||v.Trick.Count==0)return baseline;
        int partner=(v.Seat+2)%4;
        if(v.Rules.Winner(v.Trick,v.Contract)!=partner)return baseline;
        if(playStyle==0)return legal.Where(p=>v.Rules.Winner(v.Trick.Append(new PublicPlay(v.Seat,p)).ToArray(),v.Contract)==partner).OrderBy(p=>v.Rules.Rank(p.Card,v.Contract)).FirstOrDefault()??baseline;
        return legal.Where(p=>v.Rules.Winner(v.Trick.Append(new PublicPlay(v.Seat,p)).ToArray(),v.Contract)==v.Seat).OrderBy(p=>v.Rules.Rank(p.Card,v.Contract)).FirstOrDefault()??baseline;
    }
    internal static double Likelihood<T>(T action,T predicted,int alternatives)=>EqualityComparer<T>.Default.Equals(action,predicted)?.94+.06/alternatives:.06/alternatives;
}

/// <summary>Seat-specific, decayed Bayesian mixture. Only relative evidence is stored, never audit hands.</summary>
public sealed class OpponentBeliefs
{
    private readonly double[][] bid=Enumerable.Range(0,4).Select(_=>new[]{.2,.6,.2}).ToArray();
    private readonly double[][] play=Enumerable.Range(0,4).Select(_=>new[]{.2,.6,.2}).ToArray();
    public int BidUpdates {get;private set;}
    public int PlayUpdates {get;private set;}
    public double[] BidWeights(int seat)=>bid[seat].ToArray();
    public double[] PlayWeights(int seat)=>play[seat].ToArray();
    internal void Update(int seat,double[] likelihood,bool playing=false,double evidence=.2)
    {
        var w=playing?play[seat]:bid[seat];double[] prior=[.2,.6,.2];
        for(int i=0;i<3;i++)w[i]=Math.Pow(w[i],.97)*Math.Pow(prior[i],.03)*Math.Pow(Math.Max(1e-6,likelihood[i]),evidence);
        double sum=w.Sum();for(int i=0;i<3;i++)w[i]=.16*prior[i]+.84*w[i]/sum;
        if(playing)PlayUpdates++;else BidUpdates++;
    }
    internal int Draw(int seat,SeededRandom random,bool playing=false)
    {double u=random.NextUInt()/4294967296.0;var w=playing?play[seat]:bid[seat];for(int i=0;i<2;i++){u-=w[i];if(u<0)return i;}return 2;}
}
