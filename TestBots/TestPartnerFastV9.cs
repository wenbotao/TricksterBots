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
public class TestPartnerFastV9
{
    private static readonly Lazy<IReadOnlyList<CapturedUxV1>> Fixtures = new(UxEvaluationV1.Build);
    private static CapturedUxV1 Fixture(string id) => Fixtures.Value.Single(c => c.Scenario.Id == id);
    private static PlayingCard C(string card) => UxFixturesV1.Cards(card).Single();
    private static PublicPlay P(int seat, string card) => new(seat, new(C(card)));

    [TestMethod]
    public void OriginalExperienceChecksPassAcrossSeedsAndHandLayouts()
    {
        foreach (var c in Fixtures.Value)
        for (int seed = 0; seed < 8; seed++)
        foreach (bool reversed in new[] { false, true })
        {
            var result = UxEvaluationV1.Evaluate(c, "partner-fast-v9", 4001 + seed * 7919, reversed);
            Assert.IsTrue(result.Legal, c.Scenario.Id + ": " + result.Error);
            Assert.IsTrue(result.Checks.Values.All(x => x), c.Scenario.Id + ": " + UxEvaluationV1.Describe(result.Action, c.Scenario.Phase));
        }
    }

    private static Observation Transform(Observation v, int rotation, bool red, bool black)
    {
        int Seat(int s) => (s + rotation) % 4;
        Suit S(Suit s) => red && s == Suit.Hearts ? Suit.Diamonds : red && s == Suit.Diamonds ? Suit.Hearts
            : black && s == Suit.Clubs ? Suit.Spades : black && s == Suit.Spades ? Suit.Clubs : s;
        PlayingCard Card(PlayingCard c) => c with { Suit = S(c.Suit) };
        Contract? Bid(Contract? c) => c == null ? null : c with { Trump = S(c.Trump) };
        PublicPlay Play(PublicPlay p) => new(Seat(p.Seat), new(Card(p.Action.Card), S(p.Action.JokerLeadSuit)));
        return v with
        {
            Seat = Seat(v.Seat), Dealer = Seat(v.Dealer), Declarer = v.Declarer == null ? null : Seat(v.Declarer.Value), Contract = Bid(v.Contract),
            Hand = v.Hand.Select(Card).ToArray(), OwnDiscards = v.OwnDiscards.Select(Card).ToArray(),
            History = v.History.Select(Play).ToArray(), Trick = v.Trick.Select(Play).ToArray(), Auction = v.Auction.Select(a => new AuctionAction(Seat(a.Seat), Bid(a.Bid))).ToArray(),
            Players = v.Players.Select(p => p with { Seat = Seat(p.Seat), Bids = p.Bids.Select(Bid).ToArray(), Taken = p.Taken.Select(Card).ToArray(), ExposedHand = p.ExposedHand?.Select(Card).ToArray() }).OrderBy(p => p.Seat).ToArray()
        };
    }

    [TestMethod]
    public void PublicMisereProofGeneralizesAcrossAllSeatsAndSuitRenamings()
    {
        var original = Fixture("open-misere-forced-bust").View;
        for (int rotation = 0; rotation < 4; rotation++)
        foreach (bool red in new[] { false, true })
        foreach (bool black in new[] { false, true })
        {
            var v = Transform(original, rotation, red, black); var player = new PartnerFastV9(71);
            var action = player.Play(v, v.Rules.LegalPlays(v.Hand, v.Trick, v.Contract!));
            Assert.AreEqual(new PlayingCard(red ? Suit.Diamonds : Suit.Hearts, 5), action.Card);
            Assert.AreEqual("public-misere-certainty", player.LastReason);
            Assert.AreEqual(0, player.LastSearch!.Rollouts);
        }
    }

    [TestMethod]
    public void UnknownJokerAndExposedDuckBothInvalidateTheImmediateBustProof()
    {
        var spec = Fixture("open-misere-forced-bust").Scenario;
        var jokerHands = spec.ReservedHands.ToDictionary(p => p.Key, p => p.Value.ToArray());
        jokerHands[0] = jokerHands[0].Select(c => c.IsJoker ? C("7S") : c).ToArray();
        jokerHands[2] = UxFixturesV1.Cards("6C HJ");
        var joker = UxCaptureV1.Capture(spec with { Id = "unknown-joker-holdout", ReservedHands = jokerHands });
        Assert.IsNull(PartnerPolicyV9.ImmediateMiserePlay(joker.View, [new(C("5H"))]));
        var duckHands = spec.ReservedHands.ToDictionary(p => p.Key, p => p.Value.ToArray());
        duckHands[1] = duckHands[1].Select(c => c == C("8H") ? C("4H") : c).ToArray();
        var duck = UxCaptureV1.Capture(spec with { Id = "visible-duck-holdout", ReservedHands = duckHands });
        Assert.IsNull(PartnerPolicyV9.ImmediateMiserePlay(duck.View, [new(C("5H"))]));
    }

    [TestMethod]
    public void PartnerPreservationDoesNotAssumeAnUnseenJokerCannotOvertake()
    {
        var spec = Fixture("play-protect-human-winner").Scenario;
        var hands = spec.ReservedHands.ToDictionary(p => p.Key, p => p.Value.ToArray());
        hands[2] = [C("AH")]; hands[1] = [C("HJ")];
        var c = UxCaptureV1.Capture(spec with { Id = "unseen-joker-winner", ReservedHands = hands, Plays = [P(2, "AH"), P(3, "5H")] });
        Assert.IsNull(PartnerPolicyV9.ProtectPartnerWinner(c.View, c.LegalPlays));
        var normal = Fixture("play-protect-human-winner");
        Assert.AreEqual(C("4H"), PartnerPolicyV9.ProtectPartnerWinner(normal.View, normal.LegalPlays)!.Card);
    }

