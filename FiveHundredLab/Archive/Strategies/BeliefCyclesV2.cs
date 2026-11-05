using FiveHundredArena;
namespace FiveHundredLab;

// Symmetric accepted cycles preserve counts, fixed cards and public voids. Finite mixing is approximate.
public static class BeliefCyclesV2
{
    public static IReadOnlyList<SampledDeal> Sample(Observation v,int count,SeededRandom random)
    {
        if(count<1)throw new ArgumentOutOfRangeException(nameof(count));
        var initial=BeliefSampler.Sample(v,1,random)[0];var voids=BeliefSampler.Voids(v);
        var cards=new List<PlayingCard>();var owners=new List<int>();
        for(int seat=0;seat<4;seat++)if(seat!=v.Seat && v.Players[seat].ExposedHand==null)
            foreach(var card in initial.Hands[seat]){cards.Add(card);owners.Add(seat);}
        foreach(var card in initial.Dead){cards.Add(card);owners.Add(-1);}
        bool Allowed(PlayingCard card,int seat)=>seat<0||v.Contract==null||!voids[seat].Contains(v.Rules.EffectiveSuit(card,v.Contract));
        bool constrained=voids.Any(x=>x.Count>0);
        void Cycle()
        {
            if(cards.Count<2)return;
            int n=2+random.Next(Math.Min(5,cards.Count)-1);var indices=new List<int>();
            while(indices.Count<n){int i=random.Next(cards.Count);if(!indices.Contains(i))indices.Add(i);}
            int direction=random.Next(2)==0?1:n-1;
            for(int i=0;i<n;i++)if(!Allowed(cards[indices[(i+direction)%n]],owners[indices[i]]))return;
            var values=indices.Select(i=>cards[i]).ToArray();for(int i=0;i<n;i++)cards[indices[i]]=values[(i+direction)%n];
        }
        if(constrained)for(int i=0;i<256;i++)Cycle();
        var output=new List<SampledDeal>();
        for(int sample=0;sample<count;sample++)
        {
            if(constrained){for(int i=0;i<96;i++)Cycle();}else random.Shuffle(cards);
            var hands=Enumerable.Range(0,4).Select(s=>s==v.Seat||v.Players[s].ExposedHand!=null?initial.Hands[s].ToList():new List<PlayingCard>()).ToArray();var dead=new List<PlayingCard>();
            for(int i=0;i<cards.Count;i++)if(owners[i]<0)dead.Add(cards[i]);else hands[owners[i]].Add(cards[i]);
            double log=0;
            foreach(var group in v.Auction.Where(a=>a.Seat!=v.Seat&&a.Bid!=null).GroupBy(a=>a.Seat))
            {
                var bid=group.Last().Bid!;var approximate=hands[group.Key].Concat(v.History.Where(p=>p.Seat==group.Key).Select(p=>p.Action.Card)).ToArray();
                log+=bid.IsMisere?Math.Clamp(.08*(LegacyFeaturesV1.HandValue(approximate,bid,v.Rules)+6),-.7,.7):Math.Clamp(.18*(LegacyFeaturesV1.HandValue(approximate,bid,v.Rules)-(bid.Tricks-2.5)),-.7,.7);
            }
            output.Add(new(hands.Select(h=>h.ToArray()).ToArray(),dead.ToArray(),Math.Exp(Math.Clamp(log,-2,2))));
        }
        return output;
    }
}
