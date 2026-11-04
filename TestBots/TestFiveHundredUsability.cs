#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;

namespace TestBots;

[TestClass]
public class TestFiveHundredUsability
{
    private static readonly Lazy<IReadOnlyList<CapturedUxV1>> Fixtures = new(UxEvaluationV1.Build);
    private static CapturedUxV1 Fixture(string id) => Fixtures.Value.Single(c => c.Scenario.Id == id);
    private static PlayAction Play(string token) => new(UxFixturesV1.Cards(token).Single());
    private static bool Accept(string scenario, string card) => Fixture(scenario).Scenario.Checks.Single().Accept(Fixture(scenario).View, new(Play: Play(card)));

    [TestMethod]
    public void EveryProbeIsReachableThroughTheRealEngineWithoutHiddenInformation()
    {
        Assert.AreEqual(17, Fixtures.Value.Count);
        Assert.AreEqual(17, Fixtures.Value.Select(c => c.Scenario.Id).Distinct().Count());
        foreach (var c in Fixtures.Value)
        {
            var v = c.View;
            Assert.AreEqual(0, v.Seat);
            Assert.AreEqual(typeof(Rules), v.Rules.GetType(), "The inverse-deal rule must not reach a bot");
            Assert.AreEqual(43, c.InitialHands.SelectMany(h => h).Concat(c.Kitty).Distinct().Count());
            Assert.IsTrue(c.InitialHands.All(h => h.Length == 10));
            Assert.IsTrue(v.Players.Where(p => p.Seat != v.Declarer || v.Contract?.Kind != ContractKind.OpenMisere)
                .All(p => p.ExposedHand == null));
            if (v.Declarer != 0) Assert.AreEqual(0, v.OwnDiscards.Count);
            if (c.Scenario.Phase == "bid") Assert.AreEqual(10, v.Hand.Count);
            if (c.Scenario.Phase == "discard") { Assert.AreEqual(13, v.Hand.Count); Assert.AreEqual(3, c.DiscardCount); }
            if (c.Scenario.Phase == "play")
            {
                CollectionAssert.AreEquivalent(v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!).ToArray(), c.LegalPlays.ToArray());
                Assert.AreEqual(v.History.Count(p => p.Seat == 0), 10 - v.Hand.Count);
            }
        }
        var exit = Fixture("bid-partner-can-exit").View;
        Assert.AreEqual(480, exit.Scores[0]);
        Assert.IsTrue(exit.Players.All(p => p.Score == exit.Scores[p.Seat % 2]));
    }

    [TestMethod]
    public void ImpossibleDealsAreRejectedBeforeBotEvaluation()
    {
        var spec = Fixture("bid-weak-opening").Scenario;
        var duplicate = spec with { ReservedHands = new Dictionary<int, PlayingCard[]> { [0] = spec.ReservedHands[0], [1] = [spec.ReservedHands[0][0]] } };
        Assert.ThrowsExactly<InvalidDataException>(() => UxCaptureV1.Capture(duplicate));
    }

    [TestMethod]
    public void MisereJudgementsDistinguishAvoidableLossFromForcedFollow()
    {
        Assert.IsTrue(Accept("misere-own-lead", "4H"));
        Assert.IsFalse(Accept("misere-own-lead", "AH"));
        Assert.IsTrue(Accept("misere-own-duck", "4H"));
        Assert.IsFalse(Accept("misere-own-duck", "AH"));
        var forced = Fixture("misere-forced-high-control");
        Assert.AreEqual(1, forced.LegalPlays.Count);
        Assert.AreEqual(UxLevelV1.Control, forced.Scenario.Checks.Single().Level);
        Assert.IsTrue(Accept(forced.Scenario.Id, "AH"));
        Assert.IsTrue(Accept("misere-defence-dont-rescue", "4H"));
        Assert.IsFalse(Accept("misere-defence-dont-rescue", "9H"));
    }

    [TestMethod]
    public void OpenMisereCertaintyUsesOnlyPublicInformationAndNotTheActualHiddenPartnerHand()
    {
        var c = Fixture("open-misere-forced-bust");
        Assert.AreEqual(9, c.View.Players[1].ExposedHand!.Count);
        Assert.IsTrue(UxFixturesV1.ForcesMisereBust(c.View, Play("5H")));
        Assert.IsFalse(UxFixturesV1.ForcesMisereBust(c.View, Play("4D")));
        // Witness against calling diamonds a sure bust: human could hold AD and rescue the declarer.
        Assert.AreEqual(2, c.View.Rules.Winner([new(0, Play("4D")), new(1, Play("9D")), new(2, Play("AD"))], c.View.Contract!));
        foreach (var reply in c.View.Players[1].ExposedHand!.Where(p => p.Suit == Suit.Hearts))
        {
            var known = c.View.Hand.Concat(c.View.Players[1].ExposedHand!).Concat(c.View.History.Select(p => p.Action.Card));
            foreach (var possible in c.View.Rules.Deck().Except(known))
                Assert.AreEqual(1, c.View.Rules.Winner([new(0, Play("5H")), new(1, new(reply)), new(2, new(possible))], c.View.Contract!));
        }
    }

    [TestMethod]
    public void PreservingPartnerWinnersAndDiscardingJokerHaveExplicitCounterexamples()
    {
        Assert.IsTrue(Accept("play-protect-human-winner", "4H"));
        Assert.IsFalse(Accept("play-protect-human-winner", "KH"));
        var c = Fixture("discard-misere-joker");
        Assert.IsTrue(c.Scenario.Checks.Single().Accept(c.View, new(Discards: UxFixturesV1.Cards("HJ AS AH"))));
        Assert.IsFalse(c.Scenario.Checks.Single().Accept(c.View, new(Discards: UxFixturesV1.Cards("5S AS AH"))));
        foreach (var signal in Fixtures.Value.Where(f => f.Scenario.Convention != null))
            Assert.IsTrue(signal.Scenario.Checks.All(k => k.Level == UxLevelV1.Advisory));
    }

    private sealed class BadPlayer(bool throws) : IPlayer
    {
        public string Name => "invalid-test-actor";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => throw new NotSupportedException();
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => throw new NotSupportedException();
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => throws ? throw new InvalidOperationException("fixture-error") : new(new(Suit.Clubs, 99));
    }

    [TestMethod]
    public void TechnicalErrorsAreSeparateFromLegalButUndesirableDecisions()
    {
        var c = Fixture("misere-own-duck");
        foreach (bool throws in new[] { false, true })
        {
            var row = UxEvaluationV1.Evaluate(c, "custom", 1, false, () => new BadPlayer(throws));
            Assert.IsFalse(row.Legal); Assert.IsNotNull(row.Error); Assert.IsFalse(row.Checks.Values.Any(x => x));
        }
        foreach (var profile in UxEvaluationV1.TestedProfiles)
        {
            var row = UxEvaluationV1.Evaluate(Fixture("misere-forced-high-control"), profile, 7, false);
            Assert.IsTrue(row.Legal); Assert.IsNull(row.Error); Assert.IsTrue(row.Checks.Values.All(x => x));
        }
    }

    [TestMethod]
    public void FreshActorsReproduceSeededDecisionsForEachHandOrder()
    {
        var c = Fixture("play-protect-human-winner");
        foreach (var profile in UxEvaluationV1.TestedProfiles)
        foreach (bool reversed in new[] { false, true })
        {
            var first = UxEvaluationV1.Evaluate(c, profile, 314159, reversed);
            var second = UxEvaluationV1.Evaluate(c, profile, 314159, reversed);
            Assert.IsTrue(first.Legal); Assert.IsNull(first.Error);
            Assert.AreEqual(JsonSerializer.Serialize(first.Action, LabJson.Options), JsonSerializer.Serialize(second.Action, LabJson.Options));
        }
    }

    [TestMethod]
    public void EvaluationWritesAuditableLayeredResultsWithoutOverwritingAnExistingRun()
    {
        string output = Path.Combine(Path.GetTempPath(), "fivehundred-ux-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            UxEvaluationV1.Run(output, 2, 314159);
            var lines = File.ReadAllLines(Path.Combine(output, "trials.jsonl"));
            Assert.AreEqual(170, lines.Length);
            var trials = lines.Select(l => JsonSerializer.Deserialize<UxTrialV1>(l, LabJson.Options)!).ToArray();
            Assert.IsTrue(trials.All(t => t.Legal && t.Error == null));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "ux-results.json")));
            foreach (var rate in report.RootElement.GetProperty("Results").EnumerateArray())
            {
                string id = rate.GetProperty("Scenario").GetString()!, profile = rate.GetProperty("Profile").GetString()!, check = rate.GetProperty("Check").GetString()!;
                Assert.AreEqual(trials.Count(t => t.Scenario == id && t.Profile == profile && t.Checks[check]), rate.GetProperty("Passed").GetInt32());
            }
            Assert.ThrowsExactly<IOException>(() => UxEvaluationV1.Run(output, 2));
            Assert.ThrowsExactly<ArgumentException>(() => UxEvaluationV1.Run(output + "-invalid", 3));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }
}
