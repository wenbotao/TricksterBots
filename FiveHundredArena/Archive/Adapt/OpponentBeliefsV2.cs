namespace FiveHundredArena;

public sealed class OpponentBeliefsV2
{
    private readonly double[][] bid=Enumerable.Range(0,4).Select(_=>new[]{.2,.6,.2}).ToArray();
    private readonly double[][] play=Enumerable.Range(0,4).Select(_=>new[]{.2,.6,.2}).ToArray();
    public int BidUpdates {get;private set;}
    public int PlayUpdates {get;private set;}
    public double[] BidWeights(int seat)=>bid[seat].ToArray();
    public double[] PlayWeights(int seat)=>play[seat].ToArray();
    internal void Update(int seat,double[] likelihood,bool playing=false,double evidence=.6)
    {
        var w=playing?play[seat]:bid[seat];double[] prior=[.2,.6,.2];
        for(int i=0;i<3;i++)w[i]=Math.Pow(w[i],.99)*Math.Pow(prior[i],.01)*Math.Pow(Math.Max(1e-6,likelihood[i]),evidence);
        double sum=w.Sum();for(int i=0;i<3;i++)w[i]=.04*prior[i]+.96*w[i]/sum;
        if(playing)PlayUpdates++;else BidUpdates++;
    }
    internal int Draw(int seat,SeededRandom random,bool playing=false)
    {double u=random.NextUInt()/4294967296.0;var w=playing?play[seat]:bid[seat];for(int i=0;i<2;i++){u-=w[i];if(u<0)return i;}return 2;}
}
