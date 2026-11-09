#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bots=FiveHundredLab.Bots.FiveHundred;
namespace TestBots;
[TestClass]
public class TestDiscardRefinement
{
 [TestMethod]
 public void MinimalRepairPreservesControlsAndChangesOnlyOneDiscardWhenPossible()
 {
  var f=UxEvaluationV1.Build().Single(x=>x.Scenario.Id=="discard-nt-stopper");
  var original=UxFixturesV1.Cards("5S 4D 4H");
  var bot=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.Shape,71);
  var selected=bot.RefineProposedDiscard(f.View,original).ToArray();
  Assert.AreEqual(3,selected.Length);Assert.AreEqual(3,selected.Distinct().Count());
  Assert.IsTrue(selected.All(f.View.Hand.Contains));Assert.AreEqual(1,selected.Count(c=>!original.Contains(c)));
  Assert.IsFalse(selected.Any(c=>c.Rank==14||c.IsJoker));
  foreach(var check in f.Scenario.Checks)Assert.IsTrue(check.Accept(f.View,new UxActionV1(Discards:selected)));
  Assert.Throws<ArgumentException>(()=>bot.RefineProposedDiscard(f.View,[original[0],original[0],original[1]]));
 }
 [TestMethod]
 public void HighContractsAndScoreThresholdsRequestIndependentAudit()
 {
  var v=UxEvaluationV1.Build().Single(x=>x.Scenario.Id=="discard-nt-stopper").View;
  foreach(int level in new[]{6,7,8,9,10})
   Assert.AreEqual(level>=8,Bots.StrongDiscardRefinedBot.NeedsAudit(v with{Contract=new(level,Trickster.cloud.Suit.Unknown)},false));
  foreach(var scores in new[]{new[]{400,0},new[]{-400,0},new[]{0,400}})
   Assert.IsTrue(Bots.StrongDiscardRefinedBot.NeedsAudit(v with{Scores=scores},false));
  Assert.IsTrue(Bots.StrongDiscardRefinedBot.NeedsAudit(v,true));
 }
 [TestMethod]
 public void EngineeringProfilesKeepDecisionsAndAuditWorldsIdentical()
 {
  foreach(var f in UxEvaluationV1.Build().Where(x=>x.Scenario.Phase=="discard"))
  {
   var old=new Bots.StrongDiscardGuardBot(71);var expected=old.Discard(f.View,f.DiscardCount).ToArray();
   var equivalent=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientBaseline,71);
   CollectionAssert.AreEquivalent(expected,equivalent.Discard(f.View,f.DiscardCount).ToArray());
   foreach(var pair in new[]{(Bots.DiscardRefinementProfile.Shape,Bots.DiscardRefinementProfile.EfficientShape),(Bots.DiscardRefinementProfile.SelectiveAudit,Bots.DiscardRefinementProfile.EfficientAudit)})
   {
    var a=new Bots.StrongDiscardRefinedBot(pair.Item1,71);var b=new Bots.StrongDiscardRefinedBot(pair.Item2,71);
    CollectionAssert.AreEquivalent(a.Discard(f.View,f.DiscardCount).ToArray(),b.Discard(f.View,f.DiscardCount).ToArray());
    Assert.AreEqual(a.LastAudit,b.LastAudit);Assert.AreEqual(a.LastRollouts,b.LastRollouts);
    Assert.IsTrue(b.LastWork!.StaticEvaluations<=a.LastWork!.StaticEvaluations);
   }
  }
 }
 [TestMethod]
 public void AuditDoesNotAdvanceMainRandomStreamAndReportsNeverGoStale()
 {
  var fixtures=UxEvaluationV1.Build();var f=fixtures.Single(x=>x.Scenario.Id=="discard-nt-stopper");
  var bot=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientAudit,71);
  var v=f.View with{Contract=new(10,Trickster.cloud.Suit.Unknown)};
  bot.RefineProposedDiscard(v,UxFixturesV1.Cards("5S 4D 4H"));Assert.IsTrue(bot.LastAudit!.Reviewed);
  Assert.IsTrue(bot.LastAudit.Samples<=16);Assert.IsNull(bot.LastDefaultSearch);
  var bid=fixtures.First(x=>x.Scenario.Phase=="bid");
  var fresh=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientAudit,71);
  Assert.AreEqual(fresh.Bid(bid.View,bid.LegalBids),bot.Bid(bid.View,bid.LegalBids));
  Assert.IsNull(bot.LastAudit);Assert.IsNull(bot.LastDiscardGuard);Assert.IsNotNull(bot.LastDefaultSearch);
  var play=fixtures.First(x=>x.Scenario.Phase=="play");bot.Play(play.View,play.LegalPlays.Take(1).ToArray());
  Assert.IsNull(bot.LastDefaultSearch);Assert.AreEqual("play",bot.LastPhase);Assert.AreEqual(0,bot.LastRollouts);
 }
 [TestMethod]
 public void DefaultAndExplicitRulesRemainPlayableAndEngineeringIsBehaviorEquivalent()
 {
  string root=Path.GetDirectoryName(Path.GetDirectoryName(Source()))!;
  foreach(var path in new string?[]{null}.Concat(new[]{"presets","custom","examples"}.SelectMany(d=>Directory.GetFiles(Path.Combine(root,"FiveHundredRules",d),"*.json"))))
  {
   var rules=RuleConfiguration.Load(path);
   GameRecord Run(IPlayer subject)
   {
    var game=new Engine(rules).Run([new Label(subject),new Bots.MediumBot(8,rules),new Bots.MediumBot(9,rules),new Bots.MediumBot(10,rules)],81119001,1);
    ArenaCommand.Replay(game);return game;
   }
   var old=Run(new Bots.StrongDiscardGuardBot(7,rules));
   var cached=Run(new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientBaseline,7,rules));
   Assert.AreEqual(JsonSerializer.Serialize(old,ArenaCommand.Json),JsonSerializer.Serialize(cached,ArenaCommand.Json));
   Run(new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientAudit,7,rules));
  }
 }
 private static string Source([System.Runtime.CompilerServices.CallerFilePath]string p="")=>p;
 private sealed class Label(IPlayer inner):IPlayer,IJokerDeclarationPlayer
 {
  public string Name=>"subject";
  public Contract? Bid(Observation v,System.Collections.Generic.IReadOnlyList<Contract?> legal)=>inner.Bid(v,legal);
  public System.Collections.Generic.IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
  public PlayAction Play(Observation v,System.Collections.Generic.IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
  public System.Collections.Generic.IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer p?p.DeclareJokers(v):[];
 }
}
