using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Bots=FiveHundredLab.Bots.FiveHundred;
var root=args[0];string stage=args[1];Directory.CreateDirectory(root);var json=ArenaCommand.Json;
IPlayer Create(string name,int seed,Rules rules)=>name switch
{
 "strong"=>new Bots.StrongBot(seed,rules),"plus"=>new Bots.StrongPlusBot(seed,rules),
 "all32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32,seed,rules),
 "bid32"=>new Bots.StrongAblationBot(Bots.StrongAblationProfile.Bid32,seed,rules),
 "structured"=>new Bots.StrongStructuredBot(seed,rules),_=>throw new ArgumentException(name)
};
if(stage=="audit")
{
 var output=new List<object>();
 foreach(var f in UxEvaluationV1.Build())foreach(int seed in new[]{71,83,97,109})foreach(string name in new[]{"strong","all32","bid32","plus","structured"})
 {
  var bot=Create(name,seed,f.View.Rules);var watch=Stopwatch.StartNew();
  object? value=f.Scenario.Phase switch{"bid"=>bot.Bid(f.View,f.LegalBids),"discard"=>bot.Discard(f.View,f.DiscardCount).ToArray(),_=>bot.Play(f.View,f.LegalPlays)};watch.Stop();
  var action=f.Scenario.Phase switch{"bid"=>new UxActionV1(Bid:(Contract?)value),"discard"=>new UxActionV1(Discards:(PlayingCard[])value!),_=>new UxActionV1(Play:(PlayAction)value!)};
  bool legal=f.Scenario.Phase switch{"bid"=>f.LegalBids.Contains(action.Bid),"discard"=>action.Discards!.Length==f.DiscardCount&&action.Discards.Distinct().Count()==f.DiscardCount&&action.Discards.All(f.View.Hand.Contains),_=>f.LegalPlays.Contains(action.Play!)};
  if(!legal)throw new IllegalActionException(name+" / "+f.Scenario.Id);
  var search=bot is Bots.StrongStructuredBot ss?ss.LastDefaultSearch:bot is Bots.StrongAblationBot ab?ab.LastDefaultSearch:bot is Bots.StrongPlusBot sp?sp.LastDefaultSearch:null;
  output.Add(new{Scenario=f.Scenario.Id,Phase=f.Scenario.Phase,Seed=seed,Profile=name,Action=value,Milliseconds=watch.Elapsed.TotalMilliseconds,Search=search,Pool=bot is Bots.StrongStructuredBot structured?structured.LastDefaultDiscardPool:null,Checks=f.Scenario.Checks.Select(c=>new{c.Id,c.Level,Pass=c.Accept(f.View,action)}).ToArray()});
 }
 File.WriteAllText(Path.Combine(root,"audit.json"),JsonSerializer.Serialize(output,json));Console.WriteLine("AUDIT "+output.Count);return;
}
Comparison[] comparisons;int seedBase;
if(stage=="screen")
{
 comparisons=new[]{"all32","bid32","plus"}.SelectMany(name=>new[]{new Comparison("budget-"+name+"-pure",name,"strong",3,false),new Comparison("budget-"+name+"-mixed",name,"strong",3,true)})
  .Concat(new[]{new Comparison("structure-pure","structured","all32",4,false),new Comparison("structure-mixed","structured","all32",4,true)}).ToArray();seedBase=9419001;
}
else if(stage=="holdout-budget")
{
 string selected=args[2];if(!new[]{"all32","bid32","plus"}.Contains(selected))throw new ArgumentException("Unknown selection");
 comparisons=[new("budget-pure",selected,"strong",8,false),new("budget-mixed",selected,"strong",8,true)];seedBase=12619001;
}
else if(stage=="holdout-structure")
{
 comparisons=[new("structure-pure","structured","all32",6,false),new("structure-mixed","structured","all32",6,true)];seedBase=15619001;
}
else throw new ArgumentException("Unknown stage");
var jobs=comparisons.SelectMany(c=>Enumerable.Range(0,c.Pairs).SelectMany(pair=>Enumerable.Range(0,2).Select(leg=>new Job(c,pair,leg,unchecked(seedBase+pair*7919+(c.Mixed?678901:0)+(c.A=="structured"&&stage=="screen"?1234567:0)))))).ToList();
new SeededRandom(seedBase^193).Shuffle(jobs);
var plan=new{Stage=stage,Comparisons=comparisons,SeedBase=seedBase,SeedStep=7919,Workers=4,MaxRounds=200,Jobs=jobs};
string planPath=Path.Combine(root,"plan-"+stage+".json"),planText=JsonSerializer.Serialize(plan,json);
if(File.Exists(planPath)&&File.ReadAllText(planPath)!=planText)throw new IOException("Existing plan differs");File.WriteAllText(planPath,planText);
string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Bots.StrongStructuredBot).Assembly.Location)));
string version=Path.Combine(root,"version-"+stage+".txt");if(File.Exists(version)&&File.ReadAllText(version)!=hash)throw new IOException("Cannot resume under a different strategy assembly");File.WriteAllText(version,hash);
int completed=0,skipped=0;var gate=new object();
Parallel.ForEach(Partitioner.Create(jobs,EnumerablePartitionerOptions.NoBuffering),new ParallelOptions{MaxDegreeOfParallelism=4},job=>
{
 var c=job.Comparison;string dir=Path.Combine(root,stage,c.Name);Directory.CreateDirectory(dir);string prefix=Path.Combine(dir,$"pair-{job.Pair:D3}-leg-{job.Leg}");
 if(File.Exists(prefix+".json"))
 {
  using var parsed=JsonDocument.Parse(File.ReadAllText(prefix+".json"));var e=parsed.RootElement;
  if(e.GetProperty("Seed").GetInt32()!=job.Seed||e.GetProperty("A").GetString()!=c.A||e.GetProperty("B").GetString()!=c.B||!File.Exists(prefix+".json.gz"))throw new IOException("Checkpoint mismatch");
  lock(gate)skipped++;return;
 }
 var rules=new Rules();var meters=Enumerable.Range(0,4).Select(s=>
 {
  bool a=s%2==job.Leg,focus=!c.Mixed||s==(a?job.Leg:1-job.Leg);
  return new Meter(focus?Create(a?c.A:c.B,unchecked(job.Seed+97*(s+1)),rules):new Bots.MediumBot(1,rules));
 }).ToArray();
 var watch=Stopwatch.StartNew();GameRecord game;
 try{game=new Engine(rules).Run(meters,job.Seed,200,job.Pair%4);}
 catch(Exception ex){if(ex.Data["record"] is GameRecord partial){using var file=File.Create(prefix+".failure.gz");using var zip=new GZipStream(file,CompressionLevel.Fastest);JsonSerializer.Serialize(zip,partial,json);}throw;}
 watch.Stop();
 using(var file=File.Create(prefix+".json.gz"))using(var zip=new GZipStream(file,CompressionLevel.Fastest))JsonSerializer.Serialize(zip,game,json);
 var record=new{Stage=stage,Group=c.Name,c.A,c.B,c.Mixed,job.Pair,job.Leg,job.Seed,ATeam=job.Leg,game.Winner,game.Truncated,AWin=game.Winner==job.Leg,Deals=game.Rounds.Count,Redeals=game.Rounds.Count(x=>x.Redeal),Seconds=watch.Elapsed.TotalSeconds,Players=meters.Select((m,s)=>new{Seat=s,m.Name,Phases=m.Phases.Select(p=>new{Phase=p.Key,Calls=p.Value.Count,Milliseconds=p.Value.Ms}).ToArray(),DiscardPools=m.DiscardPools.ToArray()}).ToArray()};
 File.WriteAllText(prefix+".json.tmp",JsonSerializer.Serialize(record,json));File.Move(prefix+".json.tmp",prefix+".json");
 lock(gate)Console.WriteLine($"GAME {++completed} {stage} {c.Name} {job.Pair}/{job.Leg} AWin={game.Winner==job.Leg} truncated={game.Truncated}");
});
Console.WriteLine($"STAGE {stage}: completed={completed} skipped={skipped}");
record Comparison(string Name,string A,string B,int Pairs,bool Mixed);
record Job(Comparison Comparison,int Pair,int Leg,int Seed);
sealed class Meter(IPlayer inner):IPlayer,IJokerDeclarationPlayer
{
 public string Name=>inner.Name;public Dictionary<string,(int Count,double Ms)> Phases=new();public List<Bots.StrongStructuredBot.DiscardPoolReport> DiscardPools=new();
 private T Time<T>(string phase,Func<T> call){var w=Stopwatch.StartNew();try{return call();}finally{w.Stop();var p=Phases.GetValueOrDefault(phase);Phases[phase]=(p.Count+1,p.Ms+w.Elapsed.TotalMilliseconds);}}
 public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>Time("bid",()=>inner.Bid(v,legal));
 public IReadOnlyList<PlayingCard> Discard(Observation v,int count){var action=Time("discard",()=>inner.Discard(v,count));if(inner is Bots.StrongStructuredBot b&&b.LastDefaultDiscardPool is {} pool){if(pool.CandidateCount!=pool.OriginalCount)throw new InvalidOperationException("Candidate budget changed");DiscardPools.Add(pool);}return action;}
 public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>Time("play",()=>inner.Play(v,legal));
 public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer p?Time("declare",()=>p.DeclareJokers(v)):[];
}
