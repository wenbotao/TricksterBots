#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab.Usability;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bots=FiveHundredLab.Bots.FiveHundred;
namespace TestBots;
[TestClass]
public class TestStrongAblation
{
 [TestMethod]
 public void BaselineControlMatchesFrozenStrongOverDefaultDeals()
 {
  IPlayer[] Seats(bool control)=>Enumerable.Range(0,4).Select(s=>(IPlayer)(s%2==0?(control?new Bots.StrongAblationBot(Bots.StrongAblationProfile.Baseline8,51+s):new Bots.StrongBot(51+s)):new Bots.MediumBot())).ToArray();
  var a=new Engine().Run(Seats(false),8316111,2);var b=new Engine().Run(Seats(true),8316111,2);
  Assert.AreEqual(JsonSerializer.Serialize(a.Rounds,ArenaCommand.Json),JsonSerializer.Serialize(b.Rounds,ArenaCommand.Json));
 }
 [TestMethod]
 public void PhaseAllocationUsesItsNamedBudget()
 {
  var f=UxEvaluationV1.Build();var bid=f.First(x=>x.Scenario.Phase=="bid");var play=f.Single(x=>x.Scenario.Id=="play-protect-human-winner");
  foreach(var profile in Enum.GetValues<Bots.StrongAblationProfile>())
  {
   var bot=new Bots.StrongAblationBot(profile,71,bid.View.Rules);
   Assert.IsInstanceOfType<Bots.StrongAblationBot>(Profiles.CreateForRules(Bots.StrongAblationBot.ProfileName(profile),71,bid.View.Rules));
   Assert.IsTrue(bid.LegalBids.Contains(bot.Bid(bid.View,bid.LegalBids)));
   Assert.AreEqual(profile is Bots.StrongAblationProfile.Baseline8 or Bots.StrongAblationProfile.Discard32 or Bots.StrongAblationProfile.Play32?8:32,bot.LastDefaultSearch!.Samples);
   Assert.IsTrue(play.LegalPlays.Contains(bot.Play(play.View,play.LegalPlays)));
   Assert.AreEqual(profile is Bots.StrongAblationProfile.Baseline8 or Bots.StrongAblationProfile.Bid32 or Bots.StrongAblationProfile.Discard32?8:32,bot.LastDefaultSearch!.Samples);
  }
 }
}