    [TestMethod]
    public void RepeatedPartnerLeadsLeaveRoomForUnblockingInsteadOfForcingPreservation()
    {
        var spec = Fixture("play-protect-human-winner").Scenario;
        var hands = new Dictionary<int, PlayingCard[]>
        {
            [0] = UxFixturesV1.Cards("KH 4H 7H 8H HJ 5C 6C 4D 5D 6D"),
            [2] = UxFixturesV1.Cards("9H TH AH"), [3] = UxFixturesV1.Cards("5H 5S 7C"), [1] = UxFixturesV1.Cards("6H 7S")
        };
        var c = UxCaptureV1.Capture(spec with { Id = "unblocking-holdout", ReservedHands = hands, Kitty = UxFixturesV1.Cards("JH QH AC"),
            Plays = [P(2,"9H"),P(3,"5H"),P(0,"7H"),P(1,"6H"), P(2,"TH"),P(3,"5S"),P(0,"8H"),P(1,"7S"), P(2,"AH"),P(3,"7C")] });
        Assert.IsNull(PartnerPolicyV9.ProtectPartnerWinner(c.View, c.LegalPlays));
    }

    [TestMethod]
    public void ARequestWithOnlyASingletonHoldingDoesNotOverrideFast()
    {
        var spec = Fixture("signal-low-discard").Scenario;
        var hands = new Dictionary<int, PlayingCard[]> { [0] = UxFixturesV1.Cards("AS HJ 7C 5H 6H 5D 4D 6D 7D 8D") };
        var c = UxCaptureV1.Capture(spec with { Id = "singleton-request-holdout", ReservedHands = hands });
        Assert.IsNull(PartnerPolicyV9.PartnerLead(c.View, c.LegalPlays));
        var positive = Fixture("signal-low-discard");
        Assert.AreEqual("return-partner-request", PartnerPolicyV9.PartnerLead(positive.View, positive.LegalPlays)!.Value.Reason);
    }

    [TestMethod]
    public void OwnMisereUnloadsTheHighestSafeCardAndForcedLossRemainsLegal()
    {
        var c = Fixture("misere-own-duck");
        var hand = c.View.Hand.Select(p => p == C("AH") ? C("8H") : p).ToArray();
        var v = c.View with { Hand = hand };
        var move = PartnerPolicyV9.ImmediateMiserePlay(v, v.Rules.LegalPlays(hand, v.Trick, v.Contract!));
        Assert.AreEqual(C("8H"), move!.Card);
        var forced = Fixture("misere-forced-high-control"); var player = new PartnerFastV9(9);
        Assert.AreEqual(C("AH"), player.Play(forced.View, forced.LegalPlays).Card);
        Assert.AreEqual("forced-legal", player.LastReason);
    }

    [TestMethod]
    public void KingGuardRepairDoesNotBuryAnAceToProtectALowCard()
    {
        var c = Fixture("discard-nt-stopper");
        var v = c.View with { Hand = UxFixturesV1.Cards("KS 5S AC AD AH 4D 4H 5C") };
        var original = UxFixturesV1.Cards("5S 4D 4H");
        var fixedCards = PartnerPolicyV9.RepairDiscards(v, original);
        Assert.IsFalse(fixedCards.Any(p => p.Rank == 14 || p.IsJoker));
        Assert.IsFalse(fixedCards.Contains(C("5S")));
        Assert.AreEqual(3, fixedCards.Distinct().Count());
    }

    [TestMethod]
    public void FullMatchesAreLegalReplayableAndSeedReproducible()
    {
        IPlayer[] Make() => [new PartnerFastV9(101), new MediumBotPlayer(), new PartnerFastV9(103), new MediumBotPlayer()];
        var a = new Engine(new()).Run(Make(), 2703001, 100);
        var b = new Engine(new()).Run(Make(), 2703001, 100);
        Assert.IsFalse(a.Truncated);
        Assert.AreEqual(JsonSerializer.Serialize(a, LabJson.Options), JsonSerializer.Serialize(b, LabJson.Options));
        ArenaCommand.Replay(a);
    }

    [TestMethod]
    public void UxRunnerSupportsExplicitProfilesWithoutChangingTheOriginalDefaults()
    {
        Assert.AreEqual(5, UxEvaluationV1.TestedProfiles.Length);
        Assert.AreEqual("partner-fast-v9", Profiles.Create("partner-fast-v9", 1).Name);
        string output = Path.Combine(Path.GetTempPath(), "partner-v9-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            UxEvaluationV1.Run(output, 2, 314159, ["visibility-fast-v8", "partner-fast-v9"]);
            Assert.AreEqual(68, File.ReadAllLines(Path.Combine(output, "trials.jsonl")).Length);
            Assert.ThrowsExactly<ArgumentException>(() => UxEvaluationV1.Run(output + "bad", 2, 1, ["partner-fast-v9", "partner-fast-v9"]));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [TestMethod]
    public void PartnerRevisionHasItsOwnFrozenSourceManifest()
    {
        string root = Path.GetDirectoryName(Path.GetDirectoryName(SourcePath()))!;
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "FiveHundredLab/frozen-profiles-v3.json")))!;
        Assert.AreEqual(2, entries.Count);
        foreach (var item in entries) Assert.AreEqual(item.Value, FrozenSourceHashV1.Compute(Path.Combine(root, item.Key)));
    }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
