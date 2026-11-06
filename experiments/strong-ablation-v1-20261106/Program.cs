using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Bots=FiveHundredLab.Bots.FiveHundred;
var root=args[0];Directory.CreateDirectory(root);var json=ArenaCommand.Json;
var comparisons=new[]{
 new Comparison("samples","all32","strong",24,false),
 new Comparison("plus","plus","all32",16,false),
 new Comparison("wide","wide32","all32",8,false),
 new Comparison("threshold","tuned32","wide32",8,false),
 new Comparison("bid-only","bid32","all32",12,false),
 new Comparison("mixed","plus","all32",12,true)
};
IPlayer Create(string profile,int seed,Rules rules)=>profile switch
{
 "strong"=>new Bots.StrongBot(seed,rules),"plus"=>new Bots.StrongPlusBot(seed,rules),
 "base8"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.Baseline8,seed,rules),
 "all32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32,seed,rules),
 "wide32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.Wide32,seed,rules),
 "tuned32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.Tuned32,seed,rules),
 "bid32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.Bid32,seed,rules),
 "discard32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.Discard32,seed,rules),
 "play32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.Play32,seed,rules),
 _=>throw new ArgumentException(profile)
};
if(args[1]=="matches")
{
 var jobs=comparisons.SelectMany(c=>Enumerable.Range(0,c.Pairs).Select(p=>new{Comparison=c,Pair=p})).ToArray();
 File.WriteAllText(Path.Combine(root,"plan.json"),JsonSerializer.Serialize(new{Comparisons=comparisons,SeedBase=8326001,SeedStep=104729,Workers=4,MaxRounds=200,Jobs=jobs},json));
 Parallel.ForEach(jobs,new ParallelOptions{MaxDegreeOfParallelism=4},job=>
 {
  var c=job.Comparison;int pair=job.Pair,seed=unchecked(8326001+pair*104729);string dir=Path.Combine(root,c.Name);Directory.CreateDirectory(dir);
  for(int leg=0;leg<2;leg++)
  {
   string prefix=Path.Combine(dir,$"pair-{pair:D3}-leg-{leg}");if(File.Exists(prefix+".json"))throw new IOException("Refusing overwrite: "+prefix);
   var rules=new Rules();var meters=Enumerable.Range(0,4).Select(s=>
   {
    bool aTeam=s%2==leg,focus=!c.Mixed||s==(aTeam?leg:1-leg);
    IPlayer policy=focus?Create(aTeam?c.A:c.B,unchecked(seed+97*(s+1)),rules):new Bots.MediumBot(1,rules);
    return new Meter(policy);
   }).ToArray();
   var watch=Stopwatch.StartNew();var game=new Engine(rules).Run(meters,seed,200,pair%4);watch.Stop();
   using(var file=File.Create(prefix+".json.gz"))using(var zip=new GZipStream(file,CompressionLevel.Fastest))JsonSerializer.Serialize(zip,game,json);
   var record=new{Group=c.Name,c.A,c.B,c.Mixed,Pair=pair,Leg=leg,Seed=seed,ATeam=leg,game.Winner,game.Truncated,AWin=game.Winner==leg,Deals=game.Rounds.Count,Seconds=watch.Elapsed.TotalSeconds,Players=meters.Select((m,s)=>new{Seat=s,m.Name,Phases=m.Phases.Select(x=>new{Phase=x.Key,Calls=x.Value.Count,Milliseconds=x.Value.Ms}).ToArray()}).ToArray()};
   File.WriteAllText(prefix+".json",JsonSerializer.Serialize(record,json));
  }
  Console.WriteLine($"PAIR {c.Name} {pair}");
 });
}
else if(args[1]=="audit")
{
 var profiles=new[]{"strong","base8","all32","wide32","tuned32","bid32","discard32","play32","plus"};var trials=new List<object>();
 foreach(var fixture in UxEvaluationV1.Build())foreach(int seed in new[]{71,83,97,109})foreach(var profile in profiles)
 {
  var v=fixture.View;var bot=Create(profile,seed,v.Rules);var w=Stopwatch.StartNew();object? value=fixture.Scenario.Phase switch{"bid"=>bot.Bid(v,fixture.LegalBids),"discard"=>bot.Discard(v,fixture.DiscardCount).ToArray(),_=>bot.Play(v,fixture.LegalPlays)};w.Stop();
  UxActionV1 action=fixture.Scenario.Phase switch{"bid"=>new(Bid:(Contract?)value),"discard"=>new(Discards:(PlayingCard[])value!),_=>new(Play:(PlayAction)value!)};
  bool legal=fixture.Scenario.Phase switch{"bid"=>fixture.LegalBids.Contains(action.Bid),"discard"=>action.Discards!.Length==fixture.DiscardCount&&action.Discards.Distinct().Count()==fixture.DiscardCount&&action.Discards.All(v.Hand.Contains),_=>fixture.LegalPlays.Contains(action.Play!)};
  if(!legal)throw new IllegalActionException(profile+" "+fixture.Scenario.Id);
  trials.Add(new{Scenario=fixture.Scenario.Id,Phase=fixture.Scenario.Phase,Seed=seed,Profile=profile,Action=value,Milliseconds=w.Elapsed.TotalMilliseconds,Search=bot is Bots.StrongAblationBot ablation?ablation.LastDefaultSearch:bot is Bots.StrongPlusBot plus?plus.LastDefaultSearch:null,Checks=fixture.Scenario.Checks.Select(c=>new{c.Id,c.Level,Pass=c.Accept(v,action)}).ToArray()});
 }
 File.WriteAllText(Path.Combine(root,"audit.json"),JsonSerializer.Serialize(trials,json));Console.WriteLine("AUDIT "+trials.Count);
}
record Comparison(string Name,string A,string B,int Pairs,bool Mixed);
sealed class Meter(IPlayer inner):IPlayer,IJokerDeclarationPlayer
{
 public string Name=>inner.Name;public Dictionary<string,(int Count,double Ms)> Phases=new();
 private T Time<T>(string phase,Func<T> call){var w=Stopwatch.StartNew();try{return call();}finally{w.Stop();var p=Phases.GetValueOrDefault(phase);Phases[phase]=(p.Count+1,p.Ms+w.Elapsed.TotalMilliseconds);}}
 public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>Time("bid",()=>inner.Bid(v,legal));
 public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>Time("discard",()=>inner.Discard(v,count));
 public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>Time("play",()=>inner.Play(v,legal));
 public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer p?Time("declare",()=>p.DeclareJokers(v)):[];
}
