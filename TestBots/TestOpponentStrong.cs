#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using FiveHundredArena;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
namespace TestBots;
[TestClass]
public class TestOpponentStrong
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string path="")=>Path.GetDirectoryName(Path.GetDirectoryName(path))!;
    private static Rules Rules(string name="legacy-au-4p-v1")=>RuleConfiguration.Load(Path.Combine(Root(),"FiveHundredRules/presets",name+".json"));
    private static StrongBotOptions Budget()=>new(){Samples=2,DiscardCandidates=3};
    [TestMethod]
    public void Stage1EvidenceLearnsSeatsSeparatelyAndCanReverse()
    {
        var b=new OpponentBeliefs();for(int i=0;i<40;i++)b.Update(1,[.95,.04,.01]);
        Assert.IsTrue(b.BidWeights(1)[0]>.65);CollectionAssert.AreEqual(new[]{.2,.6,.2},b.BidWeights(2));
        for(int i=0;i<100;i++)b.Update(1,[.01,.04,.95]);Assert.IsTrue(b.BidWeights(1)[2]>.65);Assert.IsTrue(b.BidWeights(1).All(x=>x>.03));
        var copy=b.BidWeights(1);copy[0]=99;Assert.IsTrue(b.BidWeights(1)[0]<1);
    }
    [TestMethod]
    public void Stage1PosteriorImprovesPredictiveLogLossOnControlledEvidence()
    {
        var b=new OpponentBeliefs();double[] likelihood=[.94,.03,.03];double prior=-Math.Log(b.BidWeights(1).Select((w,i)=>w*likelihood[i]).Sum());
        for(int i=0;i<25;i++)b.Update(1,likelihood);
        double posterior=-Math.Log(b.BidWeights(1).Select((w,i)=>w*likelihood[i]).Sum());Assert.IsTrue(posterior<prior);
        Console.WriteLine($"Controlled likelihood test NLL: {prior:F3} -> {posterior:F3}; not an actual-player calibration claim");
    }
    [TestMethod]
    public void Stage1ConfiguredBiddingReallyUsesSearchAndEntireAuction()
    {
        var r=Rules("us-single-round-4p-v1");var bot=new OpponentStrong(7,options:Budget(),rules:r);
        var observer=new ForceSearch(bot);var game=new Engine(r).Run([observer,new OpponentStyle(0),new OpponentStyle(1),new OpponentStyle(2)],9137,1,3);
        Assert.IsTrue(observer.BidCandidates>1);Assert.AreEqual(2,observer.Samples);Replay(game);
    }
    [TestMethod]
    public void Stage1ActualPublicBidStreamUpdatesWithoutSeeingActorHand()
    {
        var r=Rules();var bot=new OpponentStrong(119,rules:r,options:Budget());var random=new SeededRandom(321);double learnedLoss=0,priorLoss=0;
        for(int round=0;round<80;round++)
        {
            var deck=r.Deck();random.Shuffle(deck);var own=deck.Take(10).ToArray();var actor=deck.Skip(10).Take(10).ToArray();
            var prefix=new[]{new AuctionAction(0,null)};var view=V(r,own) with{Round=round,Auction=prefix};bot.ObserveAuction(view);
            var actorView=view with{Seat=1,Hand=actor};var legal=OpponentRules.Bids(actorView);var action=new OpponentStyle(0).Bid(actorView,legal);
            learnedLoss-=Math.Log(bot.PublicBidProbability(view,1,action));priorLoss-=Math.Log(bot.PublicBidProbability(view,1,action,[.2,.6,.2]));
            var observed=view with{Auction=prefix.Append(new AuctionAction(1,action)).ToArray()};bot.ObserveAuction(observed);int updates=bot.Beliefs.BidUpdates;bot.ObserveAuction(observed);Assert.AreEqual(updates,bot.Beliefs.BidUpdates);
        }
        Assert.AreEqual(80,bot.Beliefs.BidUpdates);Assert.IsTrue(double.IsFinite(learnedLoss));
        Console.WriteLine($"Public conservative stream prequential NLL: prior={priorLoss/80:F3}, adaptive={learnedLoss/80:F3}; actor hand only used by synthetic actor");
    }
    private static Observation V(Rules rules,PlayingCard[] hand)=>new(0,3,0,hand,Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,10,0,[],0,[],null)).ToArray(),[],null,null,[],[],[0,0],rules,[]);
    [TestMethod]
    public void Stage2ReweightsWithRoundPriorAndDoesNotMultiplyOldAuctionWeights()
    {
        var r=Rules();var root=V(r,r.Deck().Take(10).ToArray()) with{Auction=new[]{new AuctionAction(0,null)}};
        var bot=new OpponentStrong(71,OpponentAdaptationStage.Belief,Budget(),r);bot.ObserveAuction(root);
        var worlds=RuleSamples.Sample(root,(ConfiguredRules)r,32,new(71));
        var chosen=worlds.First(w=>{var a=root with{Seat=1,Hand=w.Hands[1]};var legal=OpponentRules.Bids(a);var p=OpponentStyle.Predictions(a,legal,new RuleAwarePlayer(RuleBotLevel.Medium,1,new MediumBotPlayer(),r));return p[0]!=p[1];});
        var actor=root with{Seat=1,Hand=chosen.Hands[1]};var legal=OpponentRules.Bids(actor);var predictions=OpponentStyle.Predictions(actor,legal,new RuleAwarePlayer(RuleBotLevel.Medium,1,new MediumBotPlayer(),r));
        var action=predictions[0];var observed=root with{Auction=root.Auction.Append(new AuctionAction(1,action)).ToArray()};bot.ObserveAuction(observed);
        double[] prior=[.2,.6,.2];var expected=prior.Select((w,i)=>w*OpponentStyle.Likelihood(action,predictions[i],legal.Count)).ToArray();
        var actual=bot.WorldBidWeights(observed,chosen,1);for(int i=0;i<3;i++)Assert.AreEqual(expected[i],actual[i],1e-12);
        var weighted=bot.Reweight(observed,worlds);var rescaled=bot.Reweight(observed,worlds.Select(w=>w with{Weight=1000}).ToArray());
        for(int i=0;i<worlds.Count;i++)Assert.AreEqual(weighted[i].Weight,rescaled[i].Weight,1e-12);
        Assert.IsTrue(bot.LastEffectiveSamples>=2);Assert.IsTrue(weighted.Select(w=>w.Weight).Distinct().Count()>1);
    }
    [TestMethod]
    public void Stage2DoesNotTreatPostKittyDeclarerHandAsOriginalBidHand()
    {
        var r=Rules();var source=FiveHundredLab.Usability.UxEvaluationV1.Build().Single(c=>c.Scenario.Id=="play-protect-human-winner");
        var v=source.View with{Rules=r};var bot=new OpponentStrong(17,OpponentAdaptationStage.Belief,Budget(),r);
        bot.Play(v,[source.LegalPlays[0]]);
        var weights=bot.WorldBidWeights(v,new SampledDeal([[],[],[],[]],[],1),v.Declarer!.Value);
        CollectionAssert.AreEqual(new[]{.2,.6,.2},weights);
    }
    [TestMethod]
    public void Stage3OnlyLearnsGenuineChoiceAndRevisitsDelayedEvidenceOnce()
    {
        var r=Rules();var bot=new OpponentStrong(7,OpponentAdaptationStage.Play,Budget(),r);
        PublicPlay[] first=[new(3,new(new(Suit.Hearts,12))),new(0,new(new(Suit.Hearts,5))),new(1,new(new(Suit.Hearts,13))),new(2,new(new(Suit.Hearts,6)))];
        var v=V(r,r.Deck().Where(c=>c.Suit!=Suit.Hearts).Take(9).ToArray()) with{Contract=new(6,Suit.Unknown),Declarer=3,History=first};
        bot.ObservePublic(v);Assert.AreEqual(0,bot.Beliefs.PlayUpdates);
        var later=v with{History=first.Append(new PublicPlay(1,new(new(Suit.Hearts,4)))).ToArray()};bot.ObservePublic(later);Assert.AreEqual(1,bot.Beliefs.PlayUpdates);
        bot.ObservePublic(later);Assert.AreEqual(1,bot.Beliefs.PlayUpdates);Assert.IsTrue(bot.Beliefs.PlayWeights(1)[2]>.2);
        var conservative=new OpponentStrong(7,OpponentAdaptationStage.Bidding,Budget(),r);conservative.ObservePublic(later);Assert.AreEqual(0,conservative.Beliefs.PlayUpdates);
    }
    [TestMethod]
    public void Stage3ForcedOvertrumpIsNotEvidenceOfAggression()
    {
        var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Root(),"FiveHundredRules/presets/legacy-au-4p-v1.json")))!;node["play"]!["mustOvertrump"]=true;
        string path=Path.GetTempFileName();Rules r;try{File.WriteAllText(path,node.ToJsonString());r=RuleConfiguration.Load(path);}finally{File.Delete(path);}
        var bot=new OpponentStrong(7,OpponentAdaptationStage.Play,Budget(),r);
        PublicPlay[] plays=[new(3,new(new(Suit.Hearts,12))),new(0,new(new(Suit.Hearts,5))),new(1,new(new(Suit.Hearts,13))),new(2,new(new(Suit.Hearts,6))),new(1,new(new(Suit.Hearts,4)))];
        var v=V(r,r.Deck().Where(c=>c.Suit!=Suit.Hearts).Take(9).ToArray()) with{Contract=new(6,Suit.Hearts),Declarer=3,History=plays};bot.ObservePublic(v);Assert.AreEqual(0,bot.Beliefs.PlayUpdates);
    }
    [TestMethod]
    public void AllStagesSearchLegallyAcrossAllSixConfigurations()
    {
        int games=0;foreach(var dir in new[]{"presets","custom","examples"})foreach(var path in Directory.GetFiles(Path.Combine(Root(),"FiveHundredRules",dir),"*.json"))foreach(var stage in Enum.GetValues<OpponentAdaptationStage>())
        {
            var r=RuleConfiguration.Load(path);var bot=new OpponentStrong(61,stage,Budget(),r);var monitor=new ForceSearch(bot);
            var game=new Engine(r).Run([monitor,new OpponentStyle(0),new OpponentStyle(1),new OpponentStyle(2)],9317,1,3);Replay(game);
            Assert.IsTrue(monitor.BidCandidates>1);Assert.AreEqual(2,monitor.Samples);games++;
        }
        Assert.AreEqual(18,games);
    }
    [TestMethod]
    public void WrongRulesAreRejectedAndFreshSessionsReproduceDecisions()
    {
        var r=Rules();var us=Rules("us-single-round-4p-v1");var v=V(us,us.Deck().Take(10).ToArray());
        Assert.Throws<ArgumentException>(()=>new OpponentStrong(1,rules:r).Bid(v,OpponentRules.Bids(v)));
        foreach(var stage in Enum.GetValues<OpponentAdaptationStage>())
        {
            IPlayer[] Make()=>[new OpponentStrong(7,stage,Budget(),r),new OpponentStyle(0),new OpponentStyle(1),new OpponentStyle(2)];
            var a=new Engine(r).Run(Make(),7117,1,3);var b=new Engine(r).Run(Make(),7117,1,3);
            Assert.AreEqual(JsonSerializer.Serialize(a,ArenaCommand.Json),JsonSerializer.Serialize(b,ArenaCommand.Json));
        }
    }
    [TestMethod]
    public void UninformativeEvidenceDoesNotInventAPlayerStyle()
    {
        var b=new OpponentBeliefs();for(int i=0;i<100;i++)b.Update(1,[.5,.5,.5]);
        var actual=b.BidWeights(1);double[] expected=[.2,.6,.2];for(int i=0;i<3;i++)Assert.AreEqual(expected[i],actual[i],1e-12);
    }
    [TestMethod]
    public void FixedMediumAblationReproducesOriginalSearchStrong()
    {
        IPlayer[] Original()=>[new StrongBot(19,Budget()),new MediumBotPlayer(),new MediumBotPlayer(),new MediumBotPlayer()];
        IPlayer[] Copied()=>[new OpponentStrong(19,options:Budget(),learn:false,fixedMedium:true),new MediumBotPlayer(),new MediumBotPlayer(),new MediumBotPlayer()];
        var a=new Engine().Run(Original(),7211,1,3);var b=new Engine().Run(Copied(),7211,1,3);
        Assert.AreEqual(JsonSerializer.Serialize(a.Rounds,ArenaCommand.Json),JsonSerializer.Serialize(b.Rounds,ArenaCommand.Json));
    }
    private sealed class ForceSearch(OpponentStrong inner):IPlayer,IJokerDeclarationPlayer
    {
        public int BidCandidates,Samples;public string Name=>inner.Name;
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal){var b=inner.Bid(v,legal);BidCandidates=inner.LastSearch!.Candidates;Samples=inner.LastSearch.Samples;return b;}
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner.DeclareJokers(v);
    }
    private static void Replay(GameRecord g)=>ArenaCommand.Replay(JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(g,ArenaCommand.Json),ArenaCommand.Json)!);
}
