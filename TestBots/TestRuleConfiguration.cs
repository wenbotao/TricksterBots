#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiveHundredArena;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
namespace TestBots;
[TestClass]
public class TestRuleConfiguration
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string p="")=>Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    private static string FilePath(string id)=>Path.Combine(Root(),"FiveHundredRules/presets/"+id+".json");
    [TestMethod]
    public void AllSixConfigurationsRunAndReplay()
    {
        var directory=Path.Combine(Root(),"FiveHundredRules");
        foreach(var sub in new[]{"presets","custom","examples"})foreach(var file in Directory.GetFiles(Path.Combine(directory,sub),"*.json"))
        {
            var r=RuleConfiguration.Load(file);
            IPlayer[] Players()=>Enumerable.Range(0,4).Select(s=>(IPlayer)new RandomLegalBot(99+s)).ToArray();
            var game=new Engine(r).Run(Players(),2311,8);
            Assert.IsTrue(game.Rounds.Any(x=>!x.Redeal),file);
            var restored=JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(game,ArenaCommand.Json),ArenaCommand.Json)!;
            ArenaCommand.Replay(restored);
            foreach(var round in game.Rounds.Where(x=>!x.Redeal))Assert.AreEqual(10,round.TrickWinners.Count);
        }
    }
    [TestMethod]
    public void DefaultAndCompatibilityPresetHaveIdenticalDecisionsAndScores()
    {
        IPlayer[] Players()=>Enumerable.Range(0,4).Select(s=>(IPlayer)new RandomLegalBot(10+s)).ToArray();
        var old=new Engine(new Rules()).Run(Players(),5,5,3);var configured=Engine.FromConfiguration(FilePath("legacy-au-4p-v1")).Run(Players(),5,5,3);
        Assert.AreEqual(JsonSerializer.Serialize(old.Rounds,ArenaCommand.Json),JsonSerializer.Serialize(configured.Rounds,ArenaCommand.Json));
        Assert.IsInstanceOfType<Rules>(RuleConfiguration.Load());
    }
    [TestMethod]
    public void RankAndScoreAreIndependentAndPresetSpecific()
    {
        var old=RuleConfiguration.Load(FilePath("legacy-au-4p-v1"));var au=RuleConfiguration.Load(FilePath("au-reference-4p-v1"));
        var open=new Contract(0,Suit.Unknown,ContractKind.OpenMisere);var hearts=new Contract(10,Suit.Hearts);
        Assert.IsTrue(old.Order(open)>old.Order(hearts));Assert.IsTrue(au.Order(open)<au.Order(hearts));
        Assert.AreEqual(500,au.Value(open));
        var ca=RuleConfiguration.Load(FilePath("canadian-platform-reference-4p-v1"));
        CollectionAssert.AreEqual(new[]{0,260},ca.Score(new(8,Suit.Clubs),0,new[]{7,3,0,0}).Delta);
        CollectionAssert.AreEqual(new[]{-260,30},au.Score(new(8,Suit.Clubs),0,new[]{7,3,0,0}).Delta);
        Assert.AreEqual(46,ca.Deck().Count);Assert.AreEqual(2,ca.Deck().Count(c=>c.IsJoker));
    }
    [TestMethod]
    public void JokerPoliciesDifferAndNamedJokerIsNotUniversalTrump()
    {
        var old=(ConfiguredRules)RuleConfiguration.Load(FilePath("legacy-au-4p-v1"));
        var au=(ConfiguredRules)RuleConfiguration.Load(FilePath("au-reference-4p-v1"));
        var c=new Contract(0,Suit.Unknown,ContractKind.Misere);
        PlayingCard joker=new(Suit.Joker,16),club=new(Suit.Clubs,5);
        PublicPlay[] trick=[new(1,new(new(Suit.Hearts,9)))];
        Assert.AreEqual(2,old.LegalPlays([joker,club],trick,c).Count);
        Assert.AreEqual(joker,au.LegalPlays([joker,club],trick,c).Single().Card);
        au.Declarations[joker]=Suit.Clubs;
        var nt=new Contract(6,Suit.Unknown);
        Assert.AreEqual(Suit.Clubs,au.LedSuit([new(0,new(joker))],nt));
        Assert.AreEqual(club,au.LegalPlays([club,new(Suit.Diamonds,5)],[new(0,new(joker))],nt).Single().Card);
        Assert.AreEqual(1,au.Winner([new(1,new(new(Suit.Diamonds,9))),new(0,new(joker))],nt));
    }
    [TestMethod]
    public void SingleRoundRecordsInkleWithoutMakingItTheContract()
    {
        var r=RuleConfiguration.Load(FilePath("us-single-round-4p-v1"));
        var game=new Engine(r).Run(Enumerable.Range(0,4).Select(s=>(IPlayer)new AuctionScript(s)).ToArray(),11,1,3);
        var round=game.Rounds.Single();
        Assert.AreEqual(4,round.Decisions.Count(x=>x.Phase=="bid"));
        Assert.AreEqual(ContractKind.Inkle,round.Decisions.First().Bid!.Kind);
        Assert.AreEqual(new Contract(7,Suit.Hearts),round.Contract);
        ArenaCommand.Replay(JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(game,ArenaCommand.Json),ArenaCommand.Json)!);
    }
    private sealed class AuctionScript(int seat):IPlayer
    {
        public string Name=>"script";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>seat==0?legal.First(c=>c?.Kind==ContractKind.Inkle):seat==2?new(7,Suit.Hearts):null;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>v.Hand.Take(count).ToArray();
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>legal.First();
    }
    private static void Reject(Action<JsonObject> mutation)
    {
        var node=JsonNode.Parse(System.IO.File.ReadAllText(FilePath("legacy-au-4p-v1")))!.AsObject();mutation(node);
        string file=Path.GetTempFileName();try{System.IO.File.WriteAllText(file,node.ToJsonString());try{RuleConfiguration.Load(file);Assert.Fail("Expected configuration rejection");}catch(ArgumentException){}catch(NotSupportedException){}}finally{System.IO.File.Delete(file);}
    }
    [TestMethod]
    public void InvalidStructureAndConflictingConfigurationsAreRejected()
    {
        Reject(n=>n["unknown"]=true);Reject(n=>n.Remove("play"));Reject(n=>n["deal"]!["kittySize"]=5);
        Reject(n=>n["auction"]!["mode"]="single_round");Reject(n=>n["deck"]!["jokerOrderHighToLow"]=new JsonArray("X2"));
        Reject(n=>n["contracts"]!["grand_nullo"]!["enabled"]=true);
    }
}
