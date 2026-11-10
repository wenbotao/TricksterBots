#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace TestBots;
[TestClass]
public class TestAdaptComparison
{
    [TestMethod]
    public void AdaptIsExactlyTheFullThreeStageVersion()
    {
        Assert.AreEqual("Adapt",Profiles.Create("Adapt",1).Name);
        IPlayer[] Make(bool named)=>[named?new Adapt(31):new OpponentStrong(31,OpponentAdaptationStage.Play),new MediumBotPlayer(),new WeakBot(),new RandomLegalBot(34)];
        var a=new Engine(new Rules()).Run(Make(true),81371,1,3);var b=new Engine(new Rules()).Run(Make(false),81371,1,3);
        Assert.AreEqual(JsonSerializer.Serialize(a.Rounds,ArenaCommand.Json),JsonSerializer.Serialize(b.Rounds,ArenaCommand.Json));
    }
    [TestMethod]
    public void TwentyGameScheduleIsReproducibleAndBalancesRootSeats()
    {
        var a=AdaptComparison.Plan(20,2711001);var b=AdaptComparison.Plan(20,2711001);
        Assert.AreEqual(JsonSerializer.Serialize(a),JsonSerializer.Serialize(b));
        foreach(int seat in Enumerable.Range(0,4))Assert.AreEqual(5,a.Count(p=>p.RootSeat==seat));
        Assert.IsTrue(a.All(p=>p.OtherProfiles[p.RootSeat]=="ROOT"&&p.OtherProfiles.Where((_,s)=>s!=p.RootSeat).All(AdaptComparison.Pool.Contains)));
        Assert.AreEqual(20,a.Select(p=>p.Seed).Distinct().Count());
    }
}
