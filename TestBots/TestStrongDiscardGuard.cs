#nullable enable
using System;
using System.IO;
using System.Linq;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bots=FiveHundredLab.Bots.FiveHundred;
namespace TestBots;

[TestClass]
public class TestStrongDiscardGuard
{
    [TestMethod]
    public void OriginalSearchBiddingAndMiserePlayAreRetained()
    {
        foreach(var f in UxEvaluationV1.Build().Where(x=>x.Scenario.Phase!="discard"))
        foreach(int seed in new[]{71,83,97,109})
        {
            var baseline=new Bots.StrongProofAblationBot(Bots.StrongProofProfile.MisereOnly,seed,f.View.Rules);
            var subject=new Bots.StrongDiscardGuardBot(seed,f.View.Rules);
            if(f.Scenario.Phase=="bid")Assert.AreEqual(baseline.Bid(f.View,f.LegalBids),subject.Bid(f.View,f.LegalBids),f.Scenario.Id);
            else Assert.AreEqual(baseline.Play(f.View,f.LegalPlays),subject.Play(f.View,f.LegalPlays),f.Scenario.Id);
        }
    }
    [TestMethod]
    public void DiscardRepairMatchesFrozenHandGuard()
    {
        foreach(var f in UxEvaluationV1.Build().Where(x=>x.Scenario.Phase=="discard"))
        foreach(int seed in new[]{71,83,97,109})
        {
            var old=new Bots.StrongHandGuardBot(seed,f.View.Rules);
            var subject=new Bots.StrongDiscardGuardBot(seed,f.View.Rules);
            CollectionAssert.AreEquivalent(old.Discard(f.View,f.DiscardCount).ToArray(),subject.Discard(f.View,f.DiscardCount).ToArray());
            CollectionAssert.AreEquivalent(old.LastDiscardGuard!.Original,subject.LastDiscardGuard!.Original);
            Assert.AreEqual(old.LastDiscardGuard.Changed,subject.LastDiscardGuard.Changed);
        }
    }
    [TestMethod]
    public void FactoryAndAllRuleConfigurationsRunAndReplay()
    {
        Assert.IsInstanceOfType<Bots.StrongDiscardGuardBot>(Profiles.Create("strong-discard-guard-v1",1));
        string root=Path.GetDirectoryName(Path.GetDirectoryName(Source()))!;
        foreach(var path in new string?[]{null}.Concat(new[]{"presets","custom","examples"}.SelectMany(d=>Directory.GetFiles(Path.Combine(root,"FiveHundredRules",d),"*.json"))))
        {
            var rules=RuleConfiguration.Load(path);
            var bot=Bots.StrongDiscardGuardBot.FromConfiguration(path,7);
            var game=new Engine(rules).Run([bot,new Bots.MediumBot(8,rules),new Bots.MediumBot(9,rules),new Bots.MediumBot(10,rules)],71119001,2);
            ArenaCommand.Replay(game);
            if(path!=null)
            {
                var f=UxEvaluationV1.Build().First();
                Assert.Throws<ArgumentException>(()=>bot.Bid(f.View,f.LegalBids));
            }
        }
    }
    private static string Source([System.Runtime.CompilerServices.CallerFilePath]string p="")=>p;
}
