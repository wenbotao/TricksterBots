#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
using Bots = FiveHundredLab.Bots.FiveHundred;
namespace TestBots;

[TestClass]
public class TestStrongTactical
{
    private static PlayingCard H(int rank) => new(Suit.Hearts, rank);
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    private static Observation LastSeat(Contract contract, int declarer = 0, Rules? rules = null)
    {
        var trick = new[] { new PublicPlay(1, new(H(12))), new PublicPlay(2, new(H(9))), new PublicPlay(3, new(H(10))) };
        return new(0, 0, 1, new[] { H(14), H(8) },
            Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, 2, 0, [], s == 0 ? 5 : 0, [], null)).ToArray(),
            [], contract, declarer, trick, trick, [0, 0], rules ?? new Rules(), []);
    }
    [TestMethod]
    public void NormalMakeAndSetUseActualTeamAndContractThreshold()
    {
        var make = LastSeat(new(6, Suit.Unknown));
        var legal = make.Rules.LegalPlays(make.Hand, make.Trick, make.Contract!);
        var a = Bots.StrongTacticalBot.ApplyPublicTactics(make, legal);
        Assert.AreEqual("normal-certain-make", a.Reason); Assert.AreEqual(H(14), a.Choices.Single().Card);
        var set = make with { Declarer = 1, Players = make.Players.Select(p => p.Seat == 0 ? p with { Tricks = 4 } : p).ToArray() };
        var b = Bots.StrongTacticalBot.ApplyPublicTactics(set, legal);
        Assert.AreEqual("normal-certain-set", b.Reason); Assert.AreEqual(H(14), b.Choices.Single().Card);
        var notCritical = make with { Players = make.Players.Select(p => p with { Tricks = 0 }).ToArray() };
        Assert.IsNull(Bots.StrongTacticalBot.ApplyPublicTactics(notCritical, legal).Reason);
    }
    [TestMethod]
    public void FourPlayerMiserePartnerMustHelpDeclarerAvoidTheTrick()
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "FiveHundredRules/presets/legacy-au-4p-v1.json")))!;
        node["contracts"]!["misere"]!["activeSeats"] = "all_four";
        var rules = (ConfiguredRules)RuleConfiguration.Load(Path.Combine(Root(), "FiveHundredRules/presets/legacy-au-4p-v1.json"));
        rules = rules with { Document = node.ToJsonString() }; rules.Validate();
        var view = LastSeat(new(0, Suit.Unknown, ContractKind.Misere), 2, rules);
        var trick = new[] { new PublicPlay(1, new(H(10))), new PublicPlay(2, new(H(12))), new PublicPlay(3, new(H(9))) };
        view = view with { Players = view.Players.Select(p => p with { Tricks = 0 }).ToArray(), Trick = trick, History = trick };
        var legal = rules.LegalPlays(view.Hand, view.Trick, view.Contract!);
        var result = Bots.StrongTacticalBot.ApplyPublicTactics(view, legal);
        Assert.AreEqual("misere-avoid-certain-loss", result.Reason);
        Assert.AreEqual(H(14), result.Choices.Single().Card);
    }
    [TestMethod]
    public void BudgetExhaustionAndAlreadyFailedMisereDoNotInventProofs()
    {
        var v = LastSeat(new(6, Suit.Unknown));
        var legal = v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!);
        var result = Bots.StrongTacticalBot.ApplyPublicTactics(v, legal, 0);
        CollectionAssert.AreEqual(legal.ToArray(), result.Choices); Assert.IsNull(result.Reason); Assert.AreEqual(0, result.Nodes);
        v = v with { Contract = new(0, Suit.Unknown, ContractKind.Misere) };
        Assert.IsNull(Bots.StrongTacticalBot.ApplyPublicTactics(v, legal).Reason);
    }
    [TestMethod]
    public void PublicOpenMisereForcedBustIsTakenAcrossSeeds()
    {
        var f = UxEvaluationV1.Build().Single(x => x.Scenario.Id == "open-misere-forced-bust");
        foreach (int seed in new[] { 71, 83, 97, 109 })
        {
            var bot = new Bots.StrongTacticalBot(seed, f.View.Rules);
            var move = bot.Play(f.View, f.LegalPlays);
            Assert.IsTrue(f.LegalPlays.Contains(move));
            foreach (var check in f.Scenario.Checks) Assert.IsTrue(check.Accept(f.View, new UxActionV1(Play: move)), check.Id);
            Assert.AreEqual("misere-certain-set", bot.LastTactics!.Reason);
        }
    }
    [TestMethod]
    public void BiddingAndDiscardMatchAll32WithIdenticalFreshRandomStreams()
    {
        foreach (var f in UxEvaluationV1.Build().Where(x => x.Scenario.Phase != "play"))
        {
            var a = new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32, 71, f.View.Rules);
            var b = new Bots.StrongTacticalBot(71, f.View.Rules);
            if (f.Scenario.Phase == "bid") Assert.AreEqual(a.Bid(f.View, f.LegalBids), b.Bid(f.View, f.LegalBids));
            else CollectionAssert.AreEquivalent(a.Discard(f.View, f.DiscardCount).ToArray(), b.Discard(f.View, f.DiscardCount).ToArray());
        }
        Assert.IsInstanceOfType<Bots.StrongTacticalBot>(Profiles.Create("strong-tactical-v1", 1));
    }
    [TestMethod]
    public void ProofsSurviveEveryActualReplyInSmallHands()
    {
        int proofs = 0;
        var paths = new string?[] { null }.Concat(new[] { "presets", "custom", "examples" }.SelectMany(d => Directory.GetFiles(Path.Combine(Root(), "FiveHundredRules", d), "*.json")));
        foreach (var path in paths)
        {
            var rules = RuleConfiguration.Load(path); var random = new SeededRandom(61331);
            foreach (bool misere in new[] { false, true }) for (int trial = 0; trial < 24; trial++)
            {
                var contract = new Contract(misere ? 0 : 6, misere ? Suit.Unknown : Suit.Hearts, misere ? ContractKind.Misere : ContractKind.Normal);
                if (misere && !rules.AllowMisere) continue;
                bool four = !misere || rules is ConfiguredRules c && ConfiguredRules.Text(c.ContractOptions(contract), "activeSeats") == "all_four";
                var active = Enumerable.Range(0, 4).Where(s => four || s != 2).ToArray();
                var deck = rules.Deck(); random.Shuffle(deck);
                var hands = Enumerable.Range(0, 4).Select(s => deck.Skip(2 * s).Take(2).ToArray()).ToArray();
                var view = new Observation(0, 0, 9, hands[0], Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, 2, 0, [], !misere && s == 0 ? 5 : 0, [], s == 0 || trial % 2 == 0 && s == 1 ? null : hands[s])).ToArray(), [], contract, 0, [], [], [0, 0], rules, []);
                var before = JsonSerializer.Serialize(rules, ArenaCommand.Json);
                var legal = rules.LegalPlays(hands[0], [], contract);
                var result = Bots.StrongTacticalBot.ApplyPublicTactics(view, legal);
                Assert.IsTrue(result.Nodes <= 8192);
                Assert.AreEqual(before, JsonSerializer.Serialize(rules, ArenaCommand.Json));
                if (result.Reason == null) continue;
                proofs++;
                foreach (var move in legal)
                {
                    bool check = result.Reason == "misere-avoid-certain-loss" ? !result.Choices.Contains(move) : result.Choices.Contains(move);
                    if (!check) continue;
                    var prefix = new System.Collections.Generic.List<PublicPlay> { new(0, move) };
                    var winners = new System.Collections.Generic.List<int>();
                    Replies(1);
                    Assert.IsTrue(winners.Count > 0);
                    Assert.IsTrue(winners.All(w => misere ? w == 0 : w % 2 == 0), result.Reason);
                    void Replies(int index)
                    {
                        if (index == active.Length) { winners.Add(rules.Winner(prefix, contract)); return; }
                        int seat = active[index];
                        Rules r = rules is ConfiguredRules configured ? configured with { CurrentSeat = seat, DeclarerSeat = 0, History = prefix.ToArray(), Declarations = new(configured.Declarations) } : rules;
                        foreach (var reply in r.LegalPlays(hands[seat], prefix, contract))
                        {
                            prefix.Add(new(seat, reply)); Replies(index + 1); prefix.RemoveAt(prefix.Count - 1);
                        }
                    }
                }
            }
        }
        Assert.IsTrue(proofs > 10, "The soundness check must exercise real interventions");
    }
    [TestMethod]
    public void ConfigurationsReplayAndRejectMismatchedRules()
    {
        foreach (var path in new string?[] { null }.Concat(new[] { "presets", "custom", "examples" }.SelectMany(d => Directory.GetFiles(Path.Combine(Root(), "FiveHundredRules", d), "*.json"))))
        {
            var rules = RuleConfiguration.Load(path);
            var bot = new Bots.StrongTacticalBot(7, rules);
            var game = new Engine(rules).Run([bot, new Bots.MediumBot(8, rules), new Bots.MediumBot(9, rules), new Bots.MediumBot(10, rules)], 1919911, 2);
            ArenaCommand.Replay(game);
            if (path != null) Assert.Throws<ArgumentException>(() => bot.Play(LastSeat(new(6, Suit.Unknown)), [new(H(8))]));
        }
    }
}
