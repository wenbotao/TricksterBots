using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Usability;

public static class UxCaptureV1
{
    private const int DealSeed = 381127;
    public static CapturedUxV1 Capture(UxScenarioV1 spec)
    {
        var deck = new Rules().Deck();
        var hands = Enumerable.Range(0,4).Select(s => spec.ReservedHands.GetValueOrDefault(s,[]).ToList()).ToArray();
        foreach(var p in spec.Plays) if(!hands[p.Seat].Contains(p.Action.Card)) hands[p.Seat].Add(p.Action.Card);
        var reserved = hands.SelectMany(h=>h).ToArray();
        if(reserved.Distinct().Count()!=reserved.Length||reserved.Any(c=>!deck.Contains(c))||hands.Any(h=>h.Count>10))throw new InvalidDataException(spec.Id+": duplicate/invalid reserved cards or overfull hand");
        var kitty = spec.Kitty ?? deck.Except(reserved).TakeLast(3).ToArray();
        if(kitty.Length!=3||kitty.Distinct().Count()!=3||kitty.Any(c=>reserved.Contains(c)||!deck.Contains(c)))throw new InvalidDataException(spec.Id+": invalid kitty");
        // Give the scripted human's preceding spade bid a plausible trump holding when capacity permits.
        if(spec.Auction.Any(d=>d.Seat==2&&d.Bid is {IsMisere:false,Trump:Suit.Spades,Tricks:>=7}))
        {
            var taken=hands.SelectMany(h=>h).Concat(kitty).ToHashSet();var contract=new Contract(7,Suit.Spades);var r=new Rules();
            foreach(var c in deck.Except(taken).Where(c=>r.EffectiveSuit(c,contract)==Suit.Spades).OrderByDescending(c=>r.Rank(c,contract)).Take(Math.Min(6,10-hands[2].Count)))hands[2].Add(c);
        }
        var used=hands.SelectMany(h=>h).Concat(kitty).ToHashSet();var remaining=new Queue<PlayingCard>(deck.Except(used));
        foreach(var h in hands)while(h.Count<10)h.Add(remaining.Dequeue());
        if(remaining.Count!=0)throw new InvalidDataException(spec.Id+": not a complete 43-card deal");
        var desired=new List<PlayingCard>();for(int i=0;i<40;i++)desired.Add(hands[(spec.Dealer+1+i)%4][i/4]);desired.AddRange(kitty);
        var permutation=Enumerable.Range(0,43).ToArray();new SeededRandom(DealSeed).Shuffle(permutation);
        var inverse=new PlayingCard[43];for(int i=0;i<43;i++)inverse[permutation[i]]=desired[i];
        var prefix=spec.Auction.ToList();
        if(spec.Phase=="play")
        {
            int declarer=spec.Auction.Last(d=>d.Bid!=null).Seat;
            prefix.Add(new(declarer,"discard",Discards:kitty));prefix.AddRange(spec.Plays.Select(p=>new Decision(p.Seat,"play",Play:p.Action)));
        }
        var controller=new Controller(spec,prefix,hands.Select(h=>h.ToArray()).ToArray(),kitty);
        try { new Engine(new FixtureDeck(inverse)).Run(Enumerable.Range(0,4).Select(s=>(IPlayer)new Actor(s,controller)).ToArray(),DealSeed,1,spec.Dealer); }
        catch(IllegalActionException ex) when(controller.Captured!=null && ex.InnerException is CapturedStop) { return controller.Captured; }
        throw new InvalidDataException(spec.Id+": target was not reached through the legal script");
    }
    private sealed record FixtureDeck(PlayingCard[] Fixed) : Rules
    { public override List<PlayingCard> Deck()=>Fixed.ToList(); }
    private sealed class CapturedStop : Exception;
    private sealed class Controller(UxScenarioV1 spec,IReadOnlyList<Decision> prefix,PlayingCard[][] hands,PlayingCard[] kitty)
    {
        private int position;
        public CapturedUxV1? Captured {get;private set;}
        public Decision Act(int seat,string phase,Observation v,IReadOnlyList<Contract?>? bids=null,IReadOnlyList<PlayAction>? plays=null,int count=0)
        {
            if(position==prefix.Count)
            {
                if(seat!=0||phase!=spec.Phase)throw new InvalidDataException(spec.Id+$": wrong target seat/phase: {seat}/{phase}");
                // Strip the inverse-deal rule object: it must never reach the player under evaluation.
                var scores=spec.Scores??v.Scores.ToArray();
                v=v with{Rules=new Rules(),Scores=scores.ToArray(),Players=v.Players.Select(p=>p with{Score=scores[p.Seat%2]}).ToArray()};
                Captured=new(spec,v,bids?.ToArray()??[],plays?.ToArray()??[],count,hands,kitty);throw new CapturedStop();
            }
            var d=prefix[position++];if(d.Seat!=seat||d.Phase!=phase)throw new InvalidDataException(spec.Id+$": script mismatch at {position-1}, got {seat}/{phase}, expected {d.Seat}/{d.Phase}");return d;
        }
    }
    private sealed class Actor(int seat,Controller controller):IPlayer
    {
        public string Name=>seat==2?"scripted-human-seat-2":"scripted-context-seat-"+seat;
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>controller.Act(seat,"bid",v,bids:legal).Bid;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>controller.Act(seat,"discard",v,count:count).Discards!;
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>controller.Act(seat,"play",v,plays:legal).Play!;
    }
}
