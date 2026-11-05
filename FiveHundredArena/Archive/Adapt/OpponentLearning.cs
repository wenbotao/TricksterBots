using System.Text.Json.Nodes;
namespace FiveHundredArena;

public sealed partial class OpponentStrong
{
    private string phase="bid";
    private readonly HashSet<int> learnedPlay=new();
    private double[][] roundPrior=Enumerable.Range(0,4).Select(_=>new[]{.2,.6,.2}).ToArray();
    private readonly int[] roundBidEvidence=new int[4],roundPlayEvidence=new int[4];
    private IReadOnlyList<SampledDeal>? initialSamples;
    private static bool SameRules(Rules a,Rules b)=>a is ConfiguredRules x&&b is ConfiguredRules y?x.Document==y.Document||JsonNode.DeepEquals(JsonNode.Parse(x.Document),JsonNode.Parse(y.Document)):a==b;
    public void ResetLearning(){Beliefs=new();observedRound=-1;bidsSeen=0;initialHand=null;initialSamples=null;}
    public void ObservePublic(Observation view)=>Observe(view);
    public void ObserveAuction(Observation view)
    {
        if(view.Hand.Count!=10||view.History.Count!=0||view.OwnDiscards.Count!=0)throw new ArgumentException("Expected pre-kitty auction observation");
        phase="bid";Observe(view,true);
    }
    internal double PublicBidProbability(Observation v,int actor,Contract? action,double[]? weights=null)
    {
        if(initialSamples==null)throw new InvalidOperationException("Observe own auction hand first");
        var likelihood=new double[3];
        foreach(var world in initialSamples)
        {
            var a=InitialView(v,actor,world.Hands[actor],v.Auction);var legal=OpponentRules.Bids(a);var predictions=OpponentStyle.Predictions(a,legal,Baseline(a));
            for(int i=0;i<3;i++)likelihood[i]+=OpponentStyle.Likelihood(action,predictions[i],legal.Count)/initialSamples.Count;
        }
        weights??=Beliefs.BidWeights(actor);return weights.Select((w,i)=>w*likelihood[i]).Sum();
    }
    private void Observe(Observation v,bool bidding=false)
    {
        if(!SameRules(bound,v.Rules))throw new ArgumentException("OpponentStrong and engine rules differ");
        if(!learningEnabled)return;
        if(observedRound>v.Round||bidding&&observedRound==v.Round&&initialHand!=null&&!initialHand.ToHashSet().SetEquals(v.Hand))ResetLearning();
        if(observedRound!=v.Round)
        {
            observedRound=v.Round;bidsSeen=0;initialHand=null;initialSamples=null;Array.Clear(roundBidEvidence);Array.Clear(roundPlayEvidence);learnedPlay.Clear();
            roundPrior=Enumerable.Range(0,4).Select(Beliefs.BidWeights).ToArray();
        }
        if(bidding&&initialHand==null&&v.Hand.Count==10)
        {
            initialHand=v.Hand.ToArray();var sampled=new List<SampledDeal>();
            for(int k=0;k<8;k++)
            {
                var unknown=v.Rules.Deck().Except(initialHand).ToArray();learningRandom.Shuffle(unknown);int pos=0;
                var hands=Enumerable.Range(0,4).Select(s=>s==v.Seat?initialHand.ToArray():unknown.Skip((pos++)*10).Take(10).ToArray()).ToArray();
                sampled.Add(new(hands,unknown.Skip(30).ToArray(),1));
            }
            initialSamples=sampled;
        }
        // No completed-hand audit feed. Learning integrates unknown original hands using only the
        // observer's original cards. Future exposed/post-kitty hands never masquerade as bid-time hands.
        for(int i=bidsSeen;i<v.Auction.Count;i++)
        {
            var action=v.Auction[i];if(action.Seat==v.Seat||initialSamples==null||roundBidEvidence[action.Seat]>=2)continue;
            var likelihood=new double[3];
            foreach(var world in initialSamples)
            {
                var actor=InitialView(v,action.Seat,world.Hands[action.Seat],v.Auction.Take(i).ToArray());var legal=OpponentRules.Bids(actor);
                if(!legal.Contains(action.Bid))continue;
                var predictions=OpponentStyle.Predictions(actor,legal,Baseline(actor));
                for(int s=0;s<3;s++)likelihood[s]+=OpponentStyle.Likelihood(action.Bid,predictions[s],legal.Count)/initialSamples.Count;
            }
            Beliefs.Update(action.Seat,likelihood);roundBidEvidence[action.Seat]++;
        }
        bidsSeen=v.Auction.Count;
        if(stage>=OpponentAdaptationStage.Play&&v.Contract!=null)
        {
            // Only directly observable, non-compulsory takeover opportunities are classified. A
            // low card with no legal alternative supplies no evidence about cooperation.
            int active=v.Rules is ConfiguredRules cr?ConfiguredRules.Text(cr.ContractOptions(v.Contract),"activeSeats")=="all_four"?4:3:v.Contract.IsMisere?3:4;
            for(int i=0;i<v.History.Count;i++)
            {
                var p=v.History[i];if(learnedPlay.Contains(i)||p.Seat==v.Seat||i%active==0||v.Contract.IsMisere||roundPlayEvidence[p.Seat]>=2)continue;
                var prefix=v.History.Skip(i/active*active).Take(i%active).ToArray();int partner=(p.Seat+2)%4;
                if(v.Rules.Winner(prefix,v.Contract)!=partner)continue;
                // A previous observed choice is eligible only if both its actual card and a known
                // later-played card were already in hand (no draws occur during play).
                var later=v.History.Skip(i+1).Where(x=>x.Seat==p.Seat).Select(x=>x.Action.Card).Concat(v.Players[p.Seat].ExposedHand??[]).Distinct().ToArray();
                bool Covers(PlayingCard c)=>v.Rules.Winner(prefix.Append(new PublicPlay(p.Seat,new(c))).ToArray(),v.Contract)==p.Seat;
                var sameSuit=later.Where(c=>v.Rules.EffectiveSuit(c,v.Contract)==v.Rules.EffectiveSuit(p.Action.Card,v.Contract)).ToArray();
                var context=v.Rules is ConfiguredRules config?config with{History=v.History.Take(i).ToArray(),CurrentSeat=p.Seat,Declarations=new(config.Declarations)}:v.Rules;
                bool Alternative(PlayingCard card)
                {
                    if(Covers(card)==Covers(p.Action.Card))return false;
                    var choices=context.LegalPlays([p.Action.Card,card],prefix,v.Contract);return choices.Any(x=>x.Card==p.Action.Card)&&choices.Any(x=>x.Card==card);
                }
                if(p.Action.Card.IsJoker||!sameSuit.Any(Alternative))continue;
                bool takeover=Covers(p.Action.Card);double[] probability=takeover?[.12,.35,.85]:[.88,.65,.15];
                Beliefs.Update(p.Seat,probability,true,.3);learnedPlay.Add(i);roundPlayEvidence[p.Seat]++;
            }
            // Revisit incomplete evidence on later turns, but never learn the same action twice.
            // Eligible indices are recorded separately; unresolved choices are revisited.
        }

    }
    private static Observation InitialView(Observation root,int actor,PlayingCard[] hand,IReadOnlyList<AuctionAction> prefix)
    {
        var high=prefix.LastOrDefault(a=>a.Bid!=null&&a.Bid.Kind!=ContractKind.Inkle);
        Rules rules=root.Rules is ConfiguredRules c?c with{History=[],Declarations=new(),CurrentSeat=actor,DeclarerSeat=high?.Seat??-1}:root.Rules;
        return root with{Seat=actor,Hand=hand,Players=Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,10,root.Scores[s%2],prefix.Where(a=>a.Seat==s).Select(a=>a.Bid).ToArray(),0,[],null)).ToArray(),Auction=prefix,Contract=high?.Bid,Declarer=high?.Seat,Trick=[],History=[],OwnDiscards=[],Rules=rules};
    }
    internal double[] WorldBidWeights(Observation v,SampledDeal world,int seat)
    {
        var weights=roundPrior[seat].ToArray();
        if(seat==v.Seat||phase!="bid"&&seat==v.Declarer)return weights;
        var original=world.Hands[seat].Concat(v.History.Where(p=>p.Seat==seat).Select(p=>p.Action.Card)).ToArray();
        if(original.Length!=10)return weights;
        for(int i=0;i<v.Auction.Count;i++)
        {
            var a=v.Auction[i];if(a.Seat!=seat)continue;var actor=InitialView(v,seat,original,v.Auction.Take(i).ToArray());var legal=OpponentRules.Bids(actor);
            var predictions=OpponentStyle.Predictions(actor,legal,Baseline(actor));
            for(int s=0;s<3;s++)weights[s]*=OpponentStyle.Likelihood(a.Bid,predictions[s],legal.Count);
        }
        return weights;
    }
    internal IReadOnlyList<SampledDeal> Reweight(Observation v,IReadOnlyList<SampledDeal> worlds)
    {
        var logs=worlds.Select(w=>Enumerable.Range(0,4).Where(s=>s!=v.Seat).Sum(s=>Math.Log(Math.Max(1e-12,WorldBidWeights(v,w,s).Sum())))).ToArray();double max=logs.Max();double power=1;
        double[] Weights()=>logs.Select(x=>Math.Exp((x-max)*power)).ToArray();var weights=Weights();
        while(power>.125&&Math.Pow(weights.Sum(),2)/weights.Sum(x=>x*x)<Math.Min(2,worlds.Count)){power/=2;weights=Weights();}
        LastEffectiveSamples=Math.Pow(weights.Sum(),2)/weights.Sum(x=>x*x);
        return worlds.Select((w,i)=>w with{Weight=weights[i]}).ToArray();
    }
    public double LastEffectiveSamples {get;private set;}
    private int DrawWorldStyle(Observation v,SampledDeal world,int seat)
    {
        if(stage<OpponentAdaptationStage.Belief)return Beliefs.Draw(seat,random);
        var weights=WorldBidWeights(v,world,seat);double u=random.NextUInt()/4294967296.0*weights.Sum();
        for(int i=0;i<2;i++){u-=weights[i];if(u<0)return i;}return 2;
    }
}
