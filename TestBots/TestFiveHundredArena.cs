#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;

namespace TestBots;

[TestClass]
public class TestFiveHundredArena
{
    private static PlayingCard C(Suit s, int rank) => new(s, rank);
    private static readonly PlayingCard Joker = C(Suit.Joker, 16);
    private static readonly Contract Hearts = new(6, Suit.Hearts);

    [TestMethod]
    public void AustralianDeckAndAuctionOrder()
    {
        var r = new Rules(); var deck = r.Deck();
        Assert.AreEqual(43, deck.Count); Assert.AreEqual(43, deck.Distinct().Count());
        Assert.IsTrue(deck.Contains(C(Suit.Hearts, 4))); Assert.IsFalse(deck.Contains(C(Suit.Spades, 4)));
        var m = new Contract(0, Suit.Unknown, ContractKind.Misere);
        var o = new Contract(0, Suit.Unknown, ContractKind.OpenMisere);
        Assert.IsTrue(r.Order(new(7, Suit.Unknown)) < r.Order(m));
        Assert.IsTrue(r.Order(m) < r.Order(new(8, Suit.Spades)));
        Assert.IsTrue(r.Order(new(10, Suit.Hearts)) < r.Order(o));
        Assert.IsTrue(r.Order(o) < r.Order(new(10, Suit.Unknown)));
        Assert.IsFalse(r.LegalBids(null, []).Contains(m));
        Assert.IsTrue(r.LegalBids(new(7, Suit.Spades), new[] { new AuctionAction(0, new(7, Suit.Spades)) }).Contains(m));
        Assert.IsFalse(r.LegalBids(new(8, Suit.Spades), new[] { new AuctionAction(0, new(8, Suit.Spades)) }).Contains(m));
    }

    [TestMethod]
    public void LeftBowerFollowsTrumpAndJokerCannotRevokeInNoTrump()
    {
        var r = new Rules(); var left = C(Suit.Diamonds, 11);
        var hand = new[] { left, C(Suit.Diamonds, 14) };
        var trick = new[] { new PublicPlay(0, new(C(Suit.Hearts, 5))) };
        CollectionAssert.AreEqual(new[] { new PlayAction(left) }, r.LegalPlays(hand, trick, Hearts));
        trick = [new(0, new(C(Suit.Diamonds, 5)))];
        CollectionAssert.AreEqual(new[] { new PlayAction(C(Suit.Diamonds, 14)) }, r.LegalPlays(hand, trick, Hearts));
        var nt = new Contract(6, Suit.Unknown);
        var legal = r.LegalPlays(new[] { Joker, C(Suit.Diamonds, 14) }, trick, nt);
        Assert.AreEqual(1, legal.Count); Assert.AreEqual(C(Suit.Diamonds, 14), legal[0].Card);
        Assert.AreEqual(4, r.LegalPlays(new[] { Joker }, [], nt).Count);
        trick = [new(0, new(Joker, Suit.Hearts))];
        legal = r.LegalPlays(new[] { C(Suit.Hearts, 4), C(Suit.Spades, 14) }, trick, nt);
        Assert.AreEqual(C(Suit.Hearts, 4), legal.Single().Card);
    }

    [TestMethod]
    public void TrumpRankingAndOffSuitCannotWin()
    {
        var r = new Rules();
        PublicPlay P(int s, Suit suit, int rank) => new(s, new(C(suit, rank)));
        Assert.AreEqual(1, r.Winner(new[] { P(0, Suit.Spades, 5), P(1, Suit.Diamonds, 11), P(2, Suit.Hearts, 14), P(3, Suit.Spades, 14) }, Hearts));
        Assert.AreEqual(2, r.Winner(new[] { P(0, Suit.Hearts, 14), P(1, Suit.Diamonds, 11), P(2, Suit.Hearts, 11) }, Hearts));
        Assert.AreEqual(3, r.Winner(new[] { P(0, Suit.Hearts, 14), P(1, Suit.Diamonds, 11), P(2, Suit.Hearts, 11), new PublicPlay(3, new(Joker)) }, Hearts));
        Assert.AreEqual(0, r.Winner(new[] { P(0, Suit.Spades, 5), P(1, Suit.Clubs, 14) }, new(6, Suit.Unknown)));
    }

