#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
namespace TestBots;

[TestClass]
public class TestFiveHundredLab
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source="")=>Path.GetDirectoryName(Path.GetDirectoryName(source))!;
    private static Observation View(PlayingCard[]? hand=null,Contract? contract=null)
    {
        hand??=new Rules().Deck().Take(10).ToArray();
        return new(0,3,0,hand,Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,s==0?hand.Length:10,0,[],0,[],null)).ToArray(),[],contract??new(6,Suit.Hearts),0,[],[],[0,0],new(),[]);
    }
    [TestMethod]
    public void PreviousBotsAndAllTheirConfigurationSourcesAreFrozen()
    {
        var root=Repo();var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"FiveHundredLab/frozen-baselines-v1.json")))!;
        foreach(var e in manifest)Assert.AreEqual(e.Value,FrozenSourceHashV1.Compute(Path.Combine(root,e.Key)),e.Key);
    }
    [TestMethod]
    [DataRow(ContractKind.Normal,Suit.Hearts)] [DataRow(ContractKind.Normal,Suit.Unknown)]
    [DataRow(ContractKind.Misere,Suit.Unknown)] [DataRow(ContractKind.OpenMisere,Suit.Unknown)]
    public void CachedMediumMatchesOriginalForEveryDecisionIncludingJokerAndOpenHands(ContractKind kind,Suit trump)
    {
        var c=new Contract(kind==ContractKind.Normal?6:0,trump,kind);
        for(int seed=0;seed<4;seed++)new Engine(new Rules{MisereRequiresSeven=false,OpenMisereRequiresEight=false}).Run(Enumerable.Range(0,4).Select(s=>(IPlayer)new ComparingPlayer(s,c,seed*7+s)).ToArray(),seed,1);
    }
    private sealed class ComparingPlayer(int seat,Contract forced,int seed):IPlayer
    {
        private readonly MediumBotPlayer old=new();private readonly CachedMediumV1 cached=new();private readonly VisibilityMediumV5 visibility=new();private readonly RandomLegalBot random=new(seed);
        public string Name=>"compare";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal){Assert.AreEqual(old.Bid(v,legal),cached.Bid(v,legal));Assert.AreEqual(old.Bid(v,legal),visibility.Bid(v,legal));return seat==0&&legal.Contains(forced)?forced:null;}
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count){Assert.IsTrue(old.Discard(v,count).ToHashSet().SetEquals(cached.Discard(v,count)));Assert.IsTrue(old.Discard(v,count).ToHashSet().SetEquals(visibility.Discard(v,count)));return random.Discard(v,count);}
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal){Assert.AreEqual(old.Play(v,legal),cached.Play(v,legal));Assert.AreEqual(old.Play(v,legal),visibility.Play(v,legal));return random.Play(v,legal);}
    }
    [TestMethod]
    public void CachedStrongPreservesFrozenStrongDecisions()
    {
        IPlayer[] Old()=>[new StrongBot(71,new(){Samples=2,DiscardCandidates=3}),new MediumBotPlayer(),new StrongBot(73,new(){Samples=2,DiscardCandidates=3}),new MediumBotPlayer()];
        IPlayer[] New()=>[new CachedStrongV1(71,new(){Samples=2,DiscardCandidates=3}),new MediumBotPlayer(),new CachedStrongV1(73,new(){Samples=2,DiscardCandidates=3}),new MediumBotPlayer()];
        var a=new Engine(new()).Run(Old(),716,3);var b=new Engine(new()).Run(New(),716,3);b.Players=a.Players;
        Assert.AreEqual(JsonSerializer.Serialize(a,LabJson.Options),JsonSerializer.Serialize(b,LabJson.Options));
    }
    [TestMethod]
    public void CardTablesMatchRulesAndRejectMaskedCards()
    {
        var r=new Rules();var random=new SeededRandom(531);
        foreach(var trump in Rules.Suits.Append(Suit.Unknown))
        {
            var c=new Contract(6,trump);var table=CardTableV1.Get(r,c);
            foreach(var card in r.Deck()){Assert.AreEqual((int)r.EffectiveSuit(card,c),table.Suit(card));Assert.AreEqual(r.Rank(card,c),table.Strength(card));}
            for(int i=0;i<100;i++){var deck=r.Deck();random.Shuffle(deck);var trick=deck.Take(4).Select((card,s)=>new PublicPlay(s,new(card,card.IsJoker&&s==0&&trump==Suit.Unknown?Suit.Clubs:Suit.Unknown))).ToArray();Assert.AreEqual(r.Winner(trick,c),table.Winner(trick,c));}
            Assert.ThrowsExactly<InvalidDataException>(()=>table.Bit(new(Suit.Unknown,0)));
        }
    }
    [TestMethod]
    public void PublicTrackerUpdatesAndResetsAcrossDivergingWorlds()
    {
        var tracker=new PublicTrackerV1();var v=View(contract:new(6,Suit.Unknown));
        PublicPlay[] plays=[new(0,new(new(Suit.Hearts,4))),new(1,new(new(Suit.Clubs,5))),new(2,new(new(Suit.Hearts,5))),new(3,new(new(Suit.Spades,5)))];
        for(int i=0;i<=plays.Length;i++)
        {
            var state=v with{History=plays.Take(i).ToArray()};var expected=BeliefSampler.Voids(state);var actual=tracker.Update(state);
            for(int s=0;s<4;s++)Assert.AreEqual(expected[s].Aggregate(0,(mask,suit)=>mask|(1<<(int)suit)),actual[s]);
        }
        var branch=plays.ToArray();branch[0]=new(0,new(new(Suit.Diamonds,4)));
        Assert.IsTrue((tracker.Update(v with{History=branch})[1]&(1<<(int)Suit.Diamonds))!=0);
        Assert.IsTrue((tracker.Update(v with{History=branch})[1]&(1<<(int)Suit.Hearts))==0);
    }
    [TestMethod]
    public void RepresentativesKeepNamedJokerActionsAndUnseenRanks()
    {
        var r=new Rules();var v=View([new(Suit.Joker,16),new(Suit.Spades,5)],new(6,Suit.Unknown));var legal=r.LegalPlays(v.Hand,[],v.Contract!);
        Assert.AreEqual(5,new CardFrameV1(v).Representatives(legal,legal[0]).Count);
        v=View([new(Suit.Spades,5),new(Suit.Spades,7)],new(6,Suit.Unknown));legal=r.LegalPlays(v.Hand,[],v.Contract!);
        Assert.AreEqual(2,new CardFrameV1(v).Representatives(legal,legal[0]).Count);
        v=v with{History=[new(1,new(new(Suit.Spades,6)))]};Assert.AreEqual(1,new CardFrameV1(v).Representatives(legal,legal[1]).Count);
    }
    [TestMethod]
    public void TacticianProtectsNoTrumpStopperStructure()
    {
        PlayingCard[] hand=[new(Suit.Spades,13),new(Suit.Spades,5),new(Suit.Clubs,14),new(Suit.Clubs,4),new(Suit.Clubs,5),new(Suit.Clubs,6),new(Suit.Diamonds,13),new(Suit.Diamonds,4),new(Suit.Hearts,14),new(Suit.Hearts,4)];
        var singleton=hand.Where(c=>c!=new PlayingCard(Suit.Spades,5)).Append(new(Suit.Diamonds,6)).ToArray();
        Assert.IsTrue(TacticianV1.Retained(hand,new(6,Suit.Unknown),new())>TacticianV1.Retained(singleton,new(6,Suit.Unknown),new()));
    }
    [TestMethod]
    public void CycleBeliefsPreserveAllPrivateAndPublicConstraints()
    {
        var r=new Rules();var own=r.Deck().Where(c=>c.Suit==Suit.Hearts).Take(9).ToArray();
        var players=Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,9,0,[],0,[],null)).ToArray();
        PublicPlay[] history=[new(0,new(new(Suit.Spades,5))),new(1,new(new(Suit.Clubs,5))),new(2,new(new(Suit.Spades,6))),new(3,new(new(Suit.Spades,7)))];
        var v=View(own,new(6,Suit.Unknown)) with{Players=players,History=history};
        var samples=BeliefCyclesV2.Sample(v,50,new(531));
        foreach(var s in samples)
        {
            CollectionAssert.AreEquivalent(own,s.Hands[0]);Assert.IsFalse(s.Hands[1].Any(c=>c.Suit==Suit.Spades));
            var cards=s.Hands.SelectMany(x=>x).Concat(s.Dead).Concat(history.Select(p=>p.Action.Card)).ToArray();Assert.AreEqual(43,cards.Length);Assert.AreEqual(43,cards.Distinct().Count());
        }
        Assert.AreEqual(JsonSerializer.Serialize(samples,LabJson.Options),JsonSerializer.Serialize(BeliefCyclesV2.Sample(v,50,new(531)),LabJson.Options));
    }
    [TestMethod]
    public void LearnedModelsValidateIdentityAndSeedGroupIsolation()
    {
        foreach(var filename in new[]{"ridge-teacher16-v1-g32.json","ridge-phase-v2-g48.json"})
        {
            string path=Path.Combine(Repo(),"FiveHundredLab/Archive/Models",filename);var model=ValueModelV1.Load(path);
            Assert.AreEqual(0,model.TrainingSeeds.Intersect(model.ValidationSeeds).Count());Assert.AreNotEqual("unsaved",model.Digest);
            var prediction=model.Predict(View(),"play");Assert.IsTrue(double.IsFinite(prediction.Utility));Assert.IsTrue(prediction.MadeProbability is >=0 and <=1);
            Assert.ThrowsExactly<NotSupportedException>(()=>model.Predict(View() with{Rules=new(){WinScore=1000}},"play"));
            foreach(var child in model.PhaseModels.Values)Assert.AreEqual(0,child.TrainingSeeds.Except(model.TrainingSeeds).Count());
        }
    }
    [TestMethod]
    public void RidgeFitsTrainingStatisticsWithoutValidationLeak()
    {
        var initial=FeaturesV1.Extract(View(),"play");var rows=Enumerable.Range(0,15).Select(i=>
        {var f=initial.ToArray();f[0]=i%5==0?10000:i;return new TrainingRowV1(i,"play",f,i*10,i/20.0,5);}).ToArray();
        var model=ValueModelV1.Fit("unit-ridge-v1",rows);Assert.IsTrue(model.Mean[0]<20);
        Assert.IsTrue(model.Metrics["validationUtilityRMSE"]>=0);model.Weights[0][0]=double.NaN;Assert.ThrowsExactly<InvalidDataException>(()=>model.Validate());
    }
    private sealed class ForwardObserver(int seat,Contract c,List<Observation> observations):IPlayer
    {
        private readonly TacticianV1 inner=new();
        public string Name=>"tactical-observer";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>seat==0&&legal.Contains(c)?c:null;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal){observations.Add(v);return inner.Play(v,legal);}
    }
    [TestMethod]
    [DataRow(ContractKind.Normal,Suit.Hearts)] [DataRow(ContractKind.Normal,Suit.Unknown)]
    [DataRow(ContractKind.Misere,Suit.Unknown)] [DataRow(ContractKind.OpenMisere,Suit.Unknown)]
    public void ShortForwardFullDepthMatchesActualRulesAndPrivatePolicies(ContractKind kind,Suit trump)
    {
        var c=new Contract(kind==ContractKind.Normal?6:0,trump,kind);var obs=new List<Observation>();
        var game=new Engine(new(){MisereRequiresSeven=false,OpenMisereRequiresEight=false}).Run(Enumerable.Range(0,4).Select(s=>(IPlayer)new ForwardObserver(s,c,obs)).ToArray(),1561,1);var round=game.Rounds.Single();
        foreach(var v in obs.Where((_,i)=>i%4==0))
        {
            var hands=round.Hands.Select(h=>h.ToList()).ToArray();hands[0].AddRange(round.Kitty);var discard=round.Decisions.Single(d=>d.Phase=="discard").Discards!;foreach(var card in discard)hands[0].Remove(card);foreach(var p in v.History)hands[p.Seat].Remove(p.Action.Card);
            var sample=new SampledDeal(hands.Select(h=>h.ToArray()).ToArray(),v.Seat==0?[]:discard,1);
            var choice=new TacticianV1().Play(v,v.Rules.LegalPlays(v.Hand,v.Trick,c));var result=new ShortForwardV1(v,sample,99,null,1,OpponentPolicyV1.Tactical).Play(choice);
            Assert.IsTrue(result.Complete);Assert.AreEqual(LeafValueV1.Score(v,round.Tricks).Utility,result.Utility);
        }
    }
    [TestMethod]
    [DataRow("adaptive-sprint-v1")] [DataRow("adaptive-balanced-v1")] [DataRow("selective-play-v1")] [DataRow("selective-joint-v1")]
    [DataRow("selective-quality-v2")] [DataRow("selective-belief-v3")] [DataRow("value-guided-v3")] [DataRow("selective-fast-v4")] [DataRow("visibility-strong-v5")] [DataRow("visibility-fast-v6")] [DataRow("visibility-quality-v7")]
    public void NewNamedStrategiesMakeLegalReproducibleDecisions(string name)
    {
        var v=View();var r=v.Rules;var legal=r.LegalPlays(v.Hand,[],v.Contract!);var model=Path.Combine(Repo(),"FiveHundredLab/Archive/Models/ridge-phase-v2-g48.json");
        var a=Profiles.Create(name,915,model);var b=Profiles.Create(name,915,model);
        Assert.AreEqual(a.Play(v,legal),b.Play(v,legal));Assert.Contains(a.Play(v,legal),legal);
        if(a is AdaptiveV1 adaptive && adaptive.LastSearch is {Direct:false} report)Assert.IsTrue(report.SamplesUsed<=12&&report.Rollouts<=report.Candidates*report.SamplesUsed);
    }
    [TestMethod]
    public void UnknownProfilesBudgetsAndOptionsCannotSilentlyChangeAnExperiment()
    {
        Assert.ThrowsExactly<ArgumentException>(()=>Profiles.Create("typo",1));
        Assert.ThrowsExactly<ArgumentException>(()=>new AdaptiveV1(1,AdaptiveProfileV1.Sprint with{MaxSamples=100}));
        Assert.AreEqual(1,LabCommand.Run(["bench","--samplse","4"]));
    }
    [TestMethod]
    public void NewStrategyAndModelVersionsAreAlsoFrozen()
    {
        var root=Repo();var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"FiveHundredLab/frozen-profiles-v1.json")))!;
        foreach(var e in manifest)Assert.AreEqual(e.Value,FrozenSourceHashV1.Compute(Path.Combine(root,e.Key)),e.Key);
    }
    [TestMethod]
    public void CorpusProfilerUsesIdenticalRecordedStatesAndRespectsBudget()
    {
        string dir=Path.Combine(Path.GetTempPath(),"corpus-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var game=new Engine(new()).Run(Enumerable.Range(0,4).Select(_=>(IPlayer)new MediumBotPlayer()).ToArray(),716,1);
            string input=Path.Combine(dir,"games.jsonl"),output=Path.Combine(dir,"profile.json");File.WriteAllText(input,JsonSerializer.Serialize(game,LabJson.Options)+"\n");
            CorpusProfilerV1.Run(input,output,["medium-v1","weak-v1"],1,2);
            using var doc=JsonDocument.Parse(File.ReadAllText(output));foreach(var profile in doc.RootElement.GetProperty("Profiles").EnumerateObject())foreach(var phase in profile.Value.EnumerateObject())Assert.IsTrue(phase.Value.GetProperty("Count").GetInt32()<=2);
            Assert.ThrowsExactly<IOException>(()=>CorpusProfilerV1.Run(input,output,["medium-v1"],1,2));
        }
        finally{Directory.Delete(dir,true);}
    }

}
