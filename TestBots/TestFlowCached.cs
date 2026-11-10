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
public class TestFlowCached
{
    [TestMethod]
    public void EveryPlayPrefixAndJokerOptionMatchesUncachedMedium()
    {
        int count = 0;
        foreach (var rules in new[] { new Rules(), new Rules { PassIsPermanent = false, MiserePoints = 230, OpenMiserePoints = 520 } })
        foreach (var contract in Contracts())
        {
            var views = Capture(rules, contract);
            var cache = new Bots.FlowPolicyCache(views[0]);
            foreach (var view in views)
            {
                var legal = rules.LegalPlays(view.Hand, view.Trick, contract);
                Assert.AreEqual(new Bots.MediumBot.DefaultAdapter().Play(view, legal), cache.Play(view, legal));
                count++;
            }
            Assert.IsTrue(cache.StrategyInstances <= 5);
        }
        Assert.IsTrue(count >= 250);
    }
    [TestMethod]
    public void AllPhaseContinuationTracesAndUtilitiesMatchAndBranchesRemainIsolated()
    {
        int count = 0;
        foreach (var contract in Contracts())
        foreach (var view in Capture(new Rules(), contract).Where((_, i) => i % 9 == 0))
        {
            var cache = new Bots.FlowPolicyCache(view);
            foreach (var world in BeliefSampler.Sample(view, 2, new SeededRandom(411119001 + view.History.Count)))
            foreach (var action in view.Rules.LegalPlays(view.Hand, view.Trick, contract).Take(2))
            {
                var expected = new List<PublicPlay>();var actual = new List<PublicPlay>();
                double a = new Bots.DefaultRollout(view, world, () => new Recording(expected)).Play(action);
                double b = new Bots.FlowCachedRollout(view, world, cache, new Bots.PlayRolloutCounter(), actual.Add).Play(action);
                Assert.AreEqual(a, b);CollectionAssert.AreEqual(expected, actual);
                Assert.AreEqual(b, new Bots.FlowCachedRollout(view, world, cache, new Bots.PlayRolloutCounter()).Play(action));
                count++;
            }
        }
        foreach (var fixture in UxEvaluationV1.Build().Where(f => f.Scenario.Phase is "bid" or "discard"))
        {
            var v = fixture.View;var cache = new Bots.FlowPolicyCache(v);
            var world = BeliefSampler.Sample(v, 1, new SeededRandom(411119111)).Single();
            var expected = new List<PublicPlay>();var actual = new List<PublicPlay>();
            var a = new Bots.DefaultRollout(v, world, () => new Recording(expected));
            var b = new Bots.FlowCachedRollout(v, world, cache, new Bots.PlayRolloutCounter(), actual.Add);
            double x, y;
            if (fixture.Scenario.Phase == "bid")
            { var action = fixture.LegalBids.First(c => c != null);x = a.Bid(action);y = b.Bid(action); }
            else
            { var action = new Bots.MediumBot.DefaultAdapter().Discard(v, fixture.DiscardCount);x = a.Discard(action);y = b.Discard(action); }
            Assert.AreEqual(x, y);CollectionAssert.AreEqual(expected, actual);count++;
        }
        Assert.IsTrue(count > 50);
    }
    [TestMethod]
    public void RootSearchActionsBudgetsAndDiagnosticsMatchAcrossPhases()
    {
        var reference = new Bots.StrongPlayIncrementalBot(71);var subject = new Bots.StrongFlowCachedBot(71);
        foreach (var f in UxEvaluationV1.Build())
        {
            switch (f.Scenario.Phase)
            {
                case "bid": Assert.AreEqual(reference.Bid(f.View, f.LegalBids), subject.Bid(f.View, f.LegalBids));break;
                case "discard": CollectionAssert.AreEqual(reference.Discard(f.View, f.DiscardCount).ToArray(), subject.Discard(f.View, f.DiscardCount).ToArray());Assert.AreEqual(JsonSerializer.Serialize(reference.LastDiscardGuard, ArenaCommand.Json), JsonSerializer.Serialize(subject.LastDiscardGuard, ArenaCommand.Json));break;
                case "play": Assert.AreEqual(reference.Play(f.View, f.LegalPlays), subject.Play(f.View, f.LegalPlays));Assert.AreEqual(JsonSerializer.Serialize(reference.LastTactics, ArenaCommand.Json), JsonSerializer.Serialize(subject.LastTactics, ArenaCommand.Json));break;
            }
            Assert.AreEqual(reference.LastDefaultSearch, subject.LastDefaultSearch);Assert.AreEqual(reference.LastRollouts, subject.LastRollouts);
        }
        Assert.IsInstanceOfType<Bots.StrongFlowCachedBot>(Profiles.Create(Bots.StrongFlowCachedBot.Profile, 7));
    }
    [TestMethod]
    public void FullEngineGamesDefaultAndConfiguredMatchAndReplay()
    {
        foreach (var path in new string?[] { null, "FiveHundredRules/presets/us-single-round-4p-v1.json", "FiveHundredRules/presets/au-reference-4p-v1.json" })
        {
            string? fullPath = path == null ? null : Path.Combine(Root(), path);
            var rules = RuleConfiguration.Load(fullPath);
            GameRecord Run(IPlayer subject)
            {
                var game = new Engine(rules).Run([new Label(subject), new Bots.MediumBot(8, rules), new Bots.FastBot(9, rules), new Bots.StrongBot(10, rules)], 411119001, 2);
                ArenaCommand.Replay(game);return game;
            }
            var a = Run(new Bots.StrongPlayIncrementalBot(7, rules));var b = Run(Bots.StrongFlowCachedBot.FromConfiguration(fullPath, 7));
            Assert.AreEqual(JsonSerializer.Serialize(a, ArenaCommand.Json), JsonSerializer.Serialize(b, ArenaCommand.Json));
        }
    }
    private static Contract[] Contracts() => [new(6, Suit.Unknown), new(6, Suit.Hearts), new(0, Suit.Unknown, ContractKind.Misere), new(0, Suit.Unknown, ContractKind.OpenMisere)];
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    private static List<Observation> Capture(Rules rules, Contract contract)
    {
        var views = new List<Observation>();
        var game = new Engine(rules).Run(Enumerable.Range(0, 4).Select(s => (IPlayer)new CapturePlayer(s, rules, contract, views)).ToArray(), 161119001, 1);
        ArenaCommand.Replay(game);return views;
    }
    private sealed class CapturePlayer(int seat, Rules rules, Contract forced, List<Observation> views) : IPlayer
    {
        private readonly Bots.MediumBot medium = new(17 + seat, rules);public string Name => "capture";
        public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal)
        {
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
