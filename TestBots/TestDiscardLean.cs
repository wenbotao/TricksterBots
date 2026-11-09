#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bots = FiveHundredLab.Bots.FiveHundred;
namespace TestBots;
[TestClass]
public class TestDiscardLean
{
    [TestMethod]
    public void SequentialDecisionsAndSearchReportsMatchSelectedBaseline()
    {
        var reference = new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientBaseline, 71);
        var lean = new Bots.StrongDiscardLeanBot(71);
        foreach (var fixture in UxEvaluationV1.Build())
        {
            switch (fixture.Scenario.Phase)
            {
                case "bid": Assert.AreEqual(reference.Bid(fixture.View, fixture.LegalBids), lean.Bid(fixture.View, fixture.LegalBids)); break;
                case "discard":
                    CollectionAssert.AreEqual(reference.Discard(fixture.View, fixture.DiscardCount).ToArray(), lean.Discard(fixture.View, fixture.DiscardCount).ToArray());
                    Assert.AreEqual(JsonSerializer.Serialize(reference.LastDiscardGuard), JsonSerializer.Serialize(lean.LastDiscardGuard));
                    Assert.AreEqual(0, lean.LastWork!.FeatureEvaluations);
                    break;
                case "play": Assert.AreEqual(reference.Play(fixture.View, fixture.LegalPlays), lean.Play(fixture.View, fixture.LegalPlays)); break;
            }
            Assert.AreEqual(reference.LastDefaultSearch, lean.LastDefaultSearch);
            Assert.AreEqual(reference.LastRollouts, lean.LastRollouts);
        }
    }
    [TestMethod]
    public void IsolatedRepairDoesNotAdvanceSearchAndDiagnosticsAreCleared()
    {
        var fixtures = UxEvaluationV1.Build();var f = fixtures.Single(x => x.Scenario.Id == "discard-nt-stopper");
        var lean = new Bots.StrongDiscardLeanBot(71);
        lean.RefineProposedDiscard(f.View, UxFixturesV1.Cards("5S 4D 4H"));
        Assert.IsTrue(lean.LastDiscardGuard!.Changed);Assert.AreEqual(0, lean.LastWork!.FeatureEvaluations);
        Assert.Throws<ArgumentException>(() => lean.RefineProposedDiscard(f.View, UxFixturesV1.Cards("5S 5S 4H")));
        var bid = fixtures.First(x => x.Scenario.Phase == "bid");
        Assert.AreEqual(new Bots.StrongDiscardLeanBot(71).Bid(bid.View, bid.LegalBids), lean.Bid(bid.View, bid.LegalBids));
        Assert.IsNull(lean.LastDiscardGuard);Assert.IsNull(lean.LastWork);Assert.IsNull(lean.LastAudit);
        var play = fixtures.First(x => x.Scenario.Phase == "play");lean.Play(play.View, play.LegalPlays.Take(1).ToArray());
        Assert.IsNull(lean.LastDefaultSearch);Assert.AreEqual(0, lean.LastRollouts);
    }
    [TestMethod]
    public void DefaultAndEveryRuleConfigurationHaveIdenticalPlayedAndReplayedGames()
    {
        string root = Path.GetDirectoryName(Path.GetDirectoryName(Source()))!;
        foreach (var path in new string?[] { null }.Concat(new[] { "presets", "custom", "examples" }.SelectMany(d => Directory.GetFiles(Path.Combine(root, "FiveHundredRules", d), "*.json"))))
        {
            var rules = RuleConfiguration.Load(path);
            GameRecord Run(IPlayer subject)
            {
                var game = new Engine(rules).Run([new Label(subject), new Bots.MediumBot(8, rules), new Bots.FastBot(9, rules), new Bots.StrongBot(10, rules)], 101119001, 1);
                ArenaCommand.Replay(game);return game;
            }
            var old = Run(new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientBaseline, 7, rules));
            var lean = Run(Profiles.CreateForRules(Bots.StrongDiscardLeanBot.Profile, 7, rules));
            Assert.AreEqual(JsonSerializer.Serialize(old, ArenaCommand.Json), JsonSerializer.Serialize(lean, ArenaCommand.Json), path ?? "default");
        }
        Assert.IsInstanceOfType<Bots.StrongDiscardLeanBot>(Profiles.Create(Bots.StrongDiscardLeanBot.Profile, 7));
        Assert.IsInstanceOfType<Bots.StrongDiscardLeanBot>(Bots.StrongDiscardLeanBot.FromConfiguration(seed: 7));
    }
    private static string Source([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    private sealed class Label(IPlayer inner) : IPlayer, IJokerDeclarationPlayer
    {
        public string Name => "subject";
        public Contract? Bid(Observation v, System.Collections.Generic.IReadOnlyList<Contract?> legal) => inner.Bid(v, legal);
        public System.Collections.Generic.IReadOnlyList<PlayingCard> Discard(Observation v, int count) => inner.Discard(v, count);
        public PlayAction Play(Observation v, System.Collections.Generic.IReadOnlyList<PlayAction> legal) => inner.Play(v, legal);
        public System.Collections.Generic.IReadOnlyList<PlayAction> DeclareJokers(Observation v) => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(v) : [];
    }
}
