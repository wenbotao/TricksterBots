#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
namespace TestBots;

[TestClass]
public class TestVisibilityFastV8
{
    private static Observation View(PlayingCard[] hand, PublicPlay[] trick, Contract? contract = null)
    {
        int seat = trick.Length;
        return new(seat, 3, 0, hand, Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, hand.Length, 0, [], 0, [], null)).ToArray(), [], contract ?? new(6, Suit.Unknown), 0, trick, trick, [0, 0], new(), []);
    }
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void CurrentQueenSeparatesLosingJackFromWinningKing(bool preferKing)
    {
        PlayingCard jack = new(Suit.Hearts, 11), king = new(Suit.Hearts, 13);
        var v = View([jack, king, new(Suit.Clubs, 5)], [new(0, new(new(Suit.Hearts, 12)))]);
        var legal = v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!); var baseline = new PlayAction(preferKing ? king : jack);
        // Historical V6 is intentionally frozen; V8 corrects this counterexample independently.
        Assert.AreEqual(1, new CardFrameV1(v).Representatives(legal, baseline).Count);
        var actual = FastPruningV8.Representatives(v, legal, baseline);
        Assert.AreEqual(2, actual.Count); Assert.Contains(baseline, actual);
        Assert.AreEqual(0, FastPruningV8.WinnerAfter(v, new(jack)));
        Assert.AreEqual(v.Seat, FastPruningV8.WinnerAfter(v, new(king)));
    }
    [TestMethod]
    public void CurrentTrumpSeparatesAnUndertrumpFromAnOvertrump()
    {
        var v = View([new(Suit.Spades, 7), new(Suit.Spades, 9)], [new(0, new(new(Suit.Clubs, 14))), new(1, new(new(Suit.Spades, 8)))], new(6, Suit.Spades));
        var legal = v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!);
        Assert.AreEqual(2, FastPruningV8.Representatives(v, legal, legal[0]).Count);
    }
    [TestMethod]
    public void AQueenFromAnEarlierTrickDoesNotPreventSafeRankPruning()
    {
        var v = View([new(Suit.Hearts, 11), new(Suit.Hearts, 13)], [new(0, new(new(Suit.Hearts, 10)))]);
        v = v with { History = new PublicPlay[] { new(0, new(new(Suit.Hearts, 12))), new(1, new(new(Suit.Clubs, 12))), new(2, new(new(Suit.Spades, 12))), new(3, new(new(Suit.Diamonds, 12))) }.Concat(v.Trick).ToArray() };
        var legal = v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!);
        var actual = FastPruningV8.Representatives(v, legal, legal[1]);
        Assert.AreEqual(1, actual.Count); Assert.AreEqual(legal[1], actual[0]);
    }
    [TestMethod]
    public void PruningPreservesEveryCurrentWinnerClassAcrossRandomStates()
    {
        var random = new SeededRandom(2718); var rules = new Rules();
        foreach (var trump in Rules.Suits.Append(Suit.Unknown))
        for (int i = 0; i < 50; i++)
        {
            var deck = rules.Deck(); random.Shuffle(deck); var lead = deck[0];
            var v = View(deck.Skip(1).Take(10).ToArray(), [new(0, new(lead, lead.IsJoker && trump == Suit.Unknown ? Suit.Hearts : Suit.Unknown))], new(6, trump));
            var legal = rules.LegalPlays(v.Hand, v.Trick, v.Contract!);
            var pruned = FastPruningV8.Representatives(v, legal, legal[0]);
            foreach (var play in legal) Assert.IsTrue(pruned.Any(p => FastPruningV8.WinnerAfter(v, p) == FastPruningV8.WinnerAfter(v, play)));
            Assert.Contains(legal[0], pruned);
        }
    }
    [TestMethod]
    public void JokerDeclarationsAndMisereRemainSeparate()
    {
        var v = View([new(Suit.Joker, 16), new(Suit.Spades, 5)], []);
        var legal = v.Rules.LegalPlays(v.Hand, [], v.Contract!);
        Assert.AreEqual(5, FastPruningV8.Representatives(v, legal, legal[0]).Count);
        v = v with { Contract = new(0, Suit.Unknown, ContractKind.Misere) };
        Assert.AreSame(legal, FastPruningV8.Representatives(v, legal, legal[0]));
    }
    [TestMethod]
    public void LastSeatStaysCheapUnlessARealWinDuckChoiceIsCritical()
    {
        var v = View([new(Suit.Hearts, 13), new(Suit.Hearts, 11), new(Suit.Clubs, 5), new(Suit.Diamonds, 5)], [new(0, new(new(Suit.Hearts, 5))), new(1, new(new(Suit.Hearts, 6))), new(2, new(new(Suit.Hearts, 12)))]);
        var legal = v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!); var baseline = new PlayAction(new(Suit.Hearts, 13));
        Assert.AreEqual(1, FastPruningV8.LastSeatChoices(v, legal, baseline).Count);
        var critical = v with { Players = v.Players.Select(p => p.Seat == 0 ? p with { Tricks = 4 } : p).ToArray() };
        var choices = FastPruningV8.LastSeatChoices(critical, legal, baseline);
        Assert.AreEqual(2, choices.Count); Assert.AreEqual(baseline, choices[0]);
        Assert.AreNotEqual(FastPruningV8.WinnerAfter(v, choices[0]), FastPruningV8.WinnerAfter(v, choices[1]));
    }
    [TestMethod]
    public void LastSeatCanConsiderTakingOverPartnerToCashAnEstablishedSuit()
    {
        var v = View([new(Suit.Hearts, 14), new(Suit.Hearts, 4), new(Suit.Clubs, 14), new(Suit.Clubs, 13), new(Suit.Clubs, 12)], [new(0, new(new(Suit.Hearts, 5))), new(1, new(new(Suit.Hearts, 13))), new(2, new(new(Suit.Hearts, 6)))]);
        var baseline = new PlayAction(new(Suit.Hearts, 4)); var legal = v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!);
        var choices = FastPruningV8.LastSeatChoices(v, legal, baseline);
        Assert.AreEqual(2, choices.Count); Assert.AreEqual(v.Seat, FastPruningV8.WinnerAfter(v, choices[1]));
    }
    [TestMethod]
    public void NoLastSeatSearchWhenEveryMoveLosesToTheSameCard()
    {
        var v = View([new(Suit.Hearts, 4), new(Suit.Hearts, 5)], [new(0, new(new(Suit.Hearts, 6))), new(1, new(new(Suit.Hearts, 10))), new(2, new(new(Suit.Hearts, 12)))]);
        var legal = v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!);
        Assert.AreEqual(1, FastPruningV8.LastSeatChoices(v, legal, legal[0]).Count);
    }
    [TestMethod]
    public void UnequalWeightsUseEffectiveSampleSizeAndDominatedSamplesKeepBaseline()
    {
        double[] d = [100, -20, 60, 0], w = [9, 1, 1, 1];
        var actual = RiskEstimateV8.Evaluate(d, w);
        double ess = 144.0 / 84, mean = d.Select((x, i) => x * w[i]).Sum() / 12;
        double variance = d.Select((x, i) => w[i] * Math.Pow(x - mean, 2)).Sum() / 12;
        Assert.AreEqual(ess, actual.EffectiveSamples, 1e-10);
        Assert.AreEqual(Math.Sqrt(variance / Math.Max(1, ess - 1)), actual.StandardError, 1e-10);
        Assert.IsTrue(actual.StandardError > Math.Sqrt(variance / 3));
        Assert.IsTrue(double.IsNegativeInfinity(RiskEstimateV8.Evaluate([100, 100, 100, 100], [1000, 1, 1, 1]).Improvement));
        Assert.AreEqual(4.0, RiskEstimateV8.Evaluate(d, [1, 1, 1, 1]).EffectiveSamples);
    }
    private sealed class BudgetGuard(int seed) : IPlayer
    {
        private readonly VisibilityFastV8 inner = new(seed);
        public string Name => inner.Name;
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) { var result = inner.Bid(v, legal); Assert.AreEqual(0, inner.LastSearch!.Rollouts); return result; }
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count)
        {
            var result = inner.Discard(v, count); var report = inner.LastSearch!;
            Assert.IsTrue(report.Candidates <= 4 && report.SamplesUsed <= 4 && report.Rollouts <= 16); return result;
        }
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal)
        {
            var result = inner.Play(v, legal); var report = inner.LastSearch!;
            if (v.Trick.Count == (v.Contract!.IsMisere ? 2 : 3)) Assert.IsTrue(report.Candidates <= 2 && report.SamplesUsed <= 2 && report.Rollouts <= 4);
            else Assert.IsTrue(report.SamplesUsed <= (v.Hand.Count >= 7 ? 4 : 8));
            return result;
        }
    }
    [TestMethod]
    public void FullGamesStayLegalBoundedAndReproducible()
    {
        IPlayer[] Make() => [new BudgetGuard(101), new MediumBotPlayer(), new BudgetGuard(103), new MediumBotPlayer()];
        var a = new Engine(new()).Run(Make(), 2101001, 60); var b = new Engine(new()).Run(Make(), 2101001, 60);
        Assert.IsFalse(a.Truncated);
        Assert.AreEqual(JsonSerializer.Serialize(a, LabJson.Options), JsonSerializer.Serialize(b, LabJson.Options));
        ArenaCommand.Replay(a);
    }
    [TestMethod]
    public void V8SourceAndFixedBudgetsHaveTheirOwnFrozenManifest()
    {
        string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(SourcePath()))!;
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "FiveHundredLab/frozen-profiles-v2.json")))!;
        Assert.AreEqual(2, entries.Count);
        foreach (var item in entries) Assert.AreEqual(item.Value, FrozenSourceHashV1.Compute(System.IO.Path.Combine(root, item.Key)));
        Assert.AreEqual("visibility-fast-v8", Profiles.Create("visibility-fast-v8", 1).Name);
        Assert.AreEqual("visibility-fast-v6", Profiles.Create("visibility-fast-v6", 1).Name);
    }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

}
