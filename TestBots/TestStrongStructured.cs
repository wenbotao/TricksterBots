#nullable enable
using System;
using System.IO;
using System.Linq;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bots=FiveHundredLab.Bots.FiveHundred;
namespace TestBots;
[TestClass]
public class TestStrongStructured
{
 [TestMethod]
 public void ShortlistMatchesOriginalBudgetAndContainsOnlyUniqueLegalDiscardSets()
 {
  foreach(var f in UxEvaluationV1.Build().Where(x=>x.Scenario.Phase=="discard"))
  {
   var v=f.View;var baseline=new Bots.MediumBot(1,v.Rules).Discard(v,f.DiscardCount).ToArray();
   double Value(PlayingCard[] d)=>HandEvaluator.RetainedValue(v.Hand.Except(d).ToArray(),v.Contract!,v.Rules);
   var ranked=HandEvaluator.DiscardSets(v.Hand,f.DiscardCount).OrderByDescending(Value).ToArray();
   string Key(PlayingCard[] d)=>string.Join("/",d.OrderBy(c=>c.Suit).ThenBy(c=>c.Rank).Select(c=>$"{(int)c.Suit}:{c.Rank}"));
   var old=ranked.Take(12).Prepend(baseline).DistinctBy(Key).ToArray();
   var actual=Bots.StrongStructuredBot.SelectStructuredDiscards(v,ranked,baseline,12,Value);
   Assert.AreEqual(old.Length,actual.Length);Assert.AreEqual(actual.Length,actual.Select(Key).Distinct().Count());
   Assert.IsTrue(actual.Any(d=>Key(d)==Key(baseline)));
   foreach(var d in actual){Assert.AreEqual(f.DiscardCount,d.Length);Assert.IsTrue(d.All(v.Hand.Contains));}
   CollectionAssert.AreEqual(actual.Select(Key).ToArray(),Bots.StrongStructuredBot.SelectStructuredDiscards(v,ranked,baseline,12,Value).Select(Key).ToArray());
  }
 }
 [TestMethod]
 public void NonDiscardDecisionsMatchAll32OnFreshIdenticalInputs()
 {
  foreach(var f in UxEvaluationV1.Build().Where(x=>x.Scenario.Phase!="discard"))
  {
   var a=new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32,71,f.View.Rules);var b=new Bots.StrongStructuredBot(71,f.View.Rules);
   if(f.Scenario.Phase=="bid") Assert.AreEqual(a.Bid(f.View,f.LegalBids),b.Bid(f.View,f.LegalBids));
   else Assert.AreEqual(a.Play(f.View,f.LegalPlays),b.Play(f.View,f.LegalPlays));
  }
 }
 [TestMethod]
 public void DefaultAndSixConfigurationsRunAndReplay()
 {
  string root=Path.GetDirectoryName(Path.GetDirectoryName(Source()))!;
  foreach(var path in new string?[]{null}.Concat(new[]{"presets","custom","examples"}.SelectMany(d=>Directory.GetFiles(Path.Combine(root,"FiveHundredRules",d),"*.json"))))
  {
   var rules=RuleConfiguration.Load(path);var game=new Engine(rules).Run([new Bots.StrongStructuredBot(7,rules),new Bots.MediumBot(8,rules),new Bots.MediumBot(9,rules),new Bots.MediumBot(10,rules)],9029111,2);ArenaCommand.Replay(game);
  }
 }
 private static string Source([System.Runtime.CompilerServices.CallerFilePath]string p="")=>p;
}
