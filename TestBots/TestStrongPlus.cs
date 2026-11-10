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
public class TestStrongPlus
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string p="")=>Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    [TestMethod]
    public void ConfiguredBudgetsAndRuleMismatchAreExplicit()
    {
        var r=RuleConfiguration.Load(Path.Combine(Root(),"FiveHundredRules/presets/legacy-au-4p-v1.json"));
        var f=UxEvaluationV1.Build().Single(x=>x.Scenario.Id=="play-protect-human-winner");var v=f.View with{Rules=r};
        var bot=new Bots.StrongPlusBot(21,r);var legal=r.LegalPlays(v.Hand,v.Trick,v.Contract!);
        Assert.IsTrue(legal.Contains(bot.Play(v,legal)));Assert.AreEqual(64,bot.LastRollouts);
        Assert.Throws<ArgumentException>(()=>bot.Play(f.View,f.LegalPlays));
        Assert.IsInstanceOfType<Bots.StrongPlusBot>(Profiles.CreateForRules("strong-plus-v1",1,new Rules()));
    }
    [TestMethod]
    public void DefaultGameIsDeterministicAndReplays()
    {
        GameRecord Run()=>new Engine(new Rules()).Run([new Bots.StrongPlusBot(51),new Bots.MediumBot(),new Bots.MediumBot(),new Bots.MediumBot()],431981,2);
        var a=Run();var b=Run();Assert.AreEqual(JsonSerializer.Serialize(a,ArenaCommand.Json),JsonSerializer.Serialize(b,ArenaCommand.Json));ArenaCommand.Replay(a);
    }
    [TestMethod]
    public void EveryConfiguredPresetRunsAndReplays()
    {
        foreach(var dir in new[]{"presets","custom","examples"})foreach(var file in Directory.GetFiles(Path.Combine(Root(),"FiveHundredRules",dir),"*.json"))
        {
            var r=RuleConfiguration.Load(file);var game=new Engine(r).Run([new Bots.StrongPlusBot(7,r),new Bots.MediumBot(8,r),new Bots.MediumBot(9,r),new Bots.MediumBot(10,r)],438191,2);ArenaCommand.Replay(game);
        }
    }
}