    [TestMethod]
    public void ScoringSlamMisereAndMustContractToWin()
    {
        var r = new Rules();
        var scored = r.Score(new(6, Suit.Spades), 0, new[] { 5, 0, 5, 0 });
        Assert.AreEqual(250, scored.Delta[0]); Assert.AreEqual(0, scored.Delta[1]);
        scored = r.Score(Hearts, 0, new[] { 2, 4, 3, 1 });
        Assert.IsFalse(scored.Made); CollectionAssert.AreEqual(new[] { -100, 50 }, scored.Delta);
        scored = r.Score(new(0, Suit.Unknown, ContractKind.Misere), 0, new[] { 0, 5, 0, 5 });
        CollectionAssert.AreEqual(new[] { 250, 0 }, scored.Delta);
        Assert.IsNull(r.WinningTeam(new[] { 300, 550 }, 0, true));
        Assert.AreEqual(0, r.WinningTeam(new[] { 500, 550 }, 0, true));
        Assert.AreEqual(1, r.WinningTeam(new[] { -500, 100 }, 0, false));
    }

    private sealed class ForcedPlayer(int seat, Contract? target, Action<Observation, string>? observe = null) : IPlayer
    {
        public string Name => "forced";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) { observe?.Invoke(v, "bid"); return seat == 0 && legal.Contains(target) ? target : null; }
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) { observe?.Invoke(v, "discard"); return v.Hand.Take(count).ToArray(); }
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) { observe?.Invoke(v, "play"); return legal[0]; }
    }

    [TestMethod]
    public void FullHandsConserveCardsMaskInformationAndReplay()
    {
        foreach (var contract in new[] { Hearts, new Contract(0, Suit.Unknown, ContractKind.Misere), new Contract(0, Suit.Unknown, ContractKind.OpenMisere), new Contract(6, Suit.Unknown) })
        {
            var r = new Rules { MisereRequiresSeven = false, OpenMisereRequiresEight = false };
            for (int seed = 0; seed < 12; seed++)
            {
                void Observe(Observation v, string phase)
                {
                    foreach (var p in v.Players)
                        if (p.ExposedHand != null)
                        {
                            Assert.AreEqual(ContractKind.OpenMisere, contract.Kind);
                            Assert.AreEqual(0, p.Seat); Assert.IsTrue(v.History.Count >= 3);
                        }
                    Assert.AreEqual(phase == "discard" ? 13 : v.Players[v.Seat].CardCount, v.Hand.Count);
                    if (phase == "play")
                    {
                        if (contract.IsMisere) Assert.AreNotEqual(2, v.Seat);
                        Assert.AreEqual(v.Seat == 0 ? 3 : 0, v.OwnDiscards.Count);
                    }
                }
                var bots = Enumerable.Range(0, 4).Select(s => (IPlayer)new ForcedPlayer(s, contract, Observe)).ToArray();
                var game = new Engine(r).Run(bots, seed, 1);
                var round = game.Rounds.Single();
                Assert.AreEqual(10, round.TrickWinners.Count); Assert.AreEqual(10, round.Tricks.Sum());
                Assert.AreEqual(43, round.Hands.SelectMany(h => h).Concat(round.Kitty).Distinct().Count());
                var plays = round.Decisions.Where(d => d.Phase == "play").ToArray();
                Assert.AreEqual(contract.IsMisere ? 30 : 40, plays.Length);
                Assert.AreEqual(plays.Length, plays.Select(p => p.Play!.Card).Distinct().Count());
                ArenaCommand.Replay(game);
            }
        }
    }

    [TestMethod]
    public void PassedOutGamesAreExplicitlyTruncatedAndReplayTamperingFails()
    {
        var bots = Enumerable.Range(0, 4).Select(s => (IPlayer)new ForcedPlayer(s, null)).ToArray();
        var game = new Engine(new()).Run(bots, 7, 3);
        Assert.IsTrue(game.Truncated); Assert.IsNull(game.Winner);
        Assert.AreEqual(3, game.Rounds.Count); Assert.IsTrue(game.Rounds.All(r => r.Redeal));
        ArenaCommand.Replay(game);
        game.Rounds[0].Scores[0] = 500;
        Assert.ThrowsExactly<InvalidDataException>(() => ArenaCommand.Replay(game));
    }

    private sealed class Cheater(string phase) : IPlayer
    {
        public string Name => "cheater";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => phase == "bid" ? new(11, Suit.Hearts) : Hearts;
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => phase == "discard" ? new[] { v.Hand[0], v.Hand[0], v.Hand[0] } : v.Hand.Take(count).ToArray();
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal)
        {
            var bad = new PlayAction(C(Suit.Clubs, 2));
            // Mutating the detached observation and legal list must not authorize a move.
            ((PlayingCard[])v.Hand)[0] = bad.Card;
            ((PlayAction[])legal)[0] = bad;
            return bad;
        }
    }
    [TestMethod]
    [DataRow("bid")][DataRow("discard")][DataRow("play")]
    public void IllegalActionsFailWithoutFallback(string phase)
    {
        IPlayer[] bots = [new Cheater(phase), new ForcedPlayer(1, null), new ForcedPlayer(2, null), new ForcedPlayer(3, null)];
        var error = Assert.ThrowsExactly<IllegalActionException>(() => new Engine(new()).Run(bots, 42, 1));
        Assert.IsNotNull(error.Data["record"]);
    }

    [TestMethod]
    public void ExistingBotCanPlayCompleteDeterministicGames()
    {
        var rules = new Rules();
        for (int seed = 0; seed < 3; seed++)
        {
            IPlayer[] MakePlayers() => Enumerable.Range(0, 4).Select(_ => (IPlayer)new ExistingBotPlayer()).ToArray();
            var a = new Engine(rules).Run(MakePlayers(), seed, 100);
            var b = new Engine(rules).Run(MakePlayers(), seed, 100);
            Assert.IsFalse(a.Truncated); Assert.IsNotNull(a.Winner);
            Assert.AreEqual(JsonSerializer.Serialize(a, ArenaCommand.Json), JsonSerializer.Serialize(b, ArenaCommand.Json));
            ArenaCommand.Replay(a);
        }
    }
    private sealed class AuctionProbe(int seat, List<AuctionAction> calls) : IPlayer
    {
        public string Name => "auction-probe";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal)
        {
            Contract? bid = seat == 0 && v.Auction.Count == 0 ? null
                : seat == 1 && v.Auction.Count == 1 ? new Contract(6, Suit.Spades)
                : seat == 0 && v.Auction.Count > 1 ? new Contract(6, Suit.Clubs) : null;
            calls.Add(new(seat, bid)); return bid;
        }
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => v.Hand.Take(count).ToArray();
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => legal[0];
    }
    [TestMethod]
    public void PassReentryVariantChangesAuctionWithoutChangingDefault()
    {
        foreach (bool permanent in new[] { true, false })
        {
            var calls = new List<AuctionAction>();
            var bots = Enumerable.Range(0, 4).Select(s => (IPlayer)new AuctionProbe(s, calls)).ToArray();
            var game = new Engine(new Rules { PassIsPermanent = permanent }).Run(bots, 8, 1);
            Assert.AreEqual(permanent ? 1 : 2, calls.Count(a => a.Seat == 0));
            Assert.AreEqual(permanent ? 1 : 0, game.Rounds[0].Declarer);
            ArenaCommand.Replay(game);
        }
    }
    [TestMethod]
    public void CommandLinePairedExperimentAndReplayPreserveExistingFiles()
    {
        string path = Path.Combine(Path.GetTempPath(), "arena-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string[] args = ["run", "--pairs", "2", "--seed", "31415", "--a", "random", "--b", "random", "--max-rounds", "20", "--out", path];
            Assert.AreEqual(0, ArenaCommand.Run(args));
            var lines = File.ReadAllLines(Path.Combine(path, "games.jsonl"));
            Assert.AreEqual(4, lines.Length);
            var records = lines.Select(l => JsonSerializer.Deserialize<GameRecord>(l, ArenaCommand.Json)!).ToArray();
            Assert.AreEqual(records[0].Seed, records[1].Seed);
            CollectionAssert.AreEqual(records[0].Rounds[0].Hands[0], records[1].Rounds[0].Hands[0]);
            Assert.AreEqual(0, ArenaCommand.Run(["replay", Path.Combine(path, "games.jsonl")]));
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "summary.json")));
            Assert.AreEqual(4, summary.RootElement.GetProperty("Games").GetInt32());
            Assert.AreEqual(1, ArenaCommand.Run(args));
            CollectionAssert.AreEqual(lines, File.ReadAllLines(Path.Combine(path, "games.jsonl")));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

}
