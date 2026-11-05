#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace TestBots;
[TestClass]
public class TestAdaptLearningV2
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string p="")=>Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    [TestMethod]
    public void V2LearnsMoreFromSameEvidenceAndCanReverseWithoutErasingModels()
    {
        var a=new OpponentBeliefs();var b=new OpponentBeliefsV2();a.Update(1,[.95,.04,.01]);b.Update(1,[.95,.04,.01]);Assert.IsTrue(b.BidWeights(1)[0]>a.BidWeights(1)[0]);
        for(int i=0;i<20;i++)b.Update(1,[.95,.04,.01]);for(int i=0;i<30;i++)b.Update(1,[.01,.04,.95]);
        Assert.IsTrue(b.BidWeights(1)[2]>.7);Assert.IsTrue(b.BidWeights(1).All(x=>x>0));
        var neutral=new OpponentBeliefsV2();for(int i=0;i<100;i++)neutral.Update(2,[.5,.5,.5]);var p=neutral.BidWeights(2);double[] prior=[.2,.6,.2];for(int i=0;i<3;i++)Assert.AreEqual(prior[i],p[i],1e-12);
    }
    [TestMethod]
    public void V2SupportsAllSixConfigurationsAndReplays()
    {
        foreach(var sub in new[]{"presets","custom","examples"})foreach(var path in Directory.GetFiles(Path.Combine(Root(),"FiveHundredRules",sub),"*.json"))
        {
            var r=RuleConfiguration.Load(path);var bot=new LearningStrongV2(31,OpponentAdaptationStage.Play,new(){Samples=2,DiscardCandidates=3},r);
            var game=new Engine(r).Run([bot,OpponentStrong.CreateStyleOpponent(0),OpponentStrong.CreateStyleOpponent(1),OpponentStrong.CreateStyleOpponent(2)],31457,1,3);
            ArenaCommand.Replay(JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(game,ArenaCommand.Json),ArenaCommand.Json)!);
            Assert.AreEqual("Adapt-v2",Profiles.CreateForRules("adapt-v2",31,r).Name);
        }
    }
    [TestMethod]
    public void V2HeldOutPublicPredictionReportsAllStylesWithoutUsingActorHands()
    {
        var r=new Rules();
        foreach(int style in new[]{0,1,2})foreach(int seed in new[]{419,733})
        {
            var old=new OpponentStrong(seed,OpponentAdaptationStage.Play);var next=new LearningStrongV2(seed,OpponentAdaptationStage.Play);var random=new SeededRandom(seed);double loss1=0,loss2=0;
            for(int round=0;round<120;round++)
            {
                var deck=r.Deck();random.Shuffle(deck);var hand=deck.Take(10).ToArray();var actor=deck.Skip(10).Take(10).ToArray();var prefix=new[]{new AuctionAction(0,null)};
                var v=new Observation(0,3,round,hand,Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,10,0,[],0,[],null)).ToArray(),prefix,null,null,[],[],[0,0],r,[]);
                old.ObserveAuction(v);next.ObserveAuction(v);var view=v with{Seat=1,Hand=actor};var action=OpponentStrong.CreateStyleOpponent(style).Bid(view,OpponentRules.Bids(view));
                loss1-=Math.Log(old.PublicBidProbability(v,1,action));loss2-=Math.Log(next.PublicBidProbability(v,1,action));
                var observed=v with{Auction=prefix.Append(new AuctionAction(1,action)).ToArray()};old.ObserveAuction(observed);next.ObserveAuction(observed);
            }
            Assert.IsTrue(double.IsFinite(loss1)&&double.IsFinite(loss2));Console.WriteLine($"style={style} seed={seed} NLL Adapt={loss1/120:F6} Adapt-v2={loss2/120:F6}");
        }
    }
}
