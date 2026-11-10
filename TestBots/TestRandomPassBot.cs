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
public class TestRandomPassBot
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    [TestMethod]
    public void AlwaysPassesAndRetainsRandomNonBiddingSequence()
    {
        foreach (Rules rules in new[] { new Rules(), RuleConfiguration.Load(Path.Combine(Root(), "FiveHundredRules/presets/au-reference-4p-v1.json")) })
        {
            var fixture = UxEvaluationV1.Build().First(f => f.Scenario.Phase == "play");
            var view = fixture.View with { Rules = rules };
            var bot = new Bots.RandomPassBot(71, rules);
            var random = new Bots.RandomBot(71, rules);
            for (int i = 0; i < 10; i++)
                Assert.IsNull(bot.Bid(view, [null, new(6, Trickster.cloud.Suit.Hearts)]));
            Assert.AreEqual(random.Play(view, fixture.LegalPlays), bot.Play(view, fixture.LegalPlays));
            CollectionAssert.AreEqual(random.Discard(view, 3).ToArray(), bot.Discard(view, 3).ToArray());
            CollectionAssert.AreEqual(random.DeclareJokers(view).ToArray(), bot.DeclareJokers(view).ToArray());
            Assert.Throws<InvalidOperationException>(() => bot.Bid(view, [new(6, Trickster.cloud.Suit.Hearts)]));
        }
    }

    [TestMethod]
    public void AllPassRedealsAndMixedGamesReplayAcrossAllConfigurations()
    {
        foreach (var file in new string?[] { null }.Concat(new[] { "presets", "custom", "examples" }.SelectMany(dir => Directory.GetFiles(Path.Combine(Root(), "FiveHundredRules", dir), "*.json"))))
        {
            var rules = RuleConfiguration.Load(file);
            var allPass = new Engine(rules).Run(Enumerable.Range(0, 4).Select(s => Profiles.CreateForRules("random-pass-v1", s, rules)).ToArray(), 27191, 2);
            Assert.IsTrue(allPass.Truncated);
            Assert.IsNull(allPass.Winner);
            Assert.IsTrue(allPass.Rounds.All(r => r.Redeal));
            var mixed = new Engine(rules).Run([new Bots.MediumBot(1, rules), new Bots.RandomPassBot(2, rules), new Bots.MediumBot(3, rules), new Bots.RandomPassBot(4, rules)], 28191, 3);
            Assert.IsTrue(mixed.Rounds.SelectMany(r => r.Decisions).Where(d => d.Phase == "bid" && d.Seat % 2 == 1).All(d => d.Bid == null));
            ArenaCommand.Replay(JsonSerializer.Deserialize<GameRecord>(JsonSerializer.Serialize(mixed, ArenaCommand.Json), ArenaCommand.Json)!);
        }
        Assert.IsInstanceOfType<Bots.RandomPassBot>(Profiles.CreateForRules("random-pass-v1", 1, new Rules()));
    }

    [TestMethod]
    public void MismatchingRulesAreRejected()
    {
        var rules = RuleConfiguration.Load(Path.Combine(Root(), "FiveHundredRules/presets/au-reference-4p-v1.json"));
        var view = UxEvaluationV1.Build().First().View;
        Assert.Throws<ArgumentException>(() => new Bots.RandomPassBot(1, rules).Bid(view, [null]));
    }
}
