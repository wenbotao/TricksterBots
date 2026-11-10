#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
using Bots = FiveHundredLab.Bots.FiveHundred.V11;
namespace TestBots;

[TestClass]
public class TestRulesV11
{
    [TestMethod]
    public void WorkbookDefaultsAndFeatureBundlesAreResolved()
    {
        var au=(ConfiguredRules)RuleConfiguration.Load();var us=RuleConfigurationV11.Create("american");
        Assert.IsTrue(au.IsV11);Assert.AreEqual(43,au.Deck().Count);Assert.AreEqual(43,us.Deck().Count);
        var open=new Contract(0,Suit.Unknown,ContractKind.OpenMisere);var misere=new Contract(0,Suit.Unknown,ContractKind.Misere);
        Assert.IsTrue(au.Order(new(10,Suit.Diamonds))<au.Order(open)&&au.Order(open)<au.Order(new(10,Suit.Hearts)));
        Assert.IsTrue(us.Order(new(8,Suit.Spades))<us.Order(misere)&&us.Order(misere)<us.Order(new(8,Suit.Clubs)));
        Assert.AreEqual("after_first_trick",ConfiguredRules.Text(au.ContractOptions(open).GetProperty("exposure"),"timing"));
        Assert.AreEqual("holder_optional",ConfiguredRules.Text(au.ContractOptions(misere).GetProperty("joker"),"predeclare"));
        Assert.AreEqual("not_shown_void",ConfiguredRules.Text(us.ContractOptions(misere).GetProperty("joker"),"leadSuits"));
        Assert.IsTrue(us.AllowOpenMisere);Assert.IsTrue(us.LegalBids(null,[]).Contains(open));
        var extra=RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=true,GrandNullo=true,GrandSlam=true});
        var no=new Contract(0,Suit.Unknown,ContractKind.MisereNoPickup);
        Assert.AreEqual(150,extra.Value(misere));Assert.AreEqual(250,extra.Value(open));Assert.AreEqual(350,extra.Value(no));
        Assert.IsTrue(extra.Order(new(7,Suit.Spades))<extra.Order(misere)&&extra.Order(misere)<extra.Order(new(7,Suit.Clubs)));
        Assert.IsTrue(extra.Order(new(9,Suit.Spades))<extra.Order(no)&&extra.Order(no)<extra.Order(new(9,Suit.Clubs)));
    }
    [TestMethod]
    public void AllPassModesAndRedealRotation()
    {
        foreach(bool rotate in new[]{true,false})
        {
            var r=RuleConfigurationV11.Create(options:new(){RotateDealerOnRedeal=rotate});
            var game=Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Script()).ToArray(),3);
            CollectionAssert.AreEqual(rotate?new[]{3,0,1}:new[]{3,3,3},game.Rounds.Select(x=>x.Dealer).ToArray());
            Assert.IsTrue(game.Rounds.All(x=>x.Redeal));
        }
        var nt=Run(RuleConfigurationV11.Create(options:new(){AllPass="play_no_contract_nt"}),Enumerable.Range(0,4).Select(_=>(IPlayer)new Script()).ToArray());
        Assert.AreEqual(ContractKind.NoContractNT,nt.Rounds[0].Contract!.Kind);Assert.AreEqual(100,nt.Rounds[0].ScoreDelta.Sum());Assert.AreEqual(40,nt.Rounds[0].Decisions.Count(d=>d.Phase=="play"));
        var forced=Run(RuleConfigurationV11.Create(options:new(){AllPass="force_dealer"}),Enumerable.Range(0,4).Select(_=>(IPlayer)new Script()).ToArray());
        Assert.IsNotNull(forced.Rounds[0].Contract);Assert.AreEqual(3,forced.Rounds[0].Declarer);
    }
    [TestMethod]
    public void AceNoFaceIsExactlyOneAceAndRequestOccursBeforeAuction()
    {
        var r=RuleConfigurationV11.Create(options:new(){AceNoFace=true});
        Assert.IsTrue(r.AceNoFace([new(Suit.Spades,14),new(Suit.Clubs,5)]));
        Assert.IsFalse(r.AceNoFace([new(Suit.Spades,5)]));Assert.IsFalse(r.AceNoFace([new(Suit.Spades,14),new(Suit.Hearts,14)]));
        Assert.IsFalse(r.AceNoFace([new(Suit.Spades,14),new(Suit.Clubs,11)]));Assert.IsFalse(r.AceNoFace([new(Suit.Spades,14),new(Suit.Joker,16)]));
        bool found=false;
        for(int seed=1;seed<=300&&!found;seed++)
        {
            var game=Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Script(request:true)).ToArray(),seed:seed);
            if(game.Rounds[0].Decisions.FirstOrDefault()?.Phase=="redeal-request")
            { Assert.IsTrue(game.Rounds[0].Redeal);Assert.IsTrue(game.Rounds[0].Decisions.All(d=>d.Phase!="bid"));found=true; }
        }
        Assert.IsTrue(found);
    }
    [TestMethod]
    public void OpenExposureFollowsConfiguredTiming()
    {
        foreach(string timing in new[]{"before_first_lead","after_first_trick"})
        {
            var r=RuleConfigurationV11.Create(options:new(){OpenTiming=timing});var views=new List<Observation>();
            var game=Run(r,Enumerable.Range(0,4).Select(s=>(IPlayer)new Script(new(0,Suit.Unknown,ContractKind.OpenMisere),views)).ToArray());
            int declarer=game.Rounds[0].Declarer!.Value;
            Assert.AreEqual(30,views.Count);
            Assert.AreEqual(timing=="before_first_lead",views[0].Players[declarer].ExposedHand!=null);
            Assert.IsNotNull(views.First(v=>v.History.Count==3).Players[declarer].ExposedHand);
            Assert.IsNull(views[0].Players[(declarer+2)%4].ExposedHand);
        }
    }
    [TestMethod]
    public void NoPickupAndGrandNulloHaveDifferentPrivateExchangeFlows()
    {
        var r=RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=true,GrandNullo=true});
        var no=Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Script(new(0,Suit.Unknown,ContractKind.MisereNoPickup))).ToArray());
        Assert.AreEqual(0,no.Rounds[0].Decisions.Single(d=>d.Phase=="discard").Discards!.Length);Assert.AreEqual(30,no.Rounds[0].Decisions.Count(d=>d.Phase=="play"));
        var views=new List<Observation>();var grand=Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Script(grand:true,views:views)).ToArray());
        var round=grand.Rounds[0];Assert.AreEqual(ContractKind.GrandNullo,round.Contract!.Kind);Assert.AreEqual(3,round.Declarer);
        CollectionAssert.AreEqual(new[]{3,1},round.Decisions.Where(d=>d.Phase=="discard").Select(d=>d.Seat).ToArray());Assert.AreEqual(40,round.Decisions.Count(d=>d.Phase=="play"));
        Assert.AreEqual(3,views.First(v=>v.Seat==1).OwnDiscards.Count);Assert.AreEqual(0,views.First(v=>v.Seat==3).OwnDiscards.Count);
        Assert.IsTrue(views.Where(v=>v.Seat%2==0).All(v=>v.PrivateEvents!.Count==0));
        CollectionAssert.AreEqual(new[]{0,-510},r.Score(round.Contract,3,[5,0,4,1]).Delta);
    }
    [TestMethod]
    public void GrandSlamTransfersOneCardAndDiscardsOneExtra()
    {
        var r=RuleConfigurationV11.Create(options:new(){GrandSlam=true});
        var game=Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Script(new(6,Suit.Hearts),solo:true)).ToArray());
        var round=game.Rounds[0];Assert.AreEqual(ContractKind.GrandSlam,round.Contract!.Kind);
        Assert.AreEqual(1,round.Decisions.Count(d=>d.Phase=="transfer"));Assert.AreEqual(2,round.Decisions.Single(d=>d.Phase=="transfer").Seat);
        Assert.AreEqual(4,round.Decisions.Single(d=>d.Phase=="discard").Discards!.Length);Assert.AreEqual(30,round.Decisions.Count(d=>d.Phase=="play"));
        CollectionAssert.AreEqual(new[]{500,0},r.Score(new(10,Suit.Hearts,ContractKind.GrandSlam),0,[10,0,0,0]).Delta);
        CollectionAssert.AreEqual(new[]{520,0},r.Score(new(10,Suit.Unknown,ContractKind.GrandSlam),0,[10,0,0,0]).Delta);
        CollectionAssert.AreEqual(new[]{-500,20},r.Score(new(10,Suit.Hearts,ContractKind.GrandSlam),0,[8,1,0,1]).Delta);
    }
    [TestMethod]
    public void JokerNominationAllowsDefendersAndSamplerKeepsPublicOwner()
    {
        var r=RuleConfigurationV11.Create();bool found=false;
        for(int seed=1;seed<=20&&!found;seed++)
        {
            var views=new List<Observation>();var game=Run(r,Enumerable.Range(0,4).Select(_=>(IPlayer)new Script(new(6,Suit.Unknown),views,nominate:true)).ToArray(),seed:seed);
            var declaration=game.Rounds[0].Decisions.FirstOrDefault(d=>d.Phase=="declare"&&d.Seat!=0);
            if(declaration==null)continue;
            found=true;var view=views.First(v=>v.Seat!=declaration.Seat&&v.History.Count==0);
            var restored=JsonSerializer.Deserialize<Observation>(JsonSerializer.Serialize(view,ArenaCommand.Json),ArenaCommand.Json)!;
            var actual=(ConfiguredRules)restored.Rules;
            Assert.AreEqual(declaration.Seat,actual.DeclarationOwners[declaration.Play!.Card]);
            view=restored;
            foreach(var sample in Bots.VariantSamples.Sample(view,actual,8,new(61)))
                Assert.IsTrue(sample.Hands[declaration.Seat].Contains(declaration.Play!.Card));
        }
        Assert.IsTrue(found);
    }
    [TestMethod]
    public void HolderLeadHistoryFirstSuitFollowAndLowJokerAreIndependent()
    {
        var r=RuleConfigurationV11.Create(options:new(){JokerLead="previously_led_by_holder"});var nt=new Contract(6,Suit.Unknown);PlayingCard joker=new(Suit.Joker,16);
        r.CurrentSeat=1;r.DeclarerSeat=0;r.History=[new(1,new(new(Suit.Clubs,7))),new(2,new(new(Suit.Clubs,8))),new(3,new(new(Suit.Clubs,9))),new(0,new(new(Suit.Clubs,10)))];
        CollectionAssert.AreEqual(new[]{Suit.Clubs},r.LegalPlays([joker],[],nt).Select(p=>p.JokerLeadSuit).ToArray());
        var first=RuleConfigurationV11.Create(options:new(){JokerFollow="first_lead_of_suit_or_void"});
        PublicPlay[] trick=[new(0,new(new(Suit.Hearts,8)))];Assert.AreEqual(2,first.LegalPlays([joker,new(Suit.Hearts,9)],trick,nt).Count);
        first.History=[new(0,new(new(Suit.Hearts,5))),new(1,new(new(Suit.Hearts,6))),new(2,new(new(Suit.Hearts,7))),new(3,new(new(Suit.Hearts,8))),..trick];
        Assert.AreEqual(1,first.LegalPlays([joker,new(Suit.Hearts,9)],trick,nt).Count);
        var low=RuleConfigurationV11.Create(options:new(){JokerLowInMisere=true});
        Assert.AreEqual(0,low.Winner([new(0,new(new(Suit.Hearts,4))),new(1,new(joker))],new(0,Suit.Unknown,ContractKind.Misere)));
    }
    [TestMethod]
    public void AllNineCurrentBotsRunAgainstNewDefaultAndReplay()
    {
        foreach(string profile in ProfilesV11())
        {
            var r=(ConfiguredRules)RuleConfiguration.Load();var bot=Profiles.CreateForRules(profile,41,r);
            Assert.IsTrue(bot.Name.EndsWith("v1.1"));
            var game=new Engine().Run([bot,new Bots.MediumBot(42),new Bots.WeakBot(43),new Bots.FastBot(44)],461119001,1,3);
            ArenaCommand.Replay(game);
        }
    }
    [TestMethod]
    public void CachedAndUncachedVariantContinuationActionsAndUtilitiesMatch()
    {
        var rules=RuleConfigurationV11.Create();var views=new List<Observation>();
        Run(rules,Enumerable.Range(0,4).Select(_=>(IPlayer)new Script(new(6,Suit.Hearts),views)).ToArray());
        int compared=0;
        foreach(var view in views.Where((_,i)=>i%7==0))
        {
            var r=(ConfiguredRules)view.Rules;var data=new Bots.VariantFlowData(r);
            foreach(var world in Bots.VariantSamples.Sample(view,r,2,new(7919+view.History.Count)))
            foreach(var action in r.LegalPlays(view.Hand,view.Trick,view.Contract!).Take(2))
            {
                var a=new List<PublicPlay>();var b=new List<PublicPlay>();
                var first=Bots.VariantRollout.Play(view,r,world,action,null,a.Add);
                var second=Bots.VariantRollout.Play(view,r,world,action,data,b.Add);
                Assert.AreEqual(first,second);CollectionAssert.AreEqual(a,b);compared++;
            }
        }
        Assert.IsTrue(compared>=10);
    }
    [TestMethod]
    public void EveryBotCompletesSpecialContractsWithLegalActions()
    {
        var r=RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=true,GrandNullo=true,GrandSlam=true});
        foreach(string profile in ProfilesV11())
        foreach(int variant in new[]{0,1,2})
        {
            IPlayer[] actors=Enumerable.Range(0,4).Select(s=>(IPlayer)new ForcedVariant(Profiles.CreateForRules(profile,200+s,r),variant)).ToArray();
            var game=Run(r,actors,seed:472119001+variant);
            Assert.AreEqual(variant==0?ContractKind.MisereNoPickup:variant==1?ContractKind.GrandNullo:ContractKind.GrandSlam,game.Rounds[0].Contract!.Kind);
            Assert.AreEqual(10,game.Rounds[0].TrickWinners.Count);
        }
    }
    private sealed class ForcedVariant(IPlayer inner,int variant):IPlayer,IJokerDeclarationPlayer,IRuleVariationPlayer
    {
        public string Name=>inner.Name+"-scripted-auction";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>variant==1
            ?v.Auction.Count==0?new(7,Suit.Spades):v.Auction.Count==1?new(0,Suit.Unknown,ContractKind.Misere):v.Auction.Count==3?new(0,Suit.Unknown,ContractKind.GrandNullo):null
            :v.Auction.Count==0?variant==0?new(0,Suit.Unknown,ContractKind.MisereNoPickup):new(6,Suit.Hearts):null;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>((IJokerDeclarationPlayer)inner).DeclareJokers(v);
        public bool RequestRedeal(Observation v)=>false;
        public bool DeclareGrandSlam(Observation v)=>variant==2;
        public PlayingCard TransferCard(Observation v,IReadOnlyList<PlayingCard> legal)=>((IRuleVariationPlayer)inner).TransferCard(v,legal);
    }
    [TestMethod]
    public void RankingsAndNoContractTerminalAreUnambiguous()
    {
        var open=new Contract(0,Suit.Unknown,ContractKind.OpenMisere);
        var first=RuleConfigurationV11.Create(options:new(){OpenMisereRanking="by_points_first"});
        Assert.AreEqual(first.Order(new(10,Suit.Hearts)),first.Order(open));
        Assert.IsFalse(first.LegalBids(new(10,Suit.Hearts),[new(1,new(10,Suit.Hearts))]).Contains(open));
        var placement=RuleConfigurationV11.Create(options:new(){OpenMisereRanking="between_9NT_10S"});
        Assert.IsTrue(placement.Order(new(9,Suit.Unknown))<placement.Order(open)&&placement.Order(open)<placement.Order(new(10,Suit.Spades)));
        Assert.Throws<ArgumentException>(()=>RuleConfigurationV11.Create(options:new(){MisereWithoutPickup=true,MisereRanking="by_points"}));
        var us=RuleConfigurationV11.Create("american",new(){AllPass="play_no_contract_nt"});var nt=new Contract(0,Suit.Unknown,ContractKind.NoContractNT);
        Assert.AreEqual(0,us.WinnerForContract(nt,3,[510,480],false));Assert.AreEqual(1,us.WinnerForContract(nt,0,[480,510],false));
        Assert.IsNull(us.WinnerForContract(nt,3,[510,510],false));
    }
    [TestMethod]
    public void OldPlayersCanDeclineOptionalRedealAndStillReplay()
    {
        var rules=RuleConfigurationV11.Create(options:new(){AceNoFace=true});bool observed=false;
        for(int seed=1;seed<=300&&!observed;seed++)
        {
            var game=Run(rules,Enumerable.Range(0,4).Select(_=>(IPlayer)new BasicOldPlayer()).ToArray(),seed:seed);
            var requests=game.Rounds[0].Decisions.Where(d=>d.Phase=="redeal-request").ToArray();
            if(requests.Length>0){Assert.IsTrue(requests.All(d=>d.Accepted==false));observed=true;}
        }
        Assert.IsTrue(observed);
    }
    private sealed class BasicOldPlayer:IPlayer
    {
        public string Name=>"old-interface";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>legal.First();
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>v.Hand.Take(count).ToArray();
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>legal[0];
    }
    [TestMethod]
    public void NewFlagsCannotSilentlyUseLegacyExecution()
    {
        var r=RuleConfigurationV11.Create();var doc=System.Text.Json.Nodes.JsonNode.Parse(r.Document)!;doc.AsObject().Remove("extensions");
        Assert.Throws<ArgumentException>(()=>RuleConfiguration.Parse(doc.ToJsonString()));
        var legacy=RuleConfiguration.Legacy();Assert.IsNotNull(legacy);
        Assert.IsTrue(RuleConfigurationV11.Create().IsV11);
    }
    private static string[] ProfilesV11()=>["random-v1.1","random-pass-v1.1","weak-v1.1","medium-v1.1","fast-v1.1","strong-v1.1","partner-v1.1","strong-plus-v1.1","strong-flow-cached-v1.1"];
    private static GameRecord Run(ConfiguredRules r,IPlayer[] players,int rounds=1,int seed=42)
    {
        var game=new Engine(r).Run(players,seed,rounds,3);ArenaCommand.Replay(JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(game,ArenaCommand.Json),ArenaCommand.Json)!);return game;
    }
    private sealed class Script(Contract? forced=null,List<Observation>? views=null,bool grand=false,bool solo=false,bool request=false,bool nominate=false):IPlayer,IJokerDeclarationPlayer,IRuleVariationPlayer
    {
        public string Name=>"script";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
        {
            if(forced?.Kind==ContractKind.OpenMisere)
            {
                if(v.Auction.Count==0)return legal.Contains(forced)?forced:new(8,Suit.Spades);
                if(v.Auction.Count==1&&v.Contract?.Kind==ContractKind.Normal)return forced;
                return null;
            }
            Contract? bid=grand?(v.Auction.Count==0?new(7,Suit.Spades):v.Auction.Count==1?new(0,Suit.Unknown,ContractKind.Misere):v.Auction.Count==3?new(0,Suit.Unknown,ContractKind.GrandNullo):null):v.Auction.Count==0?forced:null;
            return legal.Contains(bid)?bid:legal.First();
        }
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>v.Hand.OrderBy(c=>c.IsJoker?1:0).Take(count).ToArray();
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal){views?.Add(v);return legal[0];}
        public bool RequestRedeal(Observation v)=>request;
        public bool DeclareGrandSlam(Observation v)=>solo;
        public PlayingCard TransferCard(Observation v,IReadOnlyList<PlayingCard> legal)=>legal[0];
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>nominate?v.Hand.Where(c=>c.IsJoker).Select(c=>new PlayAction(c,Suit.Clubs)).ToArray():[];
    }
}
