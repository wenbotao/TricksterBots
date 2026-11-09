using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Trickster.cloud;
using Bots=FiveHundredLab.Bots.FiveHundred;

static class RefinementStudy
{
 public static string Key(IEnumerable<PlayingCard> cards)=>string.Join("/",cards.OrderBy(c=>c.Suit).ThenBy(c=>c.Rank).Select(c=>$"{(int)c.Suit}:{c.Rank}"));
 private static string OrderedKey(IEnumerable<PlayingCard> cards)=>string.Join("/",cards.Select(c=>$"{(int)c.Suit}:{c.Rank}"));
 public static Observation View(PlayingCard[] hand,int tricks,int[] scores)=>new(0,3,0,hand,
  Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,s==0?13:10,scores[s%2],s==0?new Contract?[]{new(tricks,Suit.Unknown)}:new Contract?[]{null},0,[],null)).ToArray(),
  [new(0,new(tricks,Suit.Unknown)),new(1,null),new(2,null),new(3,null)],new(tricks,Suit.Unknown),0,[],[],scores,new Rules(),[]);
 public static void Prepare(string root)
 {
  var plan=IO.Read<JsonElement>("experiments/guard-benefits-v1-20261109/results/discard-template-plan.json");
  var prior=plan.GetProperty("Hands").Deserialize<PlayingCard[][]>(IO.Json)!;
  var used=prior.Select(Key).ToHashSet();var all=new List<FreshHand>();var deck=new Rules().Deck();
  var core=UxFixturesV1.Cards("KS 5S AC QC AH");var original=UxFixturesV1.Cards("KS 5S AC QC 8C 8D 9D AH 8H 9H 4D 4H 5C");int attempt=0;
  while(all.Count<192)
  {
   var rng=new SeededRandom(83119001+attempt++*7919);PlayingCard[] hand;
   if(all.Count<96){var fill=deck.Except(core).Where(c=>c.Suit!=Suit.Spades&&!c.IsJoker).ToList();rng.Shuffle(fill);hand=core.Concat(fill.Take(8)).ToArray();}
   else{var remove=original.Except(core).ToList();rng.Shuffle(remove);var fill=deck.Except(original).Where(c=>c.Suit!=Suit.Spades&&!c.IsJoker).ToList();rng.Shuffle(fill);hand=original.Except(remove.Take(2)).Concat(fill.Take(2)).ToArray();}
   if(used.Add(Key(hand)))all.Add(new(all.Count,all.Count<96?"random":"perturb",hand,93119001+all.Count*7919));
  }
  IO.Write(Path.Combine(root,"hand-pool.json"),all);
  Parallel.ForEach(all,new ParallelOptions{MaxDegreeOfParallelism=2},h=>
  {
   string p=Path.Combine(root,"screen",$"hand-{h.Id:D3}.json");if(File.Exists(p))return;
   var bot=new Bots.StrongDiscardGuardBot(h.Seed);bot.Discard(View(h.Hand,6,[0,0]),3);
   IO.Write(p,new Screen(h,bot.LastDiscardGuard!.Changed));
  });
  var eligible=all.Where(h=>IO.Read<Screen>(Path.Combine(root,"screen",$"hand-{h.Id:D3}.json")).Changed).ToArray();
  var chosen=eligible.GroupBy(h=>h.Family).SelectMany(g=>g.Take(16)).ToList();
  chosen.AddRange(eligible.Where(h=>!chosen.Contains(h)).Take(32-chosen.Count));chosen=chosen.OrderBy(h=>h.Id).ToList();
  IO.Write(Path.Combine(root,"selected-hands.json"),chosen);Console.WriteLine($"SELECT fresh eligible={eligible.Length}, selected={chosen.Count}");
  var conditions=Enumerable.Range(6,5).Select((n,i)=>new Condition(i,n,[0,0])).Concat(new[]{new Condition(5,6,[400,0]),new Condition(6,6,[-400,0]),new Condition(7,6,[0,400])}).ToArray();
  var states=chosen.SelectMany(h=>conditions.Select(c=>new State(h,c))).ToArray();
  IO.Write(Path.Combine(root,"state-plan.json"),states);
  Parallel.ForEach(states,new ParallelOptions{MaxDegreeOfParallelism=2},state=>
  {
   var h=state.Hand;var c=state.Condition;string p=Path.Combine(root,"decisions",$"hand-{h.Id:D3}-condition-{c.Id}.json");if(File.Exists(p))return;
   var view=View(h.Hand,c.Tricks,c.Scores);var old=new Bots.StrongDiscardGuardBot(h.Seed);var legacy=old.Discard(view,3).ToArray();var baseline=old.LastDiscardGuard!.Original;
   var shape=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.Shape,h.Seed);var shapeChoice=shape.RefineProposedDiscard(view,baseline).ToArray();
   var audit=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.SelectiveAudit,h.Seed);var auditChoice=audit.RefineProposedDiscard(view,baseline).ToArray();
   var fastShape=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientShape,h.Seed);var fastAudit=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientAudit,h.Seed);
   if(!shapeChoice.SequenceEqual(fastShape.RefineProposedDiscard(view,baseline))||!auditChoice.SequenceEqual(fastAudit.RefineProposedDiscard(view,baseline))||audit.LastAudit!=fastAudit.LastAudit)
    throw new InvalidDataException("Engineering altered refinement decisions");
   if(c.Id==0)
   {
    var equivalent=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientBaseline,h.Seed);
    if(!legacy.SequenceEqual(equivalent.Discard(view,3)))throw new InvalidDataException("Cached baseline differs from v1");
   }
   var choices=new Dictionary<string,PlayingCard[]>{{"original",baseline},{"v1",legacy},{"shape",shapeChoice},{"audit",auditChoice}};
   foreach(var d in choices.Values)if(d.Length!=3||d.Distinct().Count()!=3||!d.All(h.Hand.Contains))throw new IllegalActionException("Bad proposal");
   IO.Write(p,new Proposal(state,choices,shape.LastDiscardGuard!,audit.LastAudit!,shape.LastWork!,fastShape.LastWork!));
  });
  Console.WriteLine($"PREPARED {states.Length} boundary states; engineering decisions matched");
 }
 public static void Run(string root)
 {
  var states=IO.Read<State[]>(Path.Combine(root,"state-plan.json"));
  var jobs=states.SelectMany(s=>Enumerable.Range(0,2).SelectMany(l=>new[]{"medium","mixed"}.Select(p=>new Job(s,l,p)))).ToList();new SeededRandom(96119999).Shuffle(jobs);
  IO.Write(Path.Combine(root,"evaluation-plan.json"),new{Groups=jobs.Count,LogicalArms=4,Layouts=2,Policies=new[]{"medium","mixed"}});int done=0,physical=0;
  Parallel.ForEach(Partitioner.Create(jobs,EnumerablePartitionerOptions.NoBuffering),new ParallelOptions{MaxDegreeOfParallelism=4},job=>
  {
   var state=job.State;var h=state.Hand;var c=state.Condition;
   string dir=Path.Combine(root,"outcomes",$"hand-{h.Id:D3}-condition-{c.Id}-layout-{job.Layout}-{job.Policy}");Directory.CreateDirectory(dir);
   if(File.Exists(Path.Combine(dir,"complete.json"))){Interlocked.Increment(ref done);return;}
   var proposal=IO.Read<Proposal>(Path.Combine(root,"decisions",$"hand-{h.Id:D3}-condition-{c.Id}.json"));
   int seed=96119001+h.Id*1009+c.Id*97+job.Layout*9176;
   var hidden=new Rules().Deck().Except(h.Hand).ToList();new SeededRandom(seed^741).Shuffle(hidden);
   var hands=new[]{h.Hand.Take(10).ToArray(),hidden.Take(10).ToArray(),hidden.Skip(10).Take(10).ToArray(),hidden.Skip(20).Take(10).ToArray()};
   var desired=Enumerable.Range(0,40).Select(i=>hands[i%4][i/4]).Concat(h.Hand.Skip(10)).ToArray();var permutation=Enumerable.Range(0,43).ToArray();new SeededRandom(seed).Shuffle(permutation);var inverse=new PlayingCard[43];for(int i=0;i<43;i++)inverse[permutation[i]]=desired[i];
   var groups=proposal.Choices.GroupBy(x=>OrderedKey(x.Value)).ToArray();int index=0;
   foreach(var group in groups)
   {
    var d=group.First().Value;var roster=Enumerable.Range(0,4).Select(s=>job.Policy=="medium"?"medium":s==0?"strong":s==2?"fast":((s==1)==(job.Layout==0)?"medium":"strong")).ToArray();
    var actors=Enumerable.Range(0,4).Select(s=>(IPlayer)new Actor(Create(roster[s],seed+97*(s+1)),state,d)).ToArray();
    var game=new Engine(new FixtureRules(inverse)).Run(actors,seed,1,3);game.Rules=new Rules();var round=game.Rounds.Single();
    if(round.TrickWinners.Count!=10)throw new InvalidDataException("Incomplete deal");
    var post=c.Scores.Zip(round.ScoreDelta,(a,b)=>a+b).ToArray();int? winner=new Rules().WinningTeam(post,0,round.Made);
    double utility=winner.HasValue?(winner==0?2000:-2000):round.ScoreDelta[0]-round.ScoreDelta[1];
    var archive=new Archive(inverse,game,c.Scores,post,winner);Replay(archive);
    string prefix=Path.Combine(dir,$"choice-{index++}");IO.Zip(prefix+".game.gz",archive);
    IO.Write(prefix+".json",new{Hand=h.Id,h.Family,Condition=c.Id,c.Tricks,c.Scores,job.Layout,job.Policy,Arms=group.Select(x=>x.Key).ToArray(),Discard=d,Roster=roster,round.Made,round.ScoreDelta,RelativeScore=round.ScoreDelta[0]-round.ScoreDelta[1],Utility=utility,Winner=winner});
    Interlocked.Increment(ref physical);
   }
   IO.Write(Path.Combine(dir,"complete.json"),new{Choices=groups.Length,LogicalArms=4});int n=Interlocked.Increment(ref done);if(n%64==0)Console.WriteLine($"GROUP {n}/{jobs.Count} physical={physical}");
  });
  Console.WriteLine($"COMPLETE groups={done} physical-new={physical}");
 }
 private static IPlayer Create(string policy,int seed)=>policy switch{"medium"=>new Bots.MediumBot(seed),"fast"=>new Bots.FastBot(seed),"strong"=>new Bots.StrongBot(seed),_=>throw new ArgumentException(policy)};
 public static void Replay(Archive a)
 {
  var g=a.Game;var scripts=Enumerable.Range(0,4).Select(s=>new ReplayPlayer(g.Players[s],g.Rounds.SelectMany(r=>r.Decisions).Where(d=>d.Seat==s))).ToArray();var replay=new Engine(new FixtureRules(a.Deck)).Run(scripts,g.Seed,g.MaxRounds,g.InitialDealer);replay.Rules=new Rules();
  if(scripts.Any(s=>!s.Complete)||JsonSerializer.Serialize(replay,IO.Json)!=JsonSerializer.Serialize(g,IO.Json))throw new InvalidDataException("Replay mismatch");
  var r=g.Rounds.Single();var post=a.InitialScores.Zip(r.ScoreDelta,(x,y)=>x+y).ToArray();if(!post.SequenceEqual(a.PostScores)||new Rules().WinningTeam(post,0,r.Made)!=a.Winner)throw new InvalidDataException("Score overlay mismatch");
 }
 private sealed class Actor(IPlayer inner,State state,PlayingCard[] discard):IPlayer,IJokerDeclarationPlayer
 {
  public string Name=>"refinement/"+inner.Name;
  private Observation Public(Observation v)=>v with{Rules=new Rules(),Scores=state.Condition.Scores.ToArray(),Players=v.Players.Select(p=>p with{Score=state.Condition.Scores[p.Seat%2]}).ToArray()};
  public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>v.Auction.Count==0?new(state.Condition.Tricks,Suit.Unknown):null;
  public IReadOnlyList<PlayingCard> Discard(Observation v,int count)
  {if(JsonSerializer.Serialize(Public(v),IO.Json)!=JsonSerializer.Serialize(View(state.Hand.Hand,state.Condition.Tricks,state.Condition.Scores),IO.Json))throw new InvalidDataException("Public state mismatch");return discard;}
  public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(Public(v),legal);
  public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer p?p.DeclareJokers(Public(v)):[];
 }
}
record FreshHand(int Id,string Family,PlayingCard[] Hand,int Seed);
record Screen(FreshHand Hand,bool Changed);
record Condition(int Id,int Tricks,int[] Scores);
record State(FreshHand Hand,Condition Condition);
record Proposal(State State,Dictionary<string,PlayingCard[]> Choices,Bots.StrongDiscardRefinedBot.DiscardGuardReport ShapeReport,Bots.StrongDiscardRefinedBot.AuditReport Audit,Bots.StrongDiscardRefinedBot.WorkReport ReferenceWork,Bots.StrongDiscardRefinedBot.WorkReport CachedWork);
record Job(State State,int Layout,string Policy);
record Archive(PlayingCard[] Deck,GameRecord Game,int[] InitialScores,int[] PostScores,int? Winner);
sealed record FixtureRules(PlayingCard[] Fixed):Rules{public override List<PlayingCard> Deck()=>Fixed.ToList();}
