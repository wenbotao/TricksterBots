#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiveHundredArena;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
using Bots=FiveHundredLab.Bots.FiveHundred.V11;
namespace TestBots;

[TestClass]
public class TestV11Compliance
{
    static readonly PlayingCard Joker=new(Suit.Joker,16);
    static readonly string[] BotProfiles=["random-v1.1","random-pass-v1.1","weak-v1.1","medium-v1.1","fast-v1.1","strong-v1.1","partner-v1.1","strong-plus-v1.1","strong-flow-cached-v1.1"];
    static string? AuditPath=>Environment.GetEnvironmentVariable("V11_COMPLIANCE_AUDIT");
    [ClassInitialize] public static void Init(TestContext context)
    { if(AuditPath is { } path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var f=new FileStream(path,FileMode.CreateNew);} }
    static void Log(string family,string id,object detail)
    {if(AuditPath is { } path)File.AppendAllText(path,JsonSerializer.Serialize(new{Family=family,Id=id,Detail=detail,Passed=true},ArenaCommand.Json)+"\n");}
    static ConfiguredRules Edit(ConfiguredRules source,Action<JsonNode> edit)
    {var n=JsonNode.Parse(source.Document)!;edit(n);return (ConfiguredRules)RuleConfiguration.Parse(n.ToJsonString());}
    static GameRecord Run(ConfiguredRules rules,IPlayer[] actors,int seed=601119001,int rounds=1,int dealer=3)
    {var game=new Engine(rules).Run(actors,seed,rounds,dealer);ArenaCommand.Replay(JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(game,ArenaCommand.Json),ArenaCommand.Json)!);return game;}
    static IEnumerable<int[]> Distributions()
    {for(int a=0;a<=10;a++)for(int b=0;b<=10-a;b++)for(int c=0;c<=10-a-b;c++)yield return [a,b,c,10-a-b-c];}

    [TestMethod][DataRow("australian")][DataRow("american")]
    public void ZeroScoringUsesTheCorrectSeatOrTeamTarget(string region)
    {
        var r=RuleConfigurationV11.Create(region,new(){MisereWithoutPickup=true,GrandNullo=true});int assertions=0;
        foreach(var (kind,points) in new[]{(ContractKind.Misere,150),(ContractKind.OpenMisere,250),(ContractKind.MisereNoPickup,350),(ContractKind.GrandNullo,510)})
        foreach(int declarer in Enumerable.Range(0,4))foreach(var tricks in Distributions())
        {
            bool made=kind==ContractKind.GrandNullo?tricks[declarer]+tricks[(declarer+2)%4]==0:tricks[declarer]==0;
            var actual=r.Score(new(0,Suit.Unknown,kind),declarer,tricks);int[] delta=[0,0];delta[declarer%2]=made?points:-points;
            if(region=="american")delta[1-declarer%2]=10*tricks[declarer];
            Assert.AreEqual(made,actual.Made);CollectionAssert.AreEqual(delta,actual.Delta);assertions++;
        }
        Log("scoring","zero/"+region,new{Distributions=assertions});
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void SoloSuccessFailureAndNoContractPointsAreSymmetric(bool nt)
    {
        var r=RuleConfigurationV11.Create(options:new(){GrandSlam=true});int cases=0;
        foreach(int seat in Enumerable.Range(0,4))foreach(var tricks in Distributions())
        {
            var c=new Contract(10,nt?Suit.Unknown:Suit.Hearts,ContractKind.GrandSlam);var actual=r.Score(c,seat,tricks);
            int[] expected=[0,0];expected[seat%2]=tricks[seat]==10?(nt?520:500):-500;expected[1-seat%2]=(10-tricks[seat])*10;
            CollectionAssert.AreEqual(expected,actual.Delta);Assert.AreEqual(tricks[seat]==10,actual.Made);cases++;
        }
        var free=RuleConfigurationV11.Create("american",new(){AllPass="play_no_contract_nt"});
        foreach(var tricks in Distributions())foreach(int seat in Enumerable.Range(0,4))
        {CollectionAssert.AreEqual(new[]{10*(tricks[0]+tricks[2]),10*(tricks[1]+tricks[3])},free.Score(new(0,Suit.Unknown,ContractKind.NoContractNT),seat,tricks).Delta);cases++;}
        Log("scoring","solo-and-free/"+nt,new{Distributions=cases});
    }
    [TestMethod][DataRow("any")][DataRow("never_led")][DataRow("previously_led_by_holder")][DataRow("not_shown_void")][DataRow("last_card_only")]
    public void JokerLeadSetsRespectGlobalHistoryAndActingHolder(string mode)
    {
        var r=RuleConfigurationV11.Create(options:new(){JokerLead=mode});var nt=new Contract(6,Suit.Unknown);
        r.CurrentSeat=1;r.DeclarerSeat=0;
        r.History=[new(1,new(new(Suit.Clubs,7))),new(2,new(new(Suit.Clubs,8))),new(3,new(new(Suit.Clubs,9))),new(0,new(new(Suit.Clubs,10))),new(2,new(new(Suit.Diamonds,6))),new(3,new(new(Suit.Diamonds,7))),new(0,new(new(Suit.Diamonds,8))),new(1,new(new(Suit.Hearts,4)))];
        Suit[] expected=mode switch{"never_led"=>[Suit.Spades,Suit.Hearts],"previously_led_by_holder"=>[Suit.Clubs],"not_shown_void"=>[Suit.Spades,Suit.Clubs,Suit.Hearts],"last_card_only"=>[],_=>Rules.Suits.ToArray()};
        var legal=r.LegalPlays([Joker,new(Suit.Spades,5)],[],nt);
        CollectionAssert.AreEquivalent(expected,legal.Where(p=>p.Card==Joker).Select(p=>p.JokerLeadSuit).ToArray());
        if(mode=="last_card_only")Assert.AreEqual(4,r.LegalPlays([Joker],[],nt).Count);
        Assert.IsTrue(legal.Any(p=>p.Card==new PlayingCard(Suit.Spades,5)));
        Log("joker-lead",mode,new{Expected=expected,Actual=legal});
    }
    [TestMethod][DataRow("only_when_void",false)][DataRow("anytime",false)][DataRow("first_lead_of_suit_or_void",false)][DataRow("only_when_void",true)][DataRow("anytime",true)][DataRow("first_lead_of_suit_or_void",true)]
    public void JokerFollowAndForcedVoidConstraintsCompose(string mode,bool misere)
    {
        foreach(bool held in new[]{false,true})foreach(bool previous in new[]{false,true})foreach(bool force in new[]{false,true})
        {
            var r=RuleConfigurationV11.Create(options:new(){JokerFollow=mode});string key=misere?"misere":"normal";
            r=Edit(r,n=>n["contracts"]![key]!["joker"]!["forceWhenVoid"]=force);
            var c=misere?new Contract(0,Suit.Unknown,ContractKind.Misere):new Contract(6,Suit.Unknown);
            PublicPlay[] trick=[new(0,new(new(Suit.Hearts,8)))];
            r.History=previous?(misere?[new(0,new(new(Suit.Hearts,4))),new(1,new(new(Suit.Hearts,5))),new(3,new(new(Suit.Hearts,6))),..trick]:[new(0,new(new(Suit.Hearts,4))),new(1,new(new(Suit.Hearts,5))),new(2,new(new(Suit.Hearts,6))),new(3,new(new(Suit.Hearts,7))),..trick]):trick;
            PlayingCard[] hand=held?[Joker,new(Suit.Hearts,9),new(Suit.Clubs,5)]:[Joker,new(Suit.Clubs,5)];
            var legal=r.LegalPlays(hand,trick,c);bool jokerAllowed=!held||mode=="anytime"||mode=="first_lead_of_suit_or_void"&&!previous;
            Assert.AreEqual(jokerAllowed,legal.Any(p=>p.Card==Joker));
            Assert.AreEqual(held||!force,legal.Any(p=>!p.Card.IsJoker));
            if(held)Assert.IsTrue(legal.Where(p=>!p.Card.IsJoker).All(p=>p.Card.Suit==Suit.Hearts));
            Log("joker-follow",$"{mode}/{misere}/{held}/{previous}/{force}",new{Legal=legal});
        }
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void NamedJokerHasOneSuitAndSurvivesObservationRoundTrip(bool low)
    {
        var r=RuleConfigurationV11.Create(options:new(){JokerLowInMisere=low});r.Declarations[Joker]=Suit.Clubs;r.DeclarationOwners[Joker]=2;r.CurrentSeat=1;r.DeclarerSeat=0;
        var c=new Contract(0,Suit.Unknown,ContractKind.Misere);
        Assert.AreEqual(Suit.Clubs,r.EffectiveSuit(Joker,c));
        Assert.AreEqual(2,r.Winner([new(0,new(new(Suit.Clubs,14))),new(2,new(Joker))],c));
        Assert.AreEqual(0,r.Winner([new(0,new(new(Suit.Hearts,4))),new(2,new(Joker))],c));
        var restored=JsonSerializer.Deserialize<Rules>(JsonSerializer.Serialize<Rules>(r,ArenaCommand.Json),ArenaCommand.Json) as ConfiguredRules;
        Assert.IsNotNull(restored);Assert.AreEqual(2,restored.DeclarationOwners[Joker]);Assert.AreEqual(Suit.Clubs,restored.EffectiveSuit(Joker,c));
        Log("nomination","named/"+low,new{Owner=restored.DeclarationOwners[Joker]});
    }
    [TestMethod][DataRow(Suit.Spades,Suit.Clubs)][DataRow(Suit.Clubs,Suit.Spades)][DataRow(Suit.Hearts,Suit.Diamonds)][DataRow(Suit.Diamonds,Suit.Hearts)]
    public void LeftBowerFollowsTrumpRatherThanPrintedSuit(Suit trump,Suit left)
    {
        var r=RuleConfigurationV11.Create();var c=new Contract(6,trump);var bower=new PlayingCard(left,11);var native=new PlayingCard(left,left is Suit.Spades or Suit.Clubs?5:4);
        Assert.AreEqual(trump,r.EffectiveSuit(bower,c));
        CollectionAssert.AreEqual(new[]{native},r.LegalPlays([bower,native],[new(0,new(new(left,8)))],c).Select(p=>p.Card).ToArray());
        Assert.AreEqual(1,r.Winner([new(0,new(new(trump,14))),new(1,new(bower))],c));
        Log("bower",trump.ToString(),new{Trump=trump,Printed=left});
    }
    [TestMethod][DataRow("australian")][DataRow("american")]
    public void AllRankingModesAndGrandEligibilityHaveExactBoundaries(string region)
    {
        var open=new Contract(0,Suit.Unknown,ContractKind.OpenMisere);
        foreach(string mode in new[]{"between_9NT_10S","between_10D_10H","by_points_higher","by_points_lower","by_points_first"})
        {
            var r=RuleConfigurationV11.Create(region,new(){OpenMisereRanking=mode});var normal=new Contract(10,Suit.Hearts);
            Assert.AreEqual(mode=="by_points_first"?0:mode=="by_points_higher"?1:-1,Math.Sign(r.Order(open)-r.Order(normal)));
            if(mode=="by_points_first")Assert.IsFalse(r.LegalBids(normal,[new(1,normal)]).Contains(open));
            Log("ranking",region+"/"+mode,new{Open=r.Order(open),Normal=r.Order(normal)});
        }
        var grand=RuleConfigurationV11.Create(region,new(){GrandNullo=true});var g=new Contract(0,Suit.Unknown,ContractKind.GrandNullo);grand.CurrentSeat=0;
        Assert.IsTrue(grand.LegalBids(new(0,Suit.Unknown,ContractKind.Misere),[new(2,new(0,Suit.Unknown,ContractKind.Misere))]).Contains(g));
        Assert.IsFalse(grand.LegalBids(null,[new(1,new(0,Suit.Unknown,ContractKind.Misere))]).Contains(g));
        Assert.IsFalse(grand.LegalBids(null,[]).Contains(g));Log("ranking",region+"/grand-partner",new{OnlyPartner=true});
    }
    [TestMethod][DataRow("bid")][DataRow("discard-count")][DataRow("discard-duplicate")][DataRow("discard-foreign")][DataRow("play")][DataRow("nomination")][DataRow("duplicate-nomination")][DataRow("transfer")]
    public void InvalidActionsAreRejectedWithoutScoringTheRound(string defect)
    {
        var r=RuleConfigurationV11.Create(options:new(){GrandSlam=defect=="transfer"});
        var actors=Enumerable.Range(0,4).Select(_=>(IPlayer)new Probe(defect:defect)).ToArray();
        var ex=Assert.Throws<IllegalActionException>(()=>new Engine(r).Run(actors,602119001,1,3));
        var partial=ex.Data["record"] as GameRecord;Assert.IsNotNull(partial);Assert.IsNull(partial.Winner);Assert.AreEqual(0,partial.Rounds[0].Scores.Length);
        Log("negative",defect,new{Rejected=ex.Message,PartialRounds=partial.Rounds.Count});
    }
    [TestMethod][DataRow("australian",false)][DataRow("australian",true)][DataRow("american",false)][DataRow("american",true)]
    public void AllPassRotationAndNoContractDoNotInventAContractor(string region,bool rotate)
    {
        var rules=RuleConfigurationV11.Create(region,new(){RotateDealerOnRedeal=rotate});var bots=Enumerable.Range(0,4).Select(_=>(IPlayer)new Probe(pass:true)).ToArray();
        var game=Run(rules,bots,rounds:3);CollectionAssert.AreEqual(rotate?new[]{3,0,1}:new[]{3,3,3},game.Rounds.Select(r=>r.Dealer).ToArray());
        var free=Run(RuleConfigurationV11.Create(region,new(){AllPass="play_no_contract_nt"}),bots);
        Assert.IsNull(free.Rounds[0].Declarer);Assert.AreEqual(100,free.Rounds[0].ScoreDelta.Sum());
        Log("pass",region+"/"+rotate,new{Redeals=3,FreeScores=free.Rounds[0].ScoreDelta});
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void PlayerObservationMutationCannotChangeEngineState(bool open)
    {
        var r=RuleConfigurationV11.Create();GameRecord Make(bool mutate)=>Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Probe(open:open,mutate:mutate)).ToArray());
        var a=Make(false);var b=Make(true);Assert.AreEqual(JsonSerializer.Serialize(a,ArenaCommand.Json),JsonSerializer.Serialize(b,ArenaCommand.Json));
        Log("isolation","mutation/"+open,new{IdenticalRecords=true});
    }
    [TestMethod][DataRow("normal")][DataRow("open")][DataRow("no-pickup")][DataRow("grand")][DataRow("solo")]
    public void CompletePhaseAndVisibilityProtocolWorksForEveryDeclarerSeat(string contract)
    {
        foreach(int dealer in Enumerable.Range(0,4))
        {
            var r=RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=contract is "no-pickup" or "grand",GrandNullo=contract=="grand",GrandSlam=contract=="solo"});var views=new List<Observation>();
            var actors=Enumerable.Range(0,4).Select(_=>(IPlayer)new Probe(contract:contract,views:views)).ToArray();
            var game=Run(r,actors,dealer:dealer);var round=game.Rounds[0];int declarer=round.Declarer!.Value;
            Assert.AreEqual(10,round.TrickWinners.Count);Assert.AreEqual(contract is "open" or "no-pickup" or "solo"?30:40,round.Decisions.Count(d=>d.Phase=="play"));
            Assert.IsTrue(views.Where(v=>v.Seat%2!=declarer%2).All(v=>v.OwnDiscards.Count==0&&v.PrivateEvents!.Count==0));
            if(contract=="open")foreach(var view in views)Assert.AreEqual(view.History.Count>=3,view.Players[declarer].ExposedHand!=null);
            if(contract=="grand")
            {
                CollectionAssert.AreEqual(new[]{declarer,(declarer+2)%4},round.Decisions.Where(d=>d.Phase=="discard").Select(d=>d.Seat).ToArray());
                Assert.IsTrue(views.Where(v=>v.Seat==declarer).All(v=>v.OwnDiscards.Count==0));
            }
            if(contract=="no-pickup")Assert.IsTrue(views.Where(v=>v.Seat==declarer).All(v=>!v.PrivateEvents!.Any(e=>e.Phase=="pickup")));
            if(contract=="solo")Assert.AreEqual(4,round.Decisions.Single(d=>d.Phase=="discard").Discards!.Length);
            Log("protocol",contract+"/dealer-"+dealer,new{Declarer=declarer,Plays=round.Decisions.Count(d=>d.Phase=="play"),Replayed=true});
        }
    }
    [TestMethod]
    public void ContradictorySpecialContractSettingsFailAtLoadTime()
    {
        var rules=RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=true,GrandNullo=true,GrandSlam=true});
        foreach(var (contract,field,value) in new[]{("misere_no_pickup","kitty","take_and_discard"),("misere_no_pickup","target","team_zero"),("grand_nullo","activeSeats","exclude_declarer_partner"),("grand_nullo","target","declarer_zero"),("grand_nullo","kitty","untouched"),("grand_slam","target","team_zero"),("grand_slam","activeSeats","all_four")})
        {
            Assert.Throws<ArgumentException>(()=>Edit(rules,n=>n["contracts"]![contract]![field]=value));
            Log("configuration",contract+"/"+field,new{Rejected=value});
        }
        Assert.Throws<ArgumentException>(()=>Edit(rules,n=>n["extensions"]!["grandSlamEnabled"]=false));
        Assert.Throws<ArgumentException>(()=>RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=true,OpenMisereRanking="by_points_higher"}));
        Assert.Throws<ArgumentException>(()=>RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=true,MisereRanking="by_points"}));
        Log("configuration","feature-switch-consistency",new{Rejected=3});
    }
    [TestMethod]
    public void ReplayRejectsTamperedScoringAndHiddenDeal()
    {
        var game=Run(RuleConfigurationV11.Create(),Enumerable.Range(0,4).Select(_=>(IPlayer)new Probe()).ToArray());
        foreach(string field in new[]{"Scores","TrickWinners","Kitty"})
        {
            var doc=JsonNode.Parse(JsonSerializer.Serialize(game,ArenaCommand.Json))!;
            if(field=="Kitty")doc["Rounds"]![0]![field]![0]!["Rank"]=2;
            else doc["Rounds"]![0]![field]![0]=999;
            var altered=JsonSerializer.Deserialize<GameRecord>(doc.ToJsonString(),ArenaCommand.Json)!;
            Assert.Throws<InvalidDataException>(()=>ArenaCommand.Replay(altered));Log("replay","tamper/"+field,new{Rejected=true});
        }
    }
    // Independent arithmetic oracle: the five strains have 20-point steps, levels 100-point steps.
    [TestMethod][DataRow("australian")][DataRow("american")]
    public void NormalScoringMatchesTheTableAndSlamFloorForAllTrickDistributions(string region)
    {
        var rules=RuleConfigurationV11.Create(region);Suit[] strains=[Suit.Spades,Suit.Clubs,Suit.Diamonds,Suit.Hearts,Suit.Unknown];int cases=0;
        for(int level=6;level<=10;level++)for(int strain=0;strain<strains.Length;strain++)
        foreach(int seat in Enumerable.Range(0,4))foreach(var tricks in Distributions())
        {
            int won=tricks[seat]+tricks[(seat+2)%4],value=100*(level-6)+40+20*strain;bool made=won>=level;
            int[] expected=[0,0];expected[seat%2]=made?(won==10?Math.Max(250,value):value):-value;expected[1-seat%2]=10*(10-won);
            var actual=rules.Score(new(level,strains[strain]),seat,tricks);Assert.AreEqual(made,actual.Made);CollectionAssert.AreEqual(expected,actual.Delta);cases++;
        }
        Log("scoring","normal/"+region,new{Distributions=cases});
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void InkleSignalsNeitherBecomeContractsNorCountAsThreePasses(bool force)
    {
        // A custom prior-seven gate makes the Inkle/history interaction observable.
        var rules=Edit(RuleConfigurationV11.Create("american",new(){AllPass=force?"force_dealer":"redeal"}),n=>n["contracts"]!["misere"]!["eligibility"]!["minimumPriorNormalLevel"]=7);
        var actors=Enumerable.Range(0,4).Select(_=>(IPlayer)new InkleProbe()).ToArray();var game=Run(rules,actors);
        var round=game.Rounds[0];Assert.AreEqual(2,round.Decisions.Count(d=>d.Bid?.Kind==ContractKind.Inkle));
        // The requirement says THREE PASSES. Inkle is a signal bid, not a pass;
        // with two Inkles the forced-dealer condition is not met, and no contract exists.
        Assert.IsTrue(round.Redeal);Assert.IsNull(round.Contract);
        Log("inkle","signal/"+force,new{Forced=force,Replayed=true});
    }
    [TestMethod][DataRow("australian")][DataRow("american")]
    public void EveryBotPlaysNamedJokersAndSpecialContractsAcrossBothPresets(string region)
    {
        string[] scenarios=["named-nt","low-open","no-pickup","grand","solo","free"];
        foreach(string profile in BotProfiles)foreach(string scenario in scenarios)
        {
            var rules=RuleConfigurationV11.Create(region,new(){MisereWithoutPickup=scenario is "no-pickup" or "grand",GrandNullo=scenario=="grand",GrandSlam=scenario=="solo",JokerNomination=scenario=="named-nt",JokerLowInMisere=scenario=="low-open",JokerFollow=scenario=="low-open"?"first_lead_of_suit_or_void":"only_when_void",AllPass=scenario=="free"?"play_no_contract_nt":"redeal"});
            var actors=Enumerable.Range(0,4).Select(seat=>(IPlayer)new ScenarioPlayer(Profiles.CreateForRules(profile,605119001+seat,rules),scenario)).ToArray();
            var game=Run(rules,actors,seed:606119001+Array.IndexOf(scenarios,scenario),dealer:Array.IndexOf(BotProfiles,profile)%4);
            var round=game.Rounds[0];Assert.AreEqual(10,round.TrickWinners.Count);Assert.IsFalse(round.Redeal);
            Log("bot-scenarios",region+"/"+profile+"/"+scenario,new{Contract=round.Contract,Declarer=round.Declarer,Replayed=true});
        }
    }
    sealed class InkleProbe:IPlayer
    {
        readonly Probe rest=new();public string Name=>"inkle-probe";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
        {
            if(v.Auction.Count<2)return legal.First(b=>b?.Kind==ContractKind.Inkle);
            Assert.IsFalse(legal.Any(b=>b?.Kind==ContractKind.Misere));
            return legal.Contains(null)?null:legal.First();
        }
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>rest.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>rest.Play(v,legal);
    }
    // Only the auction, optional nomination and solo switch are scripted. All substantive
    // discards, card transfers and plays come from the actual bot under the resolved rules.
    sealed class ScenarioPlayer(IPlayer inner,string scenario):IPlayer,IJokerDeclarationPlayer,IRuleVariationPlayer
    {
        readonly Probe auction=new(contract:scenario switch{"low-open"=>"open","named-nt"=>"normal",_=>scenario},pass:scenario=="free");
        public string Name=>inner.Name+"-compliance";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>auction.Bid(v,legal);
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>scenario=="named-nt"?v.Hand.Where(c=>c.IsJoker).Select(c=>new PlayAction(c,Suit.Clubs)).ToArray():[];
        public bool RequestRedeal(Observation v)=>false;
        public bool DeclareGrandSlam(Observation v)=>scenario=="solo";
        public PlayingCard TransferCard(Observation v,IReadOnlyList<PlayingCard> legal)=>((IRuleVariationPlayer)inner).TransferCard(v,legal);
    }
    [TestMethod]
    public void EveryBotCompliesWithForcedDealerInsteadOfReturningIllegalPass()
    {
        foreach(string profile in BotProfiles)
        {
            var r=RuleConfigurationV11.Create(options:new(){AllPass="force_dealer"});var inner=Profiles.CreateForRules(profile,603119001,r);
            var game=Run(r,Enumerable.Range(0,4).Select(s=>(IPlayer)new BidOnly(inner,s)).ToArray());Assert.AreEqual(3,game.Rounds[0].Declarer);
            Log("bot-legality",profile+"/forced-dealer",new{Bid=game.Rounds[0].Contract});
        }
    }
    [TestMethod][DataRow("nt")][DataRow("low-zero")][DataRow("grand")][DataRow("solo")]
    public void CachedContinuationPreservesNamedAndSpecialContractActions(string type)
    {
        var r=RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=type is "low-zero" or "grand",GrandNullo=type=="grand",GrandSlam=type=="solo",JokerLowInMisere=type=="low-zero"});
        var views=new List<Observation>();Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Probe(contract:type=="low-zero"?"no-pickup":type=="nt"?"normal":type,views:views,nominate:type=="nt")).ToArray());int comparisons=0;
        foreach(var view in views.Where((v,i)=>i%13==0))
        {
            var source=(ConfiguredRules)view.Rules;var context=new Bots.VariantFlowData(source);
            foreach(var sample in Bots.VariantSamples.Sample(view,source,1,new(604119001+view.History.Count)))
            foreach(var action in source.LegalPlays(view.Hand,view.Trick,view.Contract!).Take(2))
            {
                var a=new List<PublicPlay>();var b=new List<PublicPlay>();var x=Bots.VariantRollout.Play(view,source,sample,action,null,a.Add);var y=Bots.VariantRollout.Play(view,source,sample,action,context,b.Add);
                Assert.AreEqual(x,y);CollectionAssert.AreEqual(a,b);comparisons++;
            }
        }
        Assert.IsTrue(comparisons>0);Log("cache",type,new{ComparedTraces=comparisons});
    }
    sealed class BidOnly(IPlayer inner,int seat):IPlayer
    {
        readonly Probe rest=new(pass:true);public string Name=>"bid-only";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>seat==3?inner.Bid(v,legal):null;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>rest.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>rest.Play(v,legal);
    }
    sealed class Probe(string? defect=null,bool pass=false,bool open=false,bool mutate=false,string contract="normal",List<Observation>? views=null,bool nominate=false):IPlayer,IJokerDeclarationPlayer,IRuleVariationPlayer
    {
        public string Name=>"compliance-probe";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
        {
            if(defect=="bid")return new(11,Suit.Hearts);if(pass)return legal.Contains(null)?null:legal.First();
            if(contract=="grand")return v.Auction.Count==0?new(7,Suit.Spades):v.Auction.Count==1?new(0,Suit.Unknown,ContractKind.Misere):v.Auction.Count==3?new(0,Suit.Unknown,ContractKind.GrandNullo):null;
            if(open||contract=="open")return v.Auction.Count==0?new(8,Suit.Spades):v.Auction.Count==1?new(0,Suit.Unknown,ContractKind.OpenMisere):null;
            return v.Auction.Count==0?contract=="no-pickup"?new(0,Suit.Unknown,ContractKind.MisereNoPickup):legal.First(b=>b?.Kind==ContractKind.Normal&&b.Trump==Suit.Unknown):null;
        }
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)
        {
            if(defect=="discard-count")return v.Hand.Take(count-1).ToArray();
            if(defect=="discard-duplicate")return Enumerable.Repeat(v.Hand[0],count).ToArray();
            if(defect=="discard-foreign")return v.Hand.Take(count-1).Append(new PlayingCard(Suit.Spades,2)).ToArray();
            return v.Hand.OrderBy(c=>c.IsJoker?1:0).Take(count).ToArray();
        }
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)
        {
            views?.Add(v);if(defect=="play")return new(new(Suit.Spades,2));
            if(mutate)
            {
                ((PlayingCard[])v.Hand)[0]=new(Suit.Spades,2);Array.Fill((int[])v.Scores,9999);
                foreach(var p in v.Players){if(p.Taken.Count>0)((PlayingCard[])p.Taken)[0]=new(Suit.Spades,2);if(p.ExposedHand is PlayingCard[] h&&h.Length>0)h[0]=new(Suit.Spades,2);}
                var r=(ConfiguredRules)v.Rules;r.Declarations.Clear();r.History=[];
                foreach(var memory in v.PrivateEvents??[])if(memory.Cards is PlayingCard[] a&&a.Length>0)a[0]=new(Suit.Spades,2);
            }
            return legal[0];
        }
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)
        {
            if(defect=="nomination")return [new(Joker,Suit.Unknown)];
            var moves=v.Hand.Where(c=>c.IsJoker).Select(c=>new PlayAction(c,Suit.Clubs)).ToArray();
            return defect=="duplicate-nomination"?moves.Concat(moves).ToArray():nominate?moves:[];
        }
        public bool RequestRedeal(Observation v)=>false;
        public bool DeclareGrandSlam(Observation v)=>defect=="transfer"||contract=="solo";
        public PlayingCard TransferCard(Observation v,IReadOnlyList<PlayingCard> legal)=>defect=="transfer"?new(Suit.Spades,2):legal[0];
    }
}
