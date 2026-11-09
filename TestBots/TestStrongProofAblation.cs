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
public class TestStrongProofAblation
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    [TestMethod]
    public void FixedProfilesGateOnlyTheirOwnContractType()
    {
        foreach (var f in UxEvaluationV1.Build().Where(x => x.Scenario.Phase == "play"))
        {
            var full = Bots.StrongTacticalBot.ApplyPublicTactics(f.View, f.LegalPlays);
            foreach (var profile in Enum.GetValues<Bots.StrongProofProfile>())
            {
                var actual = Bots.StrongProofAblationBot.ApplyPublicTactics(f.View, f.LegalPlays, profile);
                bool enabled = f.View.Contract!.IsMisere == (profile == Bots.StrongProofProfile.MisereOnly);
                CollectionAssert.AreEqual(enabled ? full.Choices : f.LegalPlays.ToArray(), actual.Choices);
                Assert.AreEqual(enabled ? full.Reason : null, actual.Reason);
                Assert.AreEqual(enabled ? full.Nodes : 0, actual.Nodes);
            }
        }
    }
    [TestMethod]
    public void BiddingAndDiscardRetainAll32Decisions()
    {
        foreach (var f in UxEvaluationV1.Build().Where(x => x.Scenario.Phase != "play"))
        foreach (var profile in Enum.GetValues<Bots.StrongProofProfile>())
        {
            var baseline = new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32, 71, f.View.Rules);
            var subject = new Bots.StrongProofAblationBot(profile, 71, f.View.Rules);
            if (f.Scenario.Phase == "bid") Assert.AreEqual(baseline.Bid(f.View, f.LegalBids), subject.Bid(f.View, f.LegalBids));
            else CollectionAssert.AreEquivalent(baseline.Discard(f.View, f.DiscardCount).ToArray(), subject.Discard(f.View, f.DiscardCount).ToArray());
        }
        foreach (var name in new[] { "strong-misere-proof-v1", "strong-contract-proof-v1" })
            Assert.IsInstanceOfType<Bots.StrongProofAblationBot>(Profiles.Create(name, 1));
    }
    [TestMethod]
    public void NormalOnlyEqualsTacticalAndMisereOnlyEqualsAll32ThroughACompleteDeal()
    {
        var paths = new string?[] { null }.Concat(new[] { "presets", "custom", "examples" }.SelectMany(d => Directory.GetFiles(Path.Combine(Root(), "FiveHundredRules", d), "*.json")));
        foreach (var path in paths)
        {
            var rules = RuleConfiguration.Load(path);
            GameRecord Run(IPlayer bot)
            {
                var players = new IPlayer[] { new ForcedNormal(bot), new ForcedNormal(new Bots.MediumBot(2, rules)), new ForcedNormal(new Bots.MediumBot(3, rules)), new ForcedNormal(new Bots.MediumBot(4, rules)) };
                var game = new Engine(rules).Run(players, 29119001, 1, 3);
                ArenaCommand.Replay(game); return game;
            }
            var full = Run(new Bots.StrongTacticalBot(11, rules));
            var normal = Run(new Bots.StrongProofAblationBot(Bots.StrongProofProfile.ContractOnly, 11, rules));
            Assert.AreEqual(JsonSerializer.Serialize(full, ArenaCommand.Json), JsonSerializer.Serialize(normal, ArenaCommand.Json));
            var baseline = Run(new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32, 11, rules));
            var misere = Run(new Bots.StrongProofAblationBot(Bots.StrongProofProfile.MisereOnly, 11, rules));
            Assert.AreEqual(JsonSerializer.Serialize(baseline, ArenaCommand.Json), JsonSerializer.Serialize(misere, ArenaCommand.Json));
        }
    }
    private sealed class ForcedNormal(IPlayer inner) : IPlayer, IJokerDeclarationPlayer
    {
        public string Name => "forced-normal-test";
        public Contract? Bid(Observation view, System.Collections.Generic.IReadOnlyList<Contract?> legal) => view.Auction.Count == 0 ? legal.First(x => x?.Kind == ContractKind.Normal) : null;
        public System.Collections.Generic.IReadOnlyList<PlayingCard> Discard(Observation view, int count) => inner.Discard(view, count);
        public PlayAction Play(Observation view, System.Collections.Generic.IReadOnlyList<PlayAction> legal) => inner.Play(view, legal);
        public System.Collections.Generic.IReadOnlyList<PlayAction> DeclareJokers(Observation view) => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(view) : [];
    }
}
