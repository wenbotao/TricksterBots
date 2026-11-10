using System.Text.Json;
using System.Text.Json.Nodes;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Trickster.cloud;
const string folder="experiments/rules-v1-1-ux-20261110";
var json=ArenaCommand.Json;
string[] profiles=["random-v1.1","random-pass-v1.1","weak-v1.1","medium-v1.1","fast-v1.1","strong-v1.1","partner-v1.1","strong-plus-v1.1","strong-flow-cached-v1.1"];
var rows=new List<object>();var contexts=new List<object>();var skipped=new List<object>();
foreach(var old in UxEvaluationV1.Build())
{
    Controller controller;
    try
    {
        var baseRules=(ConfiguredRules)RuleConfiguration.Load();var root=JsonNode.Parse(baseRules.Document)!;
        var desired=new List<PlayingCard>();for(int i=0;i<40;i++)desired.Add(old.InitialHands[(old.Scenario.Dealer+1+i)%4][i/4]);desired.AddRange(old.Kitty);
        var permutation=Enumerable.Range(0,43).ToArray();new SeededRandom(381127).Shuffle(permutation);
        var inverse=new PlayingCard[43];for(int i=0;i<43;i++)inverse[permutation[i]]=desired[i];
        string Encode(PlayingCard card)=>card.IsJoker?"X"+(card.Rank-15):(card.Rank switch{14=>"A",13=>"K",12=>"Q",11=>"J",_=>card.Rank.ToString()})+(card.Suit switch{Suit.Spades=>"S",Suit.Clubs=>"C",Suit.Diamonds=>"D",Suit.Hearts=>"H",_=>throw new InvalidDataException("Unknown suit")});
        root["deck"]!["cards"]=new JsonArray(inverse.Select(c=>(JsonNode?)JsonValue.Create(Encode(c))).ToArray());
        var fixture=(ConfiguredRules)RuleConfiguration.Parse(root.ToJsonString());
        var prefix=old.Scenario.Auction.ToList();
        if(old.Scenario.Phase=="play")
        {int declarer=old.Scenario.Auction.Last(a=>a.Bid!=null).Seat;prefix.Add(new(declarer,"discard",Discards:old.Kitty));prefix.AddRange(old.Scenario.Plays.Select(p=>new Decision(p.Seat,"play",Play:p.Action)));}
        controller=new(old,prefix,baseRules);
        try{new Engine(fixture).Run(Enumerable.Range(0,4).Select(_=>(IPlayer)new Actor(controller)).ToArray(),381127,1,old.Scenario.Dealer);}
        catch(Stop){}
        if(controller.View==null)throw new InvalidDataException("Target not reached");
    }
    catch(Exception ex){skipped.Add(new{old.Scenario.Id,Error=ex.Message});continue;}
    var view=controller.View!;
    contexts.Add(new{old.Scenario.Id,old.Scenario.Title,old.Scenario.Phase,View=view,Checks=old.Scenario.Checks.Select(c=>new{c.Id,c.Level,c.Expectation})});
    foreach(string profile in profiles)for(int trial=0;trial<4;trial++)
    {
        var v=view with{Hand=(trial%2==0?view.Hand:view.Hand.Reverse()).ToArray()};int seed=495119001+contexts.Count*7919+trial/2;
        var bot=Profiles.CreateForRules(profile,seed,v.Rules);UxActionV1? action=null;string? error=null;bool legal=false;
        try
        {
            if(old.Scenario.Phase=="bid"){var bid=bot.Bid(v,controller.Bids);action=new(Bid:bid);legal=controller.Bids.Contains(bid);}
            else if(old.Scenario.Phase=="discard"){var cards=bot.Discard(v,controller.Count).ToArray();action=new(Discards:cards);legal=cards.Length==controller.Count&&cards.Distinct().Count()==cards.Length&&cards.All(v.Hand.Contains);}
            else{var move=bot.Play(v,controller.Plays);action=new(Play:move);legal=controller.Plays.Contains(move);}
            if(!legal)error="Illegal action";
        }
        catch(Exception ex){error=ex.ToString();}
        var checks=old.Scenario.Checks.Select(check=>new{check.Id,check.Level,Passed=legal&&action!=null&&check.Accept(v,action)}).ToArray();
        rows.Add(new{Scenario=old.Scenario.Id,Profile=profile,Trial=trial,Seed=seed,Legal=legal,Error=error,Action=action,Checks=checks});
    }
    Console.WriteLine($"UX {contexts.Count}/17 {old.Scenario.Id}");
}
File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new{Profiles=profiles,Rules=RuleConfiguration.Load(),Scenarios=contexts,Skipped=skipped,Rows=rows,Method="Recapture original scripted card allocations through the new authoritative engine with an inverted deck permutation. Optional naming declined in script;each source prefix must be legal under v1.1. Canonical rule deck restored for bot sampling. Two seeds x two hand orders;probe results are not natural error frequencies;timing ignored."},json));
Console.WriteLine($"UX complete: {contexts.Count} contexts,{rows.Count} decisions,{skipped.Count} excluded prefixes");
sealed class Stop:Exception;
sealed class Controller(CapturedUxV1 fixture,List<Decision> prefix,ConfiguredRules canonical)
{
    int position;
    public Observation? View;public Contract?[] Bids=[];public PlayAction[] Plays=[];public int Count;
    public Decision Act(string phase,Observation view,IReadOnlyList<Contract?>? bids=null,IReadOnlyList<PlayAction>? plays=null,int count=0)
    {
        if(position==prefix.Count&&phase==fixture.Scenario.Phase&&view.Seat==0)
        {
            var source=(ConfiguredRules)view.Rules;var r=canonical with{PublicState=source.PublicState};
            var scores=fixture.Scenario.Scores??view.Scores.ToArray();View=view with{Rules=r,Scores=scores,Players=view.Players.Select(p=>p with{Score=scores[p.Seat%2]}).ToArray()};
            Bids=bids?.ToArray()??[];Plays=plays?.ToArray()??[];Count=count;throw new Stop();
        }
        if(position>=prefix.Count)throw new InvalidDataException("Unexpected scripted phase");
        var next=prefix[position++];if(next.Seat!=view.Seat||next.Phase!=phase)throw new InvalidDataException("Script mismatch");return next;
    }
}
sealed class Actor(Controller controller):IPlayer,IJokerDeclarationPlayer
{
    public string Name=>"new-rule-ux-script";
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>controller.Act("bid",v,bids:legal).Bid;
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>controller.Act("discard",v,count:count).Discards!;
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>controller.Act("play",v,plays:legal).Play!;
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>[];
}
