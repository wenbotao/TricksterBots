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
using Bots = FiveHundredLab.Bots.FiveHundred;
namespace TestBots;
[TestClass]
public class TestPlayForced
{
    [TestMethod]
    public void DefaultContinuationActionsAndUtilitiesMatchIncludingMisere()
    {
        long forced = 0;int comparisons = 0;
        foreach (var contract in new[] { new Contract(6, Suit.Unknown), new Contract(6, Suit.Hearts), new Contract(0, Suit.Unknown, ContractKind.Misere), new Contract(0, Suit.Unknown, ContractKind.OpenMisere) })
        foreach (var view in Capture(new Rules(), contract).Where((_, i) => i % 7 == 0))
        foreach (var world in BeliefSampler.Sample(view, 2, new SeededRandom(171119001 + view.History.Count)))
        foreach (var action in view.Rules.LegalPlays(view.Hand, view.Trick, view.Contract!).Take(3))
        {
            var expected = new List<PublicPlay>();var actual = new List<PublicPlay>();
            var reference = new Bots.DefaultRollout(view, world, () => new Recording(expected));
            var work = new Bots.PlayRolloutCounter();
            double a = reference.Play(action), b = new Bots.ForcedPlayRollout(view, world, () => new Bots.MediumBot.DefaultAdapter(), work, actual.Add).Play(action);
            Assert.AreEqual(a, b);CollectionAssert.AreEqual(expected, actual);forced += work.ForcedMoves;comparisons++;
            Assert.AreEqual(actual.Count, work.ForcedMoves + work.PolicyMoves);
        }
        Assert.IsTrue(forced > 0);Assert.IsTrue(comparisons > 100);
    }
    [TestMethod]
    public void SingleJokerWithFourLeadDeclarationsStillCallsPolicy()
    {
        var rules = new Rules();var contract = new Contract(6, Suit.Unknown);
        PlayingCard C(string text) => UxFixturesV1.Cards(text).Single();
        var hands = new[] { Array.Empty<PlayingCard>(), UxFixturesV1.Cards("5C"), UxFixturesV1.Cards("4D"), UxFixturesV1.Cards("6S 4H") };
        // Use the actual rule-deck Joker, avoiding assumptions about fixture encodings.
        hands[0] = [rules.Deck().Single(c => c.IsJoker)];
        var trick = new PublicPlay[] { new(0, new(C("AS"))), new(1, new(C("7S"))), new(2, new(C("5S"))) };
        var unused = rules.Deck().Except(hands.SelectMany(h => h)).Except(trick.Select(p => p.Action.Card)).ToArray();
        var dead = unused.Take(3).ToArray();var history = unused.Skip(3).Select((c, i) => new PublicPlay(i % 4, new(c))).ToList();
        Assert.AreEqual(32, history.Count);
        var tricks = new int[4];var taken = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
        for (int i = 0; i < 32; i += 4) { var cards = history.Skip(i).Take(4).ToArray();int w = rules.Winner(cards, contract);tricks[w]++;taken[w].AddRange(cards.Select(p => p.Action.Card)); }
        history.AddRange(trick);
        var players = Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, hands[s].Length, 0, [s == 3 ? contract : null], tricks[s], taken[s], null)).ToArray();
        var view = new Observation(3, 3, 0, hands[3], players, [new(3, contract)], contract, 3, trick, history, [0, 0], rules, dead);
        var world = new SampledDeal(hands, dead, 1);var expected = new List<PublicPlay>();var actual = new List<PublicPlay>();var work = new Bots.PlayRolloutCounter();
        double a = new Bots.DefaultRollout(view, world, () => new Recording(expected)).Play(new(C("6S")));
        double b = new Bots.ForcedPlayRollout(view, world, () => new Bots.MediumBot.DefaultAdapter(), work, actual.Add).Play(new(C("6S")));
        Assert.AreEqual(a, b);CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(0, actual[0].Seat);Assert.IsTrue(actual[0].Action.Card.IsJoker);Assert.AreNotEqual(Suit.Unknown, actual[0].Action.JokerLeadSuit);
        Assert.IsTrue(work.PolicyMoves > 0);Assert.AreEqual(4, rules.LegalPlays(hands[0], [], contract).Count);
    }
    [TestMethod]
    public void ConfiguredContinuationUtilitiesMatchForEveryConfiguration()
    {
        long forced = 0;
        foreach (var path in Configurations())
        {
            var rules = (ConfiguredRules)RuleConfiguration.Load(path);
            foreach (var view in Capture(rules, null).Where((_, i) => i % 9 == 0))
            foreach (var world in RuleSamples.Sample(view, (ConfiguredRules)view.Rules, 2, new SeededRandom(181119001 + view.History.Count)))
            foreach (var action in view.Rules.LegalPlays(view.Hand, view.Trick, view.Contract!).Take(2))
            {
                var work = new Bots.PlayRolloutCounter();
                double a = Bots.ConfiguredRollout.Play(view, (ConfiguredRules)view.Rules, world, action);
                double b = Bots.ForcedConfiguredPlayRollout.Play(view, (ConfiguredRules)view.Rules, world, action, work);
                Assert.AreEqual(a, b, path);forced += work.ForcedMoves;
            }
        }
        Assert.IsTrue(forced > 0);
    }
    [TestMethod]
    public void SequentialRootActionsReportsAndRandomStreamRemainIdentical()
    {
        var reference = new Bots.StrongDiscardLeanBot(71);var subject = new Bots.StrongPlayForcedBot(71);
        foreach (var f in UxEvaluationV1.Build())
        {
            switch (f.Scenario.Phase)
            {
                case "bid": Assert.AreEqual(reference.Bid(f.View, f.LegalBids), subject.Bid(f.View, f.LegalBids));Assert.IsNull(subject.LastPlayWork);break;
                case "discard": CollectionAssert.AreEqual(reference.Discard(f.View, f.DiscardCount).ToArray(), subject.Discard(f.View, f.DiscardCount).ToArray());Assert.IsNull(subject.LastPlayWork);break;
                case "play": Assert.AreEqual(reference.Play(f.View, f.LegalPlays), subject.Play(f.View, f.LegalPlays));Assert.AreEqual(subject.LastRollouts, subject.LastPlayWork!.Evaluations);break;
            }
            Assert.AreEqual(reference.LastDefaultSearch, subject.LastDefaultSearch);Assert.AreEqual(reference.LastRollouts, subject.LastRollouts);
        }
        Assert.IsInstanceOfType<Bots.StrongPlayForcedBot>(Profiles.Create(Bots.StrongPlayForcedBot.Profile, 7));
    }
    [TestMethod]
    public void DefaultAndConfiguredGameRecordsAreIdenticalAndReplay()
    {
        foreach (var path in new string?[] { null }.Concat(Configurations()))
        {
            var rules = RuleConfiguration.Load(path);
            GameRecord Run(IPlayer subject)
            {
                var game = new Engine(rules).Run([new Label(subject), new Bots.MediumBot(8, rules), new Bots.FastBot(9, rules), new Bots.StrongBot(10, rules)], 101119001, 1);
                ArenaCommand.Replay(game);return game;
            }
            var a = Run(new Bots.StrongDiscardLeanBot(7, rules));var b = Run(Bots.StrongPlayForcedBot.FromConfiguration(path, 7));
            Assert.AreEqual(JsonSerializer.Serialize(a, ArenaCommand.Json), JsonSerializer.Serialize(b, ArenaCommand.Json), path ?? "default");
        }
    }
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    private static IEnumerable<string> Configurations() => new[] { "presets", "custom", "examples" }.SelectMany(d => Directory.GetFiles(Path.Combine(Root(), "FiveHundredRules", d), "*.json"));
    private static List<Observation> Capture(Rules rules, Contract? contract)
    {
        var views = new List<Observation>();var bots = Enumerable.Range(0, 4).Select(s => (IPlayer)new CapturePlayer(s, rules, contract, views)).ToArray();
        var game = new Engine(rules).Run(bots, 161119001, 1);ArenaCommand.Replay(game);return views;
    }
    private sealed class CapturePlayer(int seat, Rules rules, Contract? forced, List<Observation> views) : IPlayer
    {
        private readonly Bots.MediumBot medium = new(17 + seat, rules);
        public string Name => "capture";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal)
        {
            if (forced == null) return medium.Bid(v, legal);
            Contract? action = v.Auction.Count == 0 ? forced.IsMisere ? new(forced.Kind == ContractKind.OpenMisere ? 8 : 7, Suit.Spades) : forced : v.Auction.Count == 1 && forced.IsMisere ? forced : null;
            if (!legal.Contains(action)) throw new InvalidOperationException("Forced contract unavailable");return action;
        }
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => medium.Discard(v, count);
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) { views.Add(v);return medium.Play(v, legal); }
    }
    private sealed class Recording(List<PublicPlay> trace) : IPlayer
    {
        private readonly Bots.MediumBot.DefaultAdapter medium = new();public string Name => "record";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => medium.Bid(v, legal);
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => medium.Discard(v, count);
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) { var action = medium.Play(v, legal);trace.Add(new(v.Seat, action));return action; }
    }
    private sealed class Label(IPlayer inner) : IPlayer, IJokerDeclarationPlayer
    {
        public string Name => "subject";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => inner.Bid(v, legal);
        public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => inner.Discard(v, count);
        public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => inner.Play(v, legal);
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v) => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(v) : [];
    }
}
