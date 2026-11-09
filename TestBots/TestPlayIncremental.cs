#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trickster.cloud;
using Bots = FiveHundredLab.Bots.FiveHundred;
namespace TestBots;
[TestClass]
public class TestPlayIncremental
{
    [TestMethod]
    public void ContinuationActionsUtilitiesAndBranchIsolationMatch()
    {
        int comparisons = 0;
        foreach (var contract in Contracts())
        foreach (var view in Capture(new Rules(), contract).Where((_, i) => i % 9 == 0))
        {
            var oldData = new Bots.PlayDataContext(view);var data = new Bots.IncrementalPlayDataContext(view);
            foreach (var world in BeliefSampler.Sample(view, 2, new SeededRandom(201119001 + view.History.Count)))
            foreach (var action in view.Rules.LegalPlays(view.Hand, view.Trick, view.Contract!).Take(2))
            {
                var expected = new List<PublicPlay>();var actual = new List<PublicPlay>();var aWork = new Bots.PlayRolloutCounter();var bWork = new Bots.PlayRolloutCounter();
                double a = new Bots.CachedPlayRollout(view, world, oldData, aWork, expected.Add).Play(action);
                double b = new Bots.IncrementalPlayRollout(view, world, data, bWork, actual.Add).Play(action);
                Assert.AreEqual(a, b);CollectionAssert.AreEqual(expected, actual);
                Assert.AreEqual(aWork.ForcedMoves, bWork.ForcedMoves);Assert.AreEqual(aWork.PolicyMoves, bWork.PolicyMoves);
                Assert.AreEqual(b, new Bots.IncrementalPlayRollout(view, world, data, new Bots.PlayRolloutCounter()).Play(action));comparisons++;
            }
        }
        Assert.IsTrue(comparisons > 50);
    }
    [TestMethod]
    public void EveryPrefixSummaryMatchesFrozenRebuildAndForkIsIndependent()
    {
        var method = typeof(Bots.PlayDataContext).GetMethod("Players", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var contract in Contracts())
        {
            var root = Capture(new Rules(), contract).Last();var data = new Bots.PlayDataContext(root);
            var sequence = root.History.ToArray();var history = new Bots.IncrementalPlayHistory(root.Rules, contract, [], c => c.ToBotCard().ToString());
            for (int count = 0; count <= sequence.Length; count++)
            {
                int current = count % (contract.IsMisere ? 3 : 4);
                var prefix = sequence.Take(count).ToArray();var view = root with { History = prefix, Trick = prefix.Skip(count - current).ToArray() };
                var players = (PlayerBase[])method.Invoke(data, [view])!;
                string expected = JsonSerializer.Serialize(players.Select(p => new { p.VoidSuits, p.PlayedCards }), ArenaCommand.Json);
                history.Populate(players);
                Assert.AreEqual(expected, JsonSerializer.Serialize(players.Select(p => new { p.VoidSuits, p.PlayedCards }), ArenaCommand.Json));
                Assert.AreEqual(string.Concat(prefix.Select(p => p.Action.Card.ToBotCard().ToString())), history.OrderedCards);
                Assert.AreEqual(JsonSerializer.Serialize(prefix.Take(count - current).Select(p => p.Action.Card.ToBotCard()).ToArray(), ArenaCommand.Json), JsonSerializer.Serialize(history.CompletedCards(), ArenaCommand.Json));
                if (count < sequence.Length)
                {
                    var fork = history.Fork();fork.Append(sequence[count]);Assert.AreEqual(count, history.Count);Assert.AreEqual(count + 1, fork.Count);
                    history.Populate(players);
                    Assert.AreEqual(expected, JsonSerializer.Serialize(players.Select(p => new { p.VoidSuits, p.PlayedCards }), ArenaCommand.Json));
                    // Returned mutable lists must not modify the stored summary.
                    foreach (var p in players) { p.VoidSuits.Clear();p.PlayedCards.Clear(); }
                    history.Populate(players);
                    Assert.AreEqual(expected, JsonSerializer.Serialize(players.Select(p => new { p.VoidSuits, p.PlayedCards }), ArenaCommand.Json));
                    history.Append(sequence[count]);
                }
            }
        }
    }
    [TestMethod]
    public void RootActionsSearchAndDiagnosticsMatch()
    {
        var reference = new Bots.StrongPlayCachedBot(71);var subject = new Bots.StrongPlayIncrementalBot(71);
        foreach (var f in UxEvaluationV1.Build())
        {
            switch (f.Scenario.Phase)
            {
                case "bid": Assert.AreEqual(reference.Bid(f.View, f.LegalBids), subject.Bid(f.View, f.LegalBids));Assert.IsNull(subject.LastDataWork);break;
                case "discard": CollectionAssert.AreEqual(reference.Discard(f.View, f.DiscardCount).ToArray(), subject.Discard(f.View, f.DiscardCount).ToArray());Assert.IsNull(subject.LastDataWork);break;
                case "play":
                    Assert.AreEqual(reference.Play(f.View, f.LegalPlays), subject.Play(f.View, f.LegalPlays));
                    if (subject.LastDataWork is { } data)
                    {
                        Assert.AreEqual((long)subject.LastRollouts, data.HistoryForks);
                        Assert.AreEqual(data.HistoryForks + subject.LastPlayWork!.ForcedMoves + subject.LastPlayWork.PolicyMoves, data.HistoryAppends);
                    }
                    break;
            }
            Assert.AreEqual(reference.LastDefaultSearch, subject.LastDefaultSearch);Assert.AreEqual(reference.LastRollouts, subject.LastRollouts);
        }
        Assert.IsInstanceOfType<Bots.StrongPlayIncrementalBot>(Profiles.Create(Bots.StrongPlayIncrementalBot.Profile, 7));
    }
    [TestMethod]
    public void DefaultAndConfiguredSmokeGamesMatchAndReplay()
    {
        foreach (var path in new string?[] { null, Path.Combine(Root(), "FiveHundredRules/presets/us-single-round-4p-v1.json") })
        {
            var rules = RuleConfiguration.Load(path);
            GameRecord Run(IPlayer subject)
            {
                var game = new Engine(rules).Run([new Label(subject), new Bots.MediumBot(8, rules), new Bots.FastBot(9, rules), new Bots.StrongBot(10, rules)], 101119001, 1);
                ArenaCommand.Replay(game);return game;
            }
            var a = Run(new Bots.StrongPlayCachedBot(7, rules));var b = Run(Bots.StrongPlayIncrementalBot.FromConfiguration(path, 7));
            Assert.AreEqual(JsonSerializer.Serialize(a, ArenaCommand.Json), JsonSerializer.Serialize(b, ArenaCommand.Json));
        }
    }
    private static Contract[] Contracts() => [new(6, Suit.Unknown), new(6, Suit.Hearts), new(0, Suit.Unknown, ContractKind.Misere), new(0, Suit.Unknown, ContractKind.OpenMisere)];
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
