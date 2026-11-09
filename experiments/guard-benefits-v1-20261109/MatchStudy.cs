using System.Collections.Concurrent;
using System.Diagnostics;
using FiveHundredArena;
using Bots=FiveHundredLab.Bots.FiveHundred;

static class MatchStudy
{
 public static void Run(string root)
 {
  var qualifying=new List<int>();var rules=new Rules();
  for(int i=0;i<100000;i++)
  {
   int seed=51119001+i*97;var deck=rules.Deck();new SeededRandom(seed).Shuffle(deck);
   var hand=Enumerable.Range(0,10).Select(n=>deck[4*n]).ToArray();
   if(hand.All(c=>!c.IsJoker&&c.Rank<=10)&&hand.GroupBy(c=>c.Suit).Max(g=>g.Count())<5)qualifying.Add(seed);
  }
  if(qualifying.Count<64)throw new InvalidDataException("Insufficient eligible natural deals");
  var plans=Enumerable.Range(0,64).SelectMany(i=>new[]{new MatchCase("weak-opening",i,qualifying[i],3,"bid"),new MatchCase("random",i,52119001+i*7919,i%4,"hand")}).ToArray();
  IO.Write(Path.Combine(root,"match-plan.json"),new{Screened=100000,Qualifying=qualifying.Count,EligibleSeeds=qualifying,Cases=plans});
  var jobs=plans.SelectMany(p=>new[]{new MatchJob(p,0),new MatchJob(p,1)}).ToList();new SeededRandom(51119999).Shuffle(jobs);
  int done=0;
  Parallel.ForEach(Partitioner.Create(jobs,EnumerablePartitionerOptions.NoBuffering),new ParallelOptions{MaxDegreeOfParallelism=4},job=>
  {
   var p=job.Case;string prefix=Path.Combine(root,"matches",p.Group,$"pair-{p.Index:D3}-arm-{job.Arm}");
   if(File.Exists(prefix+".json")){if(!File.Exists(prefix+".game.gz"))throw new InvalidDataException("Missing game");Interlocked.Increment(ref done);return;}
   var random=new SeededRandom(p.Seed^527);var roster=Enumerable.Range(0,3).Select(_=>new[]{"weak","medium","fast"}[random.Next(3)]).ToArray();
   IPlayer subject=job.Arm==0?new Bots.StrongProofAblationBot(Bots.StrongProofProfile.MisereOnly,p.Seed+97):p.Subject=="bid"?new Bots.StrongBidGuardBot(p.Seed+97):new Bots.StrongHandGuardBot(p.Seed+97);
   var meter=new RootMeter(subject);var players=new IPlayer[]{meter,Other(roster[0],p.Seed+194),Other(roster[1],p.Seed+291),Other(roster[2],p.Seed+388)};
   var watch=Stopwatch.StartNew();var game=new Engine().Run(players,p.Seed,200,p.Dealer);watch.Stop();
   ArenaCommand.Replay(game);Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);IO.Zip(prefix+".game.gz",game);
   IO.Write(prefix+".json",new{p.Group,p.Index,p.Seed,job.Arm,p.Subject,Roster=roster,game.Winner,game.Truncated,RootWin=game.Winner==0,Deals=game.Rounds.Count,Seconds=watch.Elapsed.TotalSeconds,meter.FirstBid,meter.Events,meter.Calls,meter.Milliseconds});
   Console.WriteLine($"MATCH {Interlocked.Increment(ref done)}/256 {p.Group} {p.Index}/{job.Arm} rootWin={game.Winner==0} events={meter.Events.Count}");
  });
 }
 public static IPlayer Other(string name,int seed)=>name switch{"weak"=>new Bots.WeakBot(seed),"medium"=>new Bots.MediumBot(seed),"fast"=>new Bots.FastBot(seed),_=>throw new ArgumentException(name)};
}
record MatchCase(string Group,int Index,int Seed,int Dealer,string Subject);
record MatchJob(MatchCase Case,int Arm);
sealed class RootMeter(IPlayer inner):IPlayer,IJokerDeclarationPlayer
{
 public string Name=>inner.Name;public Contract? FirstBid{get;private set;}private bool first=true;
 public List<object> Events=new();public int Calls;public double Milliseconds;
 private T Time<T>(Func<T> call){var w=Stopwatch.StartNew();try{return call();}finally{w.Stop();Calls++;Milliseconds+=w.Elapsed.TotalMilliseconds;}}
 public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
 {var a=Time(()=>inner.Bid(v,legal));if(first){FirstBid=a;first=false;}string? reason=inner is Bots.StrongBidGuardBot b?b.LastBidGuard:inner is Bots.StrongHandGuardBot h?h.LastBidGuard:null;if(reason!=null)Events.Add(new{Phase="bid",v.Round,Reason=reason});return a;}
 public IReadOnlyList<PlayingCard> Discard(Observation v,int count)
 {var a=Time(()=>inner.Discard(v,count));if(inner is Bots.StrongHandGuardBot h&&h.LastDiscardGuard is {Changed:true} r)Events.Add(new{Phase="discard",v.Round,Report=r});return a;}
 public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>Time(()=>inner.Play(v,legal));
 public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer p?p.DeclareJokers(v):[];
}
