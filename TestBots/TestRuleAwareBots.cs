#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
namespace TestBots;
[TestClass]
public class TestRuleAwareBots
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string p="")=>Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    private static string PathFor(string name)=>Path.Combine(Root(),"FiveHundredRules/presets/"+name+".json");
    private static readonly string[] Names=["random-legal-v1","weak-v1","medium-v1","visibility-fast-v8","strong-v1","partner-fast-v9"];
    [TestMethod]
    public void AllSixBotsRunAllSixConfigurationsAndReplay()
    {
        int games=0;
        foreach(var sub in new[]{"presets","custom","examples"})foreach(var file in Directory.GetFiles(Path.Combine(Root(),"FiveHundredRules",sub),"*.json"))foreach(var name in Names)
        {
            var r=RuleConfiguration.Load(file);
            var players=Enumerable.Range(0,4).Select(s=>Profiles.CreateForRules(name,51+s,r)).ToArray();
            var actual=new Engine(r).Run(players,18191,3,3);RoundTrip(actual);games++;
            // Force only the auction context in a separate hand, so even conservative Pass-only
            // policies must demonstrate discard/play support. Returned bot bids are still validated.
            var forced=new Engine(r).Run([new ForcedContract(Profiles.CreateForRules(name,61,r)),new PassPlayer(62),new PassPlayer(63),new PassPlayer(64)],19291,1,3);
            Assert.AreEqual(10,forced.Rounds.Single().TrickWinners.Count,name+" "+file);RoundTrip(forced);games++;
            Console.WriteLine(name+" / "+Path.GetFileName(file)+": passed");
        }
        Assert.AreEqual(72,games);
    }
    [TestMethod]
    public void FastAndStrongUseDistinctBudgetsAndCandidates()
    {
        var source=UxEvaluationV1.Build().Single(c=>c.Scenario.Id=="play-protect-human-winner").View;
        var r=RuleConfiguration.Load(PathFor("legacy-au-4p-v1"));var view=source with{Rules=r};var legal=r.LegalPlays(view.Hand,view.Trick,view.Contract!);
        var fast=(FiveHundredLab.Bots.FiveHundred.FiveHundredRuleBot)Profiles.CreateForRules("fast-rules-v1",21,r);
        var strong=(FiveHundredLab.Bots.FiveHundred.FiveHundredRuleBot)Profiles.CreateForRules("strong-rules-v1",21,r);
        fast.Play(view,legal);strong.Play(view,legal);
        Assert.AreEqual(8,fast.LastRollouts);Assert.AreEqual(16,strong.LastRollouts);
        var reduced=new RuleAwarePlayer(RuleBotLevel.Strong,21,new StrongBot(21),r,sampleCount:2);
        reduced.Play(view,legal);Assert.AreEqual(4,reduced.LastRollouts);Assert.AreEqual("strong-rules-v1-s2",reduced.Name);
        var customFast=new RuleAwarePlayer(RuleBotLevel.Fast,21,new VisibilityFastV8(21),r,sampleCount:2);
        Assert.AreEqual("fast-rules-v1-s2",customFast.Name);
    }
    [TestMethod]
    public void PartnerRetainsCriticalAndSignalExperienceChecksUnderConfiguredLegacyRules()
    {
        var r=RuleConfiguration.Load(PathFor("legacy-au-4p-v1"));
        foreach(var fixture in UxEvaluationV1.Build())
        {
            var v=fixture.View with{Rules=r};var bot=Profiles.CreateForRules("partner-rules-v1",119,r);
            UxActionV1 action=fixture.Scenario.Phase switch
            {
                "bid"=>new(Bid:bot.Bid(v,fixture.LegalBids)),
                "discard"=>new(Discards:bot.Discard(v,fixture.DiscardCount).ToArray()),
                _=>new(Play:bot.Play(v,fixture.LegalPlays))
            };
            foreach(var check in fixture.Scenario.Checks)Assert.IsTrue(check.Accept(fixture.View,action),fixture.Scenario.Id+" / "+check.Id);
        }
    }
    [TestMethod]
    public void ArenaAndLabCommandLinesUseOneConfigurationForEngineAndBots()
    {
        string root=Path.Combine(Path.GetTempPath(),"rule-bot-cli-"+Guid.NewGuid().ToString("N"));
        try
        {
            foreach(var name in new[]{"random-legal","weak","medium","fast","strong","partner"})
            {
                string output=Path.Combine(root,name);
                Assert.AreEqual(0,ArenaCommand.Run(["run","--rules",PathFor("canadian-platform-reference-4p-v1"),"--a",name,"--b","random-legal","--pairs","1","--max-rounds","1","--out",output]),name);
                foreach(var line in File.ReadLines(Path.Combine(output,"games.jsonl")))RoundTrip(JsonSerializer.Deserialize<GameRecord>(line,ArenaCommand.Json)!);
            }
            string lab=Path.Combine(root,"lab");
            Assert.AreEqual(0,LabCommand.Run(["bench-mixed","--rules",PathFor("us-single-round-4p-v1"),"--a","partner-rules-v1","--b","random-rules-v1","--pairs","1","--max-rounds","1","--out",lab]));
            foreach(var line in File.ReadLines(Path.Combine(lab,"games.jsonl")))RoundTrip(JsonSerializer.Deserialize<GameRecord>(line,ArenaCommand.Json)!);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [TestMethod]
    public void TimingWrappersForwardPublicJokerDeclarations()
    {
        var rules=RuleConfiguration.Load(PathFor("au-reference-4p-v1"));
        var players=Enumerable.Range(0,4).Select(_=>(IPlayer)new TimedPlayer(new MeteredPlayer(new NamingPlayer(),new Telemetry(),false),new DecisionTiming())).ToArray();
        var game=new Engine(rules).Run(players,8271,3,3);
        Assert.IsTrue(game.Rounds.SelectMany(r=>r.Decisions).Any(d=>d.Phase=="declare"));RoundTrip(game);
    }
    private sealed class NamingPlayer:IPlayer,IJokerDeclarationPlayer
    {
        public string Name=>"naming-fixture";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>v.Hand.Any(c=>c.IsJoker)?legal.First(c=>c is {Kind:ContractKind.Normal,Trump:Suit.Unknown}):null;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>v.Hand.Where(c=>!c.IsJoker).Take(count).ToArray();
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>legal.First();
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>v.Hand.Where(c=>c.IsJoker).Select(c=>new PlayAction(c,Suit.Clubs)).ToArray();
    }
    [TestMethod]
    public void ActiveMiserePartnerProtectsDeclarerWhileOpponentTriesToBust()
    {
        var r=Modified(n=>n["contracts"]!["misere"]!["activeSeats"]="all_four");
        var heart4=new PlayingCard(Suit.Hearts,4);var heart9=new PlayingCard(Suit.Hearts,9);
        var hand=new[]{heart4,heart9}.Concat(r.Deck().Where(c=>!c.IsJoker&&c.Suit!=Suit.Hearts).Take(8)).ToArray();
        PublicPlay[] trick=[new(0,new(new(Suit.Hearts,8))),new(1,new(new(Suit.Hearts,5)))];
        var v=View(r,hand) with{Seat=2,Declarer=0,Contract=new(0,Suit.Unknown,ContractKind.Misere),Trick=trick,History=trick};
        var friend=Profiles.CreateForRules("medium-rules-v1",1,r).Play(v,r.LegalPlays(hand,trick,v.Contract));Assert.AreEqual(heart9,friend.Card);
        var full=trick.Append(new PublicPlay(2,new(new(Suit.Hearts,6)))).ToArray();
        var opponent=v with{Seat=3,Trick=full,History=full};
        var foe=Profiles.CreateForRules("medium-rules-v1",1,r).Play(opponent,r.LegalPlays(hand,full,v.Contract));Assert.AreEqual(heart4,foe.Card);
    }
    private static void RoundTrip(GameRecord game)
    {ArenaCommand.Replay(JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(game,ArenaCommand.Json),ArenaCommand.Json)!);}
    private sealed class ForcedContract(IPlayer inner):IPlayer,IJokerDeclarationPlayer
    {
        public string Name=>inner.Name;
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal){var bid=inner.Bid(v,legal);Assert.IsTrue(legal.Contains(bid));return legal.First(c=>c?.Kind==ContractKind.Normal);}
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer p?p.DeclareJokers(v):[];
    }
    private sealed class PassPlayer(int seed):IPlayer
    {
        private readonly RandomLegalBot random=new(seed);
        public string Name=>"passing-context";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>null;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>random.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>random.Play(v,legal);
    }
    [TestMethod]
    public void DefaultRulesDelegateToEachFrozenStrategyWithoutDecisionChanges()
    {
        foreach(var name in Names)
        {
            var old=new Engine(new Rules()).Run(Enumerable.Range(0,4).Select(s=>Profiles.Create(name,31+s)).ToArray(),20291,1);
            var updated=new Engine(new Rules()).Run(Enumerable.Range(0,4).Select(s=>Profiles.CreateForRules(name,31+s,new Rules())).ToArray(),20291,1);
            Assert.AreEqual(JsonSerializer.Serialize(old.Rounds,ArenaCommand.Json),JsonSerializer.Serialize(updated.Rounds,ArenaCommand.Json),name);
        }
    }
    [TestMethod]
    public void RuleMismatchIsRejectedForEveryPolicy()
    {
        var au=RuleConfiguration.Load(PathFor("au-reference-4p-v1"));var us=RuleConfiguration.Load(PathFor("us-single-round-4p-v1"));
        foreach(var name in Names)
        {
            var bot=Profiles.CreateForRules(name,3,au);var v=View(us,us.Deck().Take(10).ToArray());
            Assert.Throws<ArgumentException>(()=>bot.Bid(v,[null]));
        }
    }
    private static Observation View(Rules r,PlayingCard[] hand)=>new(0,3,0,hand,Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,10,0,[],0,[],null)).ToArray(),[],null,null,[],[],[0,0],r,[]);
    private static Rules Modified(Action<JsonObject> edit)
    {
        var document=JsonNode.Parse(File.ReadAllText(PathFor("legacy-au-4p-v1")))!.AsObject();edit(document);string path=Path.GetTempFileName();
        try{File.WriteAllText(path,document.ToJsonString());return RuleConfiguration.Load(path);}finally{File.Delete(path);}
    }
    [TestMethod]
    public void LowJokerChangesMisereDiscardRatherThanOnlyValidatingJson()
    {
        var source=UxEvaluationV1.Build().Single(c=>c.Scenario.Id=="discard-misere-joker").View;
        foreach(bool low in new[]{false,true})
        {
            var r=Modified(n=>n["contracts"]!["misere"]!["joker"]!["unassignedRank"]=low?"lowest":"highest");
            var bot=Profiles.CreateForRules("medium-v1",3,r);var discard=bot.Discard(source with{Rules=r},3);
            Assert.AreEqual(!low,discard.Any(c=>c.IsJoker));
        }
    }
    [TestMethod]
    public void ScoringChangesBiddingForAllNonWeakStrategicProfiles()
    {
        var hand=UxFixturesV1.Cards("HJ JH JD AH KH QH TH 9H AC AD");
        var normal=RuleConfiguration.Load(PathFor("legacy-au-4p-v1"));
        var changed=Modified(n=>{foreach(var row in n["scoring"]!["normalTable"]!.AsObject())row.Value!["H"]=1;n["scoring"]!["slam"]!["mode"]="none";n["scoring"]!["slam"]!["value"]=0;});
        foreach(var name in Names.Skip(2))
        {
            var a=Profiles.CreateForRules(name,4,normal).Bid(View(normal,hand),normal.LegalBids(null,[]));
            var b=Profiles.CreateForRules(name,4,changed).Bid(View(changed,hand),changed.LegalBids(null,[]));
            Assert.AreEqual(Suit.Hearts,a!.Trump,name);Assert.AreNotEqual(Suit.Hearts,b!.Trump,name);
        }
    }
    [TestMethod]
    public void SixCardDeadPoolAndPublicJokerOwnerAreRespectedBySampling()
    {
        var r=(ConfiguredRules)RuleConfiguration.Load(PathFor("canadian-platform-reference-4p-v1"));
        var joker=r.Deck().First(c=>c.IsJoker);r.Declarations[joker]=Suit.Clubs;
        var v=View(r,r.Deck().Where(c=>!c.IsJoker).Take(10).ToArray()) with{Contract=new(7,Suit.Unknown),Declarer=1};
        foreach(var world in RuleSamples.Sample(v,r,20,new(93)))
        {Assert.AreEqual(6,world.Dead.Length);Assert.Contains(joker,world.Hands[1]);Assert.AreEqual(46,world.Hands.SelectMany(h=>h).Concat(world.Dead).Distinct().Count());}
    }
}
