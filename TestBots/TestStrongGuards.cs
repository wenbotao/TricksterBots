#nullable enable
using System;
using System.Linq;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
using Bots = FiveHundredLab.Bots.FiveHundred;
namespace TestBots;
[TestClass]
public class TestStrongGuards
{
    [TestMethod]
    public void NoTrumpRepairKeepsGuardOrBuriesKingWithoutSacrificingAces()
    {
        var f = UxEvaluationV1.Build().Single(x=>x.Scenario.Id=="discard-nt-stopper");
        var original = UxFixturesV1.Cards("5S 4D 4H");
        var repaired = Bots.StrongHandGuardBot.RepairNoTrumpDiscard(f.View,original);
        Assert.AreEqual(3,repaired.Length); Assert.AreEqual(3,repaired.Distinct().Count()); Assert.IsTrue(repaired.All(f.View.Hand.Contains));
        Assert.IsTrue(!repaired.Contains(UxFixturesV1.Cards("5S")[0]) || repaired.Contains(UxFixturesV1.Cards("KS")[0]));
        Assert.IsFalse(repaired.Any(c=>c.Rank==14 || c.IsJoker));
        CollectionAssert.AreEquivalent(original,Bots.StrongHandGuardBot.RepairNoTrumpDiscard(f.View with {Contract=new(6,Suit.Hearts)},original));
        CollectionAssert.AreEquivalent(original,Bots.StrongHandGuardBot.RepairNoTrumpDiscard(f.View with {Contract=new(0,Suit.Unknown,ContractKind.Misere)},original));
        CollectionAssert.AreEquivalent(original,Bots.StrongHandGuardBot.RepairNoTrumpDiscard(f.View with {OwnDiscards=[new(Suit.Spades,14)]},original));
        foreach(int seed in new[]{71,83,97,109})
        {
            var bot=new Bots.StrongHandGuardBot(seed);
            var discards=bot.Discard(f.View,f.DiscardCount).ToArray();
            foreach(var check in f.Scenario.Checks) Assert.IsTrue(check.Accept(f.View,new UxActionV1(Discards:discards)));
        }
    }
    [TestMethod]
    public void BothNewProfilesLoadEveryConfigurationAndReplay()
    {
        string root=System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(Source()))!;
        var paths=new string?[]{null}.Concat(new[]{"presets","custom","examples"}.SelectMany(d=>System.IO.Directory.GetFiles(System.IO.Path.Combine(root,"FiveHundredRules",d),"*.json")));
        foreach(var path in paths)
        {
            var rules=RuleConfiguration.Load(path);
            foreach(bool combined in new[]{false,true})
            {
                IPlayer bot=combined?new Bots.StrongHandGuardBot(7,rules):new Bots.StrongBidGuardBot(7,rules);
                var game=new Engine(rules).Run([bot,new Bots.MediumBot(8,rules),new Bots.MediumBot(9,rules),new Bots.MediumBot(10,rules)],39119001,2);
                ArenaCommand.Replay(game);
                if(path!=null)
                {
                    var f=UxEvaluationV1.Build().First();
                    Assert.Throws<ArgumentException>(()=>bot.Bid(f.View,f.LegalBids));
                }
            }
        }
    }
    private static string Source([System.Runtime.CompilerServices.CallerFilePath]string p="")=>p;
    [TestMethod]
    public void WeakOpeningPassesButHonorsLengthCompetitionAndPressureAreExempt()
    {
        var f = UxEvaluationV1.Build().Single(x => x.Scenario.Id == "bid-weak-opening");
        Assert.IsTrue(Bots.StrongBidGuardBot.ShouldPassWeakOpening(f.View, f.LegalBids));
        foreach (int seed in new[] {71,83,97,109})
        {
            var bot = new Bots.StrongBidGuardBot(seed);
            Assert.IsNull(bot.Bid(f.View, f.LegalBids)); Assert.AreEqual("weak-balanced-opening", bot.LastBidGuard);
        }
        var v = f.View;
        Assert.IsFalse(Bots.StrongBidGuardBot.ShouldPassWeakOpening(v with { Hand = v.Hand.Skip(1).Prepend(new(Suit.Spades,14)).ToArray() }, f.LegalBids));
        Assert.IsFalse(Bots.StrongBidGuardBot.ShouldPassWeakOpening(v with { Hand = UxFixturesV1.Cards("5S 6S 7S 8S 9S 5C 6C 4D 4H 5H") }, f.LegalBids));
        Assert.IsFalse(Bots.StrongBidGuardBot.ShouldPassWeakOpening(v with { Auction = [new(1,new(6,Suit.Hearts))] }, f.LegalBids));
        foreach (var scores in new[] {new[] {-400,0},new[] {400,0},new[] {0,400}})
            Assert.IsFalse(Bots.StrongBidGuardBot.ShouldPassWeakOpening(v with {Scores=scores},f.LegalBids));
        Assert.IsFalse(Bots.StrongBidGuardBot.ShouldPassWeakOpening(v,f.LegalBids.Append(new(0,Suit.Unknown,ContractKind.Misere)).ToArray()));
        Assert.IsFalse(Bots.StrongBidGuardBot.ShouldPassWeakOpening(v,f.LegalBids.Where(b=>b!=null).ToArray()));
    }
    [TestMethod]
    public void NonGuardedBidsAndOtherPhasesMatchMisereOnlyOnFreshInputs()
    {
        foreach (var f in UxEvaluationV1.Build())
        {
            if (f.Scenario.Phase == "bid" && Bots.StrongBidGuardBot.ShouldPassWeakOpening(f.View,f.LegalBids)) continue;
            var a = new Bots.StrongProofAblationBot(Bots.StrongProofProfile.MisereOnly,71,f.View.Rules);
            var b = new Bots.StrongBidGuardBot(71,f.View.Rules);
            if (f.Scenario.Phase == "bid") Assert.AreEqual(a.Bid(f.View,f.LegalBids),b.Bid(f.View,f.LegalBids));
            else if (f.Scenario.Phase == "discard") CollectionAssert.AreEquivalent(a.Discard(f.View,f.DiscardCount).ToArray(),b.Discard(f.View,f.DiscardCount).ToArray());
            else Assert.AreEqual(a.Play(f.View,f.LegalPlays),b.Play(f.View,f.LegalPlays));
        }
    }
}
