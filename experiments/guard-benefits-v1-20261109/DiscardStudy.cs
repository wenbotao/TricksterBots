using System.Collections.Concurrent;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Trickster.cloud;
using Bots=FiveHundredLab.Bots.FiveHundred;

static class DiscardStudy
{
 private static readonly Contract Contract=new(6,Suit.Unknown);
 public static Observation View(PlayingCard[] hand)=>new(0,3,0,hand,
  Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,s==0?13:10,0,s==0?new Contract?[]{Contract}:new Contract?[]{null},0,[],null)).ToArray(),
  [new(0,Contract),new(1,null),new(2,null),new(3,null)],Contract,0,[],[],[0,0],new Rules(),[]);
 public static void Select(string root)
 {
  var deck=new Rules().Deck();var core=UxFixturesV1.Cards("KS 5S AC QC AH");
  var original=UxFixturesV1.Cards("KS 5S AC QC 8C 8D 9D AH 8H 9H 4D 4H 5C");
  var hands=new List<PlayingCard[]>{original};var keys=new HashSet<string>{Key(original)};int attempt=0;
  while(hands.Count<256)
  {
   var random=new SeededRandom(61119001+attempt++*7919);PlayingCard[] hand;
   if(hands.Count<128)
   {var fill=deck.Except(core).Where(c=>c.Suit!=Suit.Spades&&!c.IsJoker).ToList();random.Shuffle(fill);hand=core.Concat(fill.Take(8)).ToArray();}
   else
   {var removable=original.Except(core).ToList();random.Shuffle(removable);var kept=original.Except(removable.Take(2)).ToArray();var fill=deck.Except(original).Where(c=>c.Suit!=Suit.Spades&&!c.IsJoker).ToList();random.Shuffle(fill);hand=kept.Concat(fill.Take(2)).ToArray();}
   if(keys.Add(Key(hand)))hands.Add(hand);
  }
  IO.Write(Path.Combine(root,"discard-template-plan.json"),new{Hands=hands,Contexts=1024,MaxSelected=128,PerTemplateCap=2,Layouts=8,Policies=new[]{"medium","weak"},CalibrationTemplate=0});
  Parallel.For(0,1024,new ParallelOptions{MaxDegreeOfParallelism=2},index=>
  {
   string path=Path.Combine(root,"proposals",$"case-{index:D4}.json");if(File.Exists(path))return;
   var hand=hands[index/4];int seed=63119001+index*7919;var bot=new Bots.StrongHandGuardBot(seed);
   var after=bot.Discard(View(hand),3).ToArray();var report=bot.LastDiscardGuard!;
   if(report.Changed)
   {
    var before=new Bots.StrongBidGuardBot(seed).Discard(View(hand),3).ToArray();
    if(!before.ToHashSet().SetEquals(report.Original))throw new InvalidDataException("Search mismatch");
   }
   if(after.Length!=3||!after.All(hand.Contains)||after.Distinct().Count()!=3)throw new IllegalActionException("Invalid discard");
   IO.Write(path,new DecisionCase(index,index/4,seed,hand,report.Original,after,report.Changed));
   if(index%32==0)Console.WriteLine($"PROPOSAL {index}/1024 changed={report.Changed}");
  });
  var all=Directory.GetFiles(Path.Combine(root,"proposals"),"case-*.json").Select(IO.Read<DecisionCase>).OrderBy(c=>c.Index).ToArray();
  if(all.Length!=1024)throw new InvalidDataException("Incomplete proposal pool");
  var selected=all.Where(c=>c.Changed&&c.Template!=0).GroupBy(c=>c.Template).SelectMany(g=>g.Take(2)).OrderBy(c=>c.Index).Take(128).ToArray();
  IO.Write(Path.Combine(root,"selected-discards.json"),selected);
  Console.WriteLine($"SELECT total={all.Length} changed={all.Count(c=>c.Changed)} selected={selected.Length} hands={selected.Select(c=>c.Template).Distinct().Count()}");
 }
 public static string Key(IEnumerable<PlayingCard> cards)=>string.Join("/",cards.OrderBy(c=>c.Suit).ThenBy(c=>c.Rank).Select(c=>$"{(int)c.Suit}:{c.Rank}"));
 public static void Continue(string root)
 {
  Directory.CreateDirectory(Path.Combine(root,"continuations"));
  var cases=IO.Read<DecisionCase[]>(Path.Combine(root,"selected-discards.json"));
  var jobs=cases.SelectMany(c=>Enumerable.Range(0,8).SelectMany(layout=>new[]{"medium","weak"}.SelectMany(policy=>new[]{0,1}.Select(arm=>new ContinuationJob(c,layout,policy,arm))))).ToList();
  new SeededRandom(57119999).Shuffle(jobs);int done=0;
  IO.Write(Path.Combine(root,"continuation-plan.json"),new{Cases=cases.Length,Templates=cases.Select(c=>c.Template).Distinct().Count(),Layouts=8,Policies=new[]{"medium","weak"},Deals=jobs.Count});
  Parallel.ForEach(Partitioner.Create(jobs,EnumerablePartitionerOptions.NoBuffering),new ParallelOptions{MaxDegreeOfParallelism=2},job=>
  {
   var c=job.Case;string prefix=Path.Combine(root,"continuations",$"case-{c.Index:D4}",$"layout-{job.Layout}-{job.Policy}-arm-{job.Arm}");
   if(File.Exists(prefix+".json")){if(!File.Exists(prefix+".game.gz"))throw new InvalidDataException("Missing continuation");Interlocked.Increment(ref done);return;}
   int seed=57119001+c.Index*1009+job.Layout*9176;
   var hidden=new Rules().Deck().Except(c.Hand).ToList();new SeededRandom(seed^741).Shuffle(hidden);
   var hands=new[]{c.Hand.Take(10).ToArray(),hidden.Take(10).ToArray(),hidden.Skip(10).Take(10).ToArray(),hidden.Skip(20).Take(10).ToArray()};
   var desired=Enumerable.Range(0,40).Select(i=>hands[i%4][i/4]).Concat(c.Hand.Skip(10)).ToArray();
   var permutation=Enumerable.Range(0,43).ToArray();new SeededRandom(seed).Shuffle(permutation);
   var inverse=new PlayingCard[43];for(int i=0;i<43;i++)inverse[permutation[i]]=desired[i];
   var discards=job.Arm==0?c.Before:c.After;
   var players=Enumerable.Range(0,4).Select(s=>(IPlayer)new FixedActor(MatchStudy.Other(job.Policy,seed+97*(s+1)),discards,c.Hand)).ToArray();
   var game=new Engine(new FixtureRules(inverse)).Run(players,seed,1,3);game.Rules=new Rules();
   if(game.Rounds.Count!=1||game.Rounds[0].TrickWinners.Count!=10)throw new InvalidDataException("Incomplete continuation");
   var archive=new FixtureArchive(inverse,game);Replay(archive);
   Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);IO.Zip(prefix+".game.gz",archive);
   var round=game.Rounds[0];IO.Write(prefix+".json",new{c.Index,c.Template,job.Layout,job.Policy,job.Arm,round.Made,round.Tricks,round.ScoreDelta,RelativeScore=round.ScoreDelta[0]-round.ScoreDelta[1]});
   int n=Interlocked.Increment(ref done);if(n%128==0)Console.WriteLine($"CONTINUATION {n}/{jobs.Count}");
  });
  Console.WriteLine($"CONTINUATIONS complete {done}");
 }
 public static void Replay(FixtureArchive saved)
 {
  var g=saved.Game;var scripts=Enumerable.Range(0,4).Select(s=>new ReplayPlayer(g.Players[s],g.Rounds.SelectMany(r=>r.Decisions).Where(d=>d.Seat==s))).ToArray();
  var replay=new Engine(new FixtureRules(saved.Deck)).Run(scripts,g.Seed,g.MaxRounds,g.InitialDealer);replay.Rules=new Rules();
  if(scripts.Any(s=>!s.Complete)||System.Text.Json.JsonSerializer.Serialize(replay,IO.Json)!=System.Text.Json.JsonSerializer.Serialize(g,IO.Json))throw new InvalidDataException("Fixture replay diverged");
 }
 private sealed class FixedActor(IPlayer inner,PlayingCard[] discards,PlayingCard[] expected):IPlayer,IJokerDeclarationPlayer
 {
  public string Name=>"fixed-discard/"+inner.Name;
  private static Observation Public(Observation v)=>v with{Rules=new Rules()};
  public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>v.Auction.Count==0?Contract:null;
  public IReadOnlyList<PlayingCard> Discard(Observation v,int count)
  {var sanitized=Public(v);if(System.Text.Json.JsonSerializer.Serialize(sanitized,IO.Json)!=System.Text.Json.JsonSerializer.Serialize(View(expected),IO.Json))throw new InvalidDataException("Decision/evaluation observation mismatch");return discards;}
  public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(Public(v),legal);
  public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer p?p.DeclareJokers(Public(v)):[];
 }
}
record DecisionCase(int Index,int Template,int Seed,PlayingCard[] Hand,PlayingCard[] Before,PlayingCard[] After,bool Changed);
record ContinuationJob(DecisionCase Case,int Layout,string Policy,int Arm);
record FixtureArchive(PlayingCard[] Deck,GameRecord Game);
sealed record FixtureRules(PlayingCard[] Fixed):Rules
{public override List<PlayingCard> Deck()=>Fixed.ToList();}
