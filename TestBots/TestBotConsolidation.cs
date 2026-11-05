#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bots = FiveHundredLab.Bots.FiveHundred;

namespace TestBots;

[TestClass]
public class TestBotConsolidation
{
    private static readonly RuleBotLevel[] Levels = Enum.GetValues<RuleBotLevel>();

    private static IPlayer Original(RuleBotLevel level, int seed) => level switch
    {
        RuleBotLevel.Random => new RandomLegalBot(seed),
        RuleBotLevel.Weak => new WeakBot(),
        RuleBotLevel.Medium => new MediumBotPlayer(),
        RuleBotLevel.Fast => new VisibilityFastV8(seed),
        RuleBotLevel.Strong => new StrongBot(seed),
        RuleBotLevel.Partner => new PartnerFastV9(seed),
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
    private static Bots.FiveHundredRuleBot Consolidated(RuleBotLevel level, int seed, Rules rules) => level switch
    {
        RuleBotLevel.Random => new Bots.RandomBot(seed, rules),
        RuleBotLevel.Weak => new Bots.WeakBot(seed, rules),
        RuleBotLevel.Medium => new Bots.MediumBot(seed, rules),
        RuleBotLevel.Fast => new Bots.FastBot(seed, rules),
        RuleBotLevel.Strong => new Bots.StrongBot(seed, rules),
        RuleBotLevel.Partner => new Bots.PartnerBot(seed, rules),
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    [TestMethod]
    public void BothPathsMatchFrozenPoliciesOnAllExperienceFixtures()
    {
        string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(SourcePath()))!;
        foreach (var configured in new[] { false, true })
        foreach (var fixture in UxEvaluationV1.Build())
        foreach (var level in Levels)
        {
            Rules rules = configured ? RuleConfiguration.Load(System.IO.Path.Combine(root, "FiveHundredRules/presets/legacy-au-4p-v1.json")) : fixture.View.Rules;
            var view = fixture.View with { Rules = rules };
            var before = new RuleAwarePlayer(level, 119, Original(level, 119), rules);
            var after = Consolidated(level, 119, rules);
            string Action(IPlayer bot) => fixture.Scenario.Phase switch
            {
                "bid" => JsonSerializer.Serialize(bot.Bid(view, fixture.LegalBids), ArenaCommand.Json),
                "discard" => JsonSerializer.Serialize(bot.Discard(view, fixture.DiscardCount), ArenaCommand.Json),
                _ => JsonSerializer.Serialize(bot.Play(view, fixture.LegalPlays), ArenaCommand.Json)
            };
            Assert.AreEqual(Action(before), Action(after), $"{configured}/{fixture.Scenario.Id}/{level}");
            Assert.AreEqual(before.LastRollouts, after.LastRollouts);
        }
    }

    [TestMethod]
    public void DefaultCompleteDealsMatchAcrossNormalAndZeroContracts()
    {
        foreach (var level in Levels)
        foreach (var kind in new[] { ContractKind.Normal, ContractKind.Misere, ContractKind.OpenMisere })
        {
            var rules = new Rules { MisereRequiresSeven = false, OpenMisereRequiresEight = false };
            var before = new Engine(rules).Run(Seats(s => Original(level, 71 + s), kind), 19091, 1, 3);
            var after = new Engine(rules).Run(Seats(s => Consolidated(level, 71 + s, rules), kind), 19091, 1, 3);
            Assert.AreEqual(10, after.Rounds.Single().TrickWinners.Count);
            Assert.AreEqual(JsonSerializer.Serialize(before.Rounds, ArenaCommand.Json), JsonSerializer.Serialize(after.Rounds, ArenaCommand.Json), $"{level}/{kind}");
        }
    }

    [TestMethod]
    public void DefaultRandomActionsDoNotConsumeTheSeparateDeclarationStream()
    {
        var fixture = UxEvaluationV1.Build().First(f => f.View.Hand.Any(c => c.IsJoker));
        var view = fixture.View;
        var before = new RuleAwarePlayer(RuleBotLevel.Random, 71, new RandomLegalBot(71), view.Rules);
        var after = new Bots.RandomBot(71, view.Rules);
        Contract?[] bids = [null, new(6, Trickster.cloud.Suit.Hearts)];
        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(before.Bid(view, bids), after.Bid(view, bids));
            Assert.AreEqual(JsonSerializer.Serialize(before.DeclareJokers(view), ArenaCommand.Json), JsonSerializer.Serialize(after.DeclareJokers(view), ArenaCommand.Json));
        }
    }

    [TestMethod]
    public void MigratedMediumStrategyInheritsOnlyPlatformBase()
    {
        var strategy = typeof(Bots.MediumBot).GetNestedType("DefaultStrategy", System.Reflection.BindingFlags.NonPublic)!;
        Assert.IsNotNull(strategy);
        Assert.AreEqual(typeof(Trickster.Bots.BaseBot<Trickster.cloud.FiveHundredOptions>), strategy.BaseType);
        Assert.IsFalse(typeof(Trickster.Bots.FiveHundredBot).IsAssignableFrom(strategy));
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    private static IPlayer[] Seats(Func<int, IPlayer> factory, ContractKind kind) => Enumerable.Range(0, 4).Select(s => (IPlayer)new FixedAuction(factory(s), s == 0, kind)).ToArray();
    private sealed class FixedAuction(IPlayer bot, bool declarer, ContractKind kind) : IPlayer, IJokerDeclarationPlayer
    {
        public string Name => "consolidation-context";
        public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
        {
            var proposed = bot.Bid(view, legal);
            Assert.IsTrue(legal.Contains(proposed));
            return declarer ? legal.First(c => c?.Kind == kind) : null;
        }
        public IReadOnlyList<PlayingCard> Discard(Observation view, int count) => bot.Discard(view, count);
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal) => bot.Play(view, legal);
        public IReadOnlyList<PlayAction> DeclareJokers(Observation view) => bot is IJokerDeclarationPlayer naming ? naming.DeclareJokers(view) : Array.Empty<PlayAction>();
    }
}
