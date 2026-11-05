namespace FiveHundredArena;

// Full auction and ten-trick continuation, with policies fixed for one sampled world.
internal sealed class OpponentForward
{
    private readonly Observation root;
    private readonly IPlayer[] policies;
    private readonly Rules rules;
    private readonly List<PlayingCard>[] hands,taken;
    private readonly List<AuctionAction> auction;
    private readonly List<PublicPlay> history,trick;
    private readonly int[] tricks;
    private readonly List<PlayingCard>[] discards=Enumerable.Range(0,4).Select(_=>new List<PlayingCard>()).ToArray();
    private readonly PlayingCard[] dead;
    private Contract? contract;
    private int declarer;
    private bool exposed;
    internal OpponentForward(Observation view,SampledDeal sample,IPlayer[] policies)
    {
        root=view;this.policies=policies;rules=view.Rules is ConfiguredRules c?c with{History=view.History.ToArray(),Declarations=new(c.Declarations)}:view.Rules;
        hands=sample.Hands.Select(h=>h.ToList()).ToArray();dead=sample.Dead.ToArray();taken=view.Players.Select(p=>p.Taken.ToList()).ToArray();auction=view.Auction.ToList();history=view.History.ToList();trick=view.Trick.ToList();tricks=view.Players.Select(p=>p.Tricks).ToArray();contract=view.Contract;declarer=view.Declarer??-1;
        exposed=declarer>=0&&view.Players[declarer].ExposedHand!=null;discards[view.Seat].AddRange(view.OwnDiscards);
    }
    private bool TakeKitty()=>rules is not ConfiguredRules c||ConfiguredRules.Text(c.ContractOptions(contract!),"kitty")=="take_and_discard";
    private int Active=>rules is ConfiguredRules c?ConfiguredRules.Text(c.ContractOptions(contract!),"activeSeats")=="all_four"?4:3:contract!.IsMisere?3:4;
    private int Next(int seat){do{seat=(seat+1)%4;}while(Active==3&&seat==(declarer+2)%4);return seat;}
    private Observation View(int seat)
    {
        if(rules is ConfiguredRules c){c.History=history.ToArray();c.CurrentSeat=seat;c.DeclarerSeat=declarer;}
        var players=root.Players.Select(p=>p with{CardCount=hands[p.Seat].Count,Bids=auction.Where(a=>a.Seat==p.Seat).Select(a=>a.Bid).ToArray(),Tricks=tricks[p.Seat],Taken=taken[p.Seat].ToArray(),ExposedHand=exposed&&p.Seat==declarer?hands[p.Seat].ToArray():null}).ToArray();
        return root with{Seat=seat,Hand=hands[seat].ToArray(),Players=players,Auction=auction.ToArray(),Contract=contract,Declarer=declarer<0?null:declarer,History=history.ToArray(),Trick=trick.ToArray(),Rules=rules,OwnDiscards=discards[seat].ToArray()};
    }
    internal double Bid(Contract? choice)
    {
        var passed=new bool[4];int consecutive=0;
        foreach(var a in auction){if(a.Bid==null){passed[a.Seat]=true;consecutive++;}else if(a.Bid.Kind!=ContractKind.Inkle)consecutive=0;}
        void Apply(int seat,Contract? bid){auction.Add(new(seat,bid));if(bid==null){passed[seat]=true;consecutive++;}else if(bid.Kind!=ContractKind.Inkle){contract=bid;declarer=seat;consecutive=0;}}
        bool Done()=>rules is ConfiguredRules c&&ConfiguredRules.Text(c.Section("auction"),"mode")=="single_round"?auction.Count>=4:contract==null?consecutive>=4:rules.PassIsPermanent?Enumerable.Range(0,4).All(s=>s==declarer||passed[s]):consecutive>=3;
        Apply(root.Seat,choice);int seat=(root.Seat+1)%4;
        while(!Done())
        {
            if(!rules.PassIsPermanent||!passed[seat]){var v=View(seat);var legal=OpponentRules.Bids(v);Apply(seat,policies[seat].Bid(v,legal));}seat=(seat+1)%4;
        }
        if(contract==null)return 0;
        if(TakeKitty()){hands[declarer].AddRange(dead);Remove(declarer,policies[declarer].Discard(View(declarer),dead.Length));}
        return StartPlay();
    }
    private void Remove(int seat,IReadOnlyList<PlayingCard> cards){foreach(var c in cards){if(!hands[seat].Remove(c))throw new InvalidOperationException("Simulated discard absent");discards[seat].Add(c);}}
    internal double Discard(IReadOnlyList<PlayingCard> choice){Remove(root.Seat,choice);return StartPlay();}
    private double StartPlay()
    {
        int seat=declarer;
        if(rules is ConfiguredRules c){var o=c.ContractOptions(contract!);seat=ConfiguredRules.Text(o,"firstLead") switch{"left_of_declarer"=>(declarer+1)%4,"left_of_dealer"=>(root.Dealer+1)%4,_=>declarer};exposed=ConfiguredRules.Text(o.GetProperty("exposure"),"timing")=="before_first_lead";}
        if(Active==3&&seat==(declarer+2)%4)seat=Next(seat);return Finish(seat);
    }
    internal double Play(PlayAction choice){if(root.Seat!=declarer&&TakeKitty())discards[declarer].AddRange(dead);Add(root.Seat,choice);return Finish(Next(root.Seat));}
    private void Add(int seat,PlayAction choice){if(!hands[seat].Remove(choice.Card))throw new InvalidOperationException("Simulation card absent");var p=new PublicPlay(seat,choice);trick.Add(p);history.Add(p);}
    private double Finish(int seat)
    {
        while(tricks.Sum()<10)
        {
            if(trick.Count==Active){seat=rules.Winner(trick,contract!);tricks[seat]++;taken[seat].AddRange(trick.Select(p=>p.Action.Card));trick.Clear();if(rules is ConfiguredRules c?ConfiguredRules.Text(c.ContractOptions(contract!).GetProperty("exposure"),"timing")=="after_first_trick":contract!.Kind==ContractKind.OpenMisere)exposed=true;continue;}
            var v=View(seat);var legal=rules.LegalPlays(v.Hand,v.Trick,contract!);var move=policies[seat].Play(v,legal);if(!legal.Contains(move))throw new InvalidOperationException("Policy returned illegal simulated play");Add(seat,move);seat=Next(seat);
        }
        if(rules is ConfiguredRules configured)return RuleRollout.Utility(root,configured,contract!,declarer,tricks);
        var(made,delta)=rules.Score(contract!,declarer,tricks);var scores=root.Scores.Select((s,i)=>s+delta[i]).ToArray();var winner=rules.WinningTeam(scores,declarer%2,made);int team=root.Seat%2;
        return winner.HasValue?(winner==team?1:-1)*4.0*rules.WinScore:delta[team]-delta[1-team];
    }
}
internal static class OpponentRules
{
    internal static IReadOnlyList<Contract?> Bids(Observation v)
    {
        var legal=v.Rules.LegalBids(v.Contract,v.Auction);
        if(v.Rules is ConfiguredRules r)
        {
            var a=r.Section("auction");int position=(v.Seat-v.Dealer+3)%4;int min=a.GetProperty("normalMinimumByPosition")[position].GetInt32();legal=legal.Where(b=>b==null||b.Kind!=ContractKind.Normal||b.Tricks>=min).ToList();
            var inkle=a.GetProperty("inkle");if(v.Contract==null&&ConfiguredRules.Flag(inkle,"enabled")&&inkle.GetProperty("allowedPositions").EnumerateArray().Any(x=>x.GetInt32()==position))legal.AddRange(inkle.GetProperty("strains").EnumerateArray().Select(s=>new Contract(6,ConfiguredRules.SuitOf(s.GetString()!),ContractKind.Inkle)));
        }
        return legal;
    }
    internal static double HandValue(Observation v,Contract c)=>v.Rules is ConfiguredRules r?RuleAwarePlayer.HandValue(v.Hand,c,r):HandEvaluator.HandValue(v.Hand,c,v.Rules);
}
