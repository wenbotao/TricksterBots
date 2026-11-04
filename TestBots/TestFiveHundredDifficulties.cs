#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.Bots;
using Trickster.cloud;

namespace TestBots;

[TestClass]
public class TestFiveHundredDifficulties
{
    private static Observation View(PlayingCard[]? hand = null, Contract? contract = null, int seat = 0)
    {
        var cards = hand ?? new Rules().Deck().Take(10).ToArray();
        return new(seat, 3, 0, cards, Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, s == seat ? cards.Length : 10, 0, [], 0, [], null)).ToArray(),
            [], contract, contract == null ? null : 0, [], [], [0, 0], new(), []);
    }
    [TestMethod]
    public void MediumInheritsEveryDecisionFromOriginalAndAdapterIsEquivalent()
    {
        foreach (string method in new[] { "SuggestBid", "SuggestDiscard", "SuggestNextCard", "SuggestPass", "SuitOrder" })
            Assert.AreEqual(typeof(FiveHundredBot), typeof(MediumBot).GetMethod(method)!.DeclaringType);
        var old = new Engine(new()).Run(Enumerable.Range(0, 4).Select(_ => (IPlayer)new ExistingBotPlayer()).ToArray(), 151, 30);
        var renamed = new Engine(new()).Run(Enumerable.Range(0, 4).Select(_ => (IPlayer)new MediumBotPlayer()).ToArray(), 151, 30);
        renamed.Players = old.Players;
        Assert.AreEqual(JsonSerializer.Serialize(old, ArenaCommand.Json), JsonSerializer.Serialize(renamed, ArenaCommand.Json));
    }
    [TestMethod]
    public void RandomLegalSamplesCardsNotExpandedJokerActionsAndIncludesPassUniformly()
    {
        var joker = new PlayingCard(Suit.Joker, 16); var card = new PlayingCard(Suit.Spades, 5);
        var view = View([joker, card], new(6, Suit.Unknown));
        var legal = view.Rules.LegalPlays(view.Hand, [], view.Contract!);
        var random = new RandomLegalBot(12345);
        int jokers = 0; var suits = Rules.Suits.ToDictionary(s => s, _ => 0);
        for (int i = 0; i < 12000; i++)
        {
            var p = random.Play(view, legal); Assert.Contains(p, legal);
            if (p.Card.IsJoker) { jokers++; suits[p.JokerLeadSuit]++; }
        }
        Assert.IsTrue(jokers > 5600 && jokers < 6400, $"Joker count={jokers}");
        Assert.IsTrue(suits.Values.All(n => n > 1300 && n < 1700));
        Contract?[] bids = [null, new(6, Suit.Clubs), new(6, Suit.Hearts)]; var counts = new int[3];
        for (int i = 0; i < 9000; i++) counts[Array.IndexOf(bids, random.Bid(view, bids))]++;
        Assert.IsTrue(counts.All(n => n > 2800 && n < 3200));
        var wholeHand = View(); var frequencies = wholeHand.Hand.ToDictionary(c => c, _ => 0);
        for (int i = 0; i < 5000; i++)
        {
            var discard = random.Discard(wholeHand, 3);
            Assert.AreEqual(3, discard.Distinct().Count());
            foreach (var c in discard) frequencies[c]++;
        }
        Assert.IsTrue(frequencies.Values.All(n => n > 1350 && n < 1650));
    }
    [TestMethod]
    public void SamplerPreservesVoidsExposedHandsOwnDiscardsAndEveryCardExactlyOnce()
    {
        var rules = new Rules(); var deck = rules.Deck();
        PublicPlay P(int s, Suit suit, int rank) => new(s, new(new(suit, rank)));
        PublicPlay[] history = [P(0, Suit.Spades, 5), P(1, Suit.Clubs, 5), P(2, Suit.Spades, 6), P(3, Suit.Spades, 7)];
        var hand = deck.Where(c => c.Suit == Suit.Hearts).Take(9).ToArray();
        var exposed = deck.Where(c => c.Suit == Suit.Diamonds).Take(9).ToArray();
        var discard = deck.Where(c => c.Suit == Suit.Hearts).Except(hand).Concat(deck.Where(c => c.Suit == Suit.Diamonds).Except(exposed)).Take(3).ToArray();
        var players = Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, 9, 0, [], s == 3 ? 1 : 0, [], s == 2 ? exposed : null)).ToArray();
        var view = View(hand, new(6, Suit.Unknown)) with { Players = players, History = history, OwnDiscards = discard };
        var samples = BeliefSampler.Sample(view, 64, new(100));
        foreach (var sample in samples)
        {
            Assert.AreEqual(0, sample.Dead.Length);
            CollectionAssert.AreEquivalent(hand, sample.Hands[0]); CollectionAssert.AreEquivalent(exposed, sample.Hands[2]);
            Assert.IsFalse(sample.Hands[1].Any(c => c.Suit == Suit.Spades));
            var all = sample.Hands.SelectMany(h => h).Concat(history.Select(p => p.Action.Card)).Concat(discard).ToArray();
            Assert.AreEqual(43, all.Length); Assert.AreEqual(43, all.Distinct().Count());
        }
        var again = BeliefSampler.Sample(view, 64, new(100));
        Assert.AreEqual(JsonSerializer.Serialize(samples, ArenaCommand.Json), JsonSerializer.Serialize(again, ArenaCommand.Json));
        var defender = view with { OwnDiscards = [] };
        Assert.IsTrue(BeliefSampler.Sample(defender, 4, new(1)).All(s => s.Dead.Length == 3));
    }
    [TestMethod]
    public void SamplerRejectsImpossibleVoidsAndKeepsInactiveMiserePartner()
    {
        var c = new Contract(0, Suit.Unknown, ContractKind.Misere);
        var normal = View(contract: c);
        foreach (var sample in BeliefSampler.Sample(normal, 4, new(5)))
        { Assert.AreEqual(10, sample.Hands[2].Length); Assert.AreEqual(3, sample.Dead.Length); }
        var own = new Rules().Deck().Where(x => x.Suit == Suit.Hearts).Take(9).ToArray();
        var players = Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, 9, 0, [], 0, [], null)).ToArray();
        // Three players have shown spade voids; too many unknown spades remain for the dead pile.
        PublicPlay[] history = [new(0, new(new(Suit.Spades, 5))), new(1, new(new(Suit.Clubs, 5))), new(2, new(new(Suit.Clubs, 6))), new(3, new(new(Suit.Clubs, 7)))];
        var impossible = View(own, new(6, Suit.Unknown)) with { Players = players, History = history };
        Assert.ThrowsExactly<InvalidDataException>(() => BeliefSampler.Sample(impossible, 1, new(6)));
    }
    [TestMethod]
    public void VoidInferenceUsesLeftBowerAndJokerNamedSuit()
    {
        var c = new Contract(6, Suit.Hearts);
        PublicPlay[] h = [new(0, new(new(Suit.Hearts, 5))), new(1, new(new(Suit.Diamonds, 11))), new(2, new(new(Suit.Clubs, 11)))];
        var voids = BeliefSampler.Voids(View(contract: c) with { History = h });
        Assert.IsFalse(voids[1].Contains(Suit.Hearts)); Assert.IsTrue(voids[2].Contains(Suit.Hearts));
        h = [new(0, new(new(Suit.Joker, 16), Suit.Diamonds)), new(1, new(new(Suit.Clubs, 5)))];
        voids = BeliefSampler.Voids(View(contract: new(6, Suit.Unknown)) with { History = h });
        Assert.IsTrue(voids[1].Contains(Suit.Diamonds));
    }
    private sealed class ObservingPolicy(int seat, Contract target, Action<Observation, string> observe) : IPlayer
    {
        public string Name => "observation-only-policy";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => seat == 0 && legal.Contains(target) ? target : null;
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) { observe(v, "discard"); return v.Hand.Take(count).ToArray(); }
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) { observe(v, "play"); return new MediumBotPlayer().Play(v, legal); }
    }
    [TestMethod]
    [DataRow(ContractKind.Normal, Suit.Hearts)]
    [DataRow(ContractKind.Normal, Suit.Unknown)]
    [DataRow(ContractKind.Misere, Suit.Unknown)]
    [DataRow(ContractKind.OpenMisere, Suit.Unknown)]
    public void ForwardModelMatchesAuthoritativeEngineFromDiscardAndMidTrick(ContractKind kind, Suit trump)
    {
        var contract = new Contract(kind == ContractKind.Normal ? 6 : 0, trump, kind);
        var rules = new Rules { MisereRequiresSeven = false, OpenMisereRequiresEight = false };
        var observations = new List<(Observation View, string Phase)>();
        var bots = Enumerable.Range(0, 4).Select(s => (IPlayer)new ObservingPolicy(s, contract, (v, phase) => observations.Add((v, phase)))).ToArray();
        var game = new Engine(rules).Run(bots, 619, 1); var round = game.Rounds[0];
        foreach (var (view, phase) in observations)
        {
            var hands = round.Hands.Select(h => h.ToList()).ToArray(); hands[0].AddRange(round.Kitty);
            var discard = round.Decisions.Single(d => d.Phase == "discard").Discards!;
            if (phase == "play") foreach (var card in discard) hands[0].Remove(card);
            foreach (var p in view.History) hands[p.Seat].Remove(p.Action.Card);
            var sample = new SampledDeal(hands.Select(h => h.ToArray()).ToArray(), phase == "play" && view.Seat != 0 ? discard : [], 1);
            double actual;
            if (phase == "discard") actual = new ForwardModel(view, sample).Discard(discard);
            else actual = new ForwardModel(view, sample).Play(new MediumBotPlayer().Play(view, view.Rules.LegalPlays(view.Hand, view.Trick, contract)));
            int team = view.Seat % 2;
            double expected = game.Winner.HasValue ? (game.Winner == team ? 1 : -1) * 4.0 * rules.WinScore : round.ScoreDelta[team] - round.ScoreDelta[1 - team];
            Assert.AreEqual(expected, actual, $"phase={phase}, history={view.History.Count}");
        }
    }
    private sealed class AuctionContinuation(int seat, Contract? firstBid, Action<Observation> capture) : IPlayer
    {
        private readonly MediumBotPlayer medium = new();
        public string Name => "medium-continuation";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal)
        {
            if (seat == 0 && v.Auction.Count == 0) { capture(v); return firstBid; }
            return medium.Bid(v, legal);
        }
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => medium.Discard(v, count);
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => medium.Play(v, legal);
    }
    [TestMethod]
    [DataRow(0, true)][DataRow(1, true)][DataRow(2, true)]
    [DataRow(0, false)][DataRow(1, false)][DataRow(2, false)]
    public void AuctionForwardModelMatchesFullContinuationAndScoring(int choice, bool permanentPass)
    {
        Contract? bid = choice == 0 ? null : choice == 1 ? new(6, Suit.Hearts) : new(0, Suit.Unknown, ContractKind.Misere);
        var rules = new Rules { MisereRequiresSeven = false, PassIsPermanent = permanentPass };
        Observation? start = null;
        var bots = Enumerable.Range(0, 4).Select(s => (IPlayer)new AuctionContinuation(s, bid, v => start = v)).ToArray();
        var game = new Engine(rules).Run(bots, 819, 1);
        var round = game.Rounds.Single();
        var model = new ForwardModel(start!, new(round.Hands, round.Kitty, 1));
        double value = model.Bid(bid);
        double expected = game.Winner.HasValue ? (game.Winner == 0 ? 1 : -1) * 4.0 * rules.WinScore : round.ScoreDelta[0] - round.ScoreDelta[1];
        Assert.AreEqual(expected, value);
    }
    [TestMethod]
    public void NewBotsCompleteLegalGamesAndAreSeedReproducible()
    {
        foreach (string level in new[] { "weak", "random-legal", "strong" })
        {
            IPlayer[] Make() => Enumerable.Range(0, 4).Select(s => s % 2 == 1 ? (IPlayer)new MediumBotPlayer() : level switch
            {
                "weak" => new WeakBot(), "random-legal" => new RandomLegalBot(100 + s),
                _ => new StrongBot(100 + s, new StrongBotOptions { Samples = 2, DiscardCandidates = 3 })
            }).ToArray();
            var a = new Engine(new()).Run(Make(), 711, 60);
            var b = new Engine(new()).Run(Make(), 711, 60);
            Assert.IsFalse(a.Truncated, level);
            Assert.AreEqual(JsonSerializer.Serialize(a, ArenaCommand.Json), JsonSerializer.Serialize(b, ArenaCommand.Json), level);
            ArenaCommand.Replay(a);
        }
    }
    [TestMethod]
    public void StrongEvaluatesAllDiscardCombinationsAndUsesItsFixedBudget()
    {
        var r = new Rules(); var hand = r.Deck().Take(13).ToArray();
        var v = View(hand, new(6, Suit.Spades));
        Assert.AreEqual(286, HandEvaluator.DiscardSets(hand, 3).Count());
        var strong = new StrongBot(31, new StrongBotOptions { Samples = 3, DiscardCandidates = 5 });
        var discard = strong.Discard(v, 3);
        Assert.AreEqual(3, discard.Distinct().Count()); Assert.IsTrue(discard.All(hand.Contains));
        Assert.AreEqual(3, strong.LastSearch!.Samples);
        Assert.IsTrue(strong.LastSearch.Candidates is >= 5 and <= 6);
        Assert.ThrowsExactly<ArgumentException>(() => new StrongBot(1, new StrongBotOptions { Samples = 0 }));
    }
}
