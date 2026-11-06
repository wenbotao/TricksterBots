using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Bots=FiveHundredLab.Bots.FiveHundred;
var root=args[0];Directory.CreateDirectory(root);var json=ArenaCommand.Json;
if(args[1]=="matches")
{
 int start=int.Parse(args[2]),count=int.Parse(args[3]);
 Parallel.For(start,start+count,new ParallelOptions{MaxDegreeOfParallelism=4},pair=>
 {
  bool mixed=pair>=30;int seed=unchecked(7116001+pair*7919);var records=new List<object>();
  for(int leg=0;leg<2;leg++)
  {
   string prefix=Path.Combine(root,$"pair-{pair:D3}-leg-{leg}");if(File.Exists(prefix+".json"))throw new IOException("Exists: "+prefix);
   var rules=new Rules();var meters=new List<Meter>();
   var players=Enumerable.Range(0,4).Select(s=>
   {
    bool plusTeam=s%2==leg;bool focus=!mixed||s==(plusTeam?leg:1-leg);
    IPlayer policy=focus?(plusTeam?new Bots.StrongPlusBot(unchecked(seed+97*(s+1)),rules):new Bots.StrongBot(unchecked(seed+97*(s+1)),rules)):new Bots.MediumBot(1,rules);
    var meter=new Meter(policy);meters.Add(meter);return (IPlayer)meter;
   }).ToArray();
   var watch=Stopwatch.StartNew();var game=new Engine(rules).Run(players,seed,200,(pair%4));watch.Stop();
   using(var file=File.Create(prefix+".json.gz"))using(var zip=new GZipStream(file,CompressionLevel.Fastest))JsonSerializer.Serialize(zip,game,json);
   var data=new{Pair=pair,Leg=leg,Mixed=mixed,Seed=seed,PlusTeam=leg,game.Winner,game.Truncated,PlusWin=game.Winner==leg,Deals=game.Rounds.Count,Seconds=watch.Elapsed.TotalSeconds,Players=meters.Select(m=>new{m.Name,m.Calls,m.Milliseconds}).ToArray()};
   File.WriteAllText(prefix+".json",JsonSerializer.Serialize(data,json));records.Add(data);
  }
  Console.WriteLine("PAIR "+pair+" "+JsonSerializer.Serialize(records,json));
 });
}
else if(args[1]=="audit")
{
 var trials=new List<object>();
 // Warm each implementation on a discarded instance; recorded actors retain fresh seeded streams.
 var warm=UxEvaluationV1.Build().First();
 new Bots.StrongBot(7,warm.View.Rules).Bid(warm.View,warm.LegalBids);
 new Bots.StrongPlusBot(7,warm.View.Rules).Bid(warm.View,warm.LegalBids);
 new FiveHundredArena.StrongBot(7,new StrongBotOptions{Samples=32}).Bid(warm.View,warm.LegalBids);
 foreach(var fixture in UxEvaluationV1.Build())foreach(int seed in new[]{71,83,97,109})
 {
  var v=fixture.View;var a=new Bots.StrongBot(seed,v.Rules);var b=new Bots.StrongPlusBot(seed,v.Rules);
  var budgetOnly=new FiveHundredArena.StrongBot(seed,new StrongBotOptions{Samples=32});
  object? Act(IPlayer bot)=>fixture.Scenario.Phase switch{"bid"=>bot.Bid(v,fixture.LegalBids),"discard"=>bot.Discard(v,fixture.DiscardCount).ToArray(),_=>bot.Play(v,fixture.LegalPlays)};
  var w=Stopwatch.StartNew();var old=Act(a);w.Stop();double oldMs=w.Elapsed.TotalMilliseconds;w.Restart();var enhanced=Act(b);w.Stop();
  double plusMs=w.Elapsed.TotalMilliseconds;w.Restart();var budget=Act(budgetOnly);w.Stop();double budgetMs=w.Elapsed.TotalMilliseconds;
  string os=JsonSerializer.Serialize(old,json),es=JsonSerializer.Serialize(enhanced,json);
  trials.Add(new{Scenario=fixture.Scenario.Id,Phase=fixture.Scenario.Phase,Seed=seed,Strong=old,Plus=enhanced,Changed=os!=es,StrongMs=oldMs,PlusMs=plusMs,Budget32=budget,Budget32Ms=budgetMs,PlusDiffersFromBudget=es!=JsonSerializer.Serialize(budget,json),Checks=fixture.Scenario.Checks.Select(c=>new{c.Id,Strong=c.Accept(v,Action(old)),Plus=c.Accept(v,Action(enhanced))}).ToArray()});
  UxActionV1 Action(object? value)=>fixture.Scenario.Phase switch{"bid"=>new(Bid:(Contract?)value),"discard"=>new(Discards:(PlayingCard[])value!),_=>new(Play:(PlayAction)value!)};
 }
 File.WriteAllText(Path.Combine(root,"audit.json"),JsonSerializer.Serialize(trials,json));Console.WriteLine("AUDIT "+trials.Count);
}
sealed class Meter(IPlayer inner):IPlayer,IJokerDeclarationPlayer
{
 public string Name=>inner.Name;public int Calls;public double Milliseconds;
 private T Time<T>(Func<T> call){var w=Stopwatch.StartNew();try{return call();}finally{w.Stop();Milliseconds+=w.Elapsed.TotalMilliseconds;Calls++;}}
 public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>Time(()=>inner.Bid(v,legal));
 public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>Time(()=>inner.Discard(v,count));
 public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>Time(()=>inner.Play(v,legal));
 public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer d?Time(()=>d.DeclareJokers(v)):[];
}
