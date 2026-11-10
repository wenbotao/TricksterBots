using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
const string folder="experiments/rules-v1-1-20261110/results";
var json=ArenaCommand.Json;
string[] profiles=["random-v1.1","random-pass-v1.1","weak-v1.1","medium-v1.1","fast-v1.1","strong-v1.1","partner-v1.1","strong-plus-v1.1","strong-flow-cached-v1.1"];
void Write(string file,object data)=>File.WriteAllText(Path.Combine(folder,file),JsonSerializer.Serialize(data,json));
T Read<T>(string file)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(folder,file)),json)!;
string mode=args[0];
if(mode=="prepare")
{
    if(File.Exists(Path.Combine(folder,"registration.json")))throw new IOException("Already registered");
    var columns=Enumerable.Range(0,4).Select(seat=>Enumerable.Range(0,180).Select(i=>i%9).ToList()).ToArray();
    for(int seat=0;seat<4;seat++)new SeededRandom(481119001+seat*7919).Shuffle(columns[seat]);
    var plans=Enumerable.Range(0,180).Select(id=>new Plan(id,491119001+id*7919,id%4,Enumerable.Range(0,4).Select(s=>profiles[columns[s][id]]).ToArray(),Enumerable.Range(0,4).Select(s=>492119001+id*31+s*97).ToArray())).ToArray();
    Write("plan.json",plans);
    Write("registration.json",new{FixedBeforeMatches=true,Matches=180,Profiles=profiles,SeatAppearancesPerProfile=80,PerPhysicalSeat=20,MaxRounds=200,Rules=RuleConfiguration.Load(),
        SourceHashes=Directory.GetFiles("FiveHundredLab/Bots/FiveHundred/V11","*.cs").Concat(new[]{"FiveHundredArena/EngineV11.cs","FiveHundredArena/ConfiguredRules.cs","FiveHundredArena/ConfiguredRules.V11.cs","FiveHundredArena/RuleConfigurationV11.cs","FiveHundredArena/Model.cs","FiveHundredRules/rules.schema.json","FiveHundredRules/v1.1/presets/au-default-4p-v1-1.json"}).ToDictionary(f=>f,f=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))),
        Design="180 pre-fixed random lineups. Each physical seat independently shuffles 20 copies of each of nine profiles;repeats within match allowed;independent instances and RNG. No hand/outcome filtering;all completed seats counted,truncate excluded. Match-cluster bootstrap for win rates. Old-rule data not pooled. No significance stopping.",
        Timing="Four independent single-worker shards;natural timings descriptive only. Separate serial matched-state micro measurements after games/tests end.",Runtime=Environment.Version.ToString()});
}
else if(mode=="run")
{
    int shard=int.Parse(args[1]);var plans=Read<Plan[]>("plan.json");
    var rules=Read<JsonElement>("registration.json").GetProperty("Rules").Deserialize<Rules>(json)!;
    foreach(var plan in plans.Where(p=>p.Id%4==shard))
    {
        string name=$"game-{plan.Id:D3}";if(File.Exists(Path.Combine(folder,name+".json")))continue;
        var metrics=Enumerable.Range(0,4).Select(s=>new Metrics(plan.Profiles[s])).ToArray();
        var bots=Enumerable.Range(0,4).Select(s=>(IPlayer)new Meter(Profiles.CreateForRules(plan.Profiles[s],plan.BotSeeds[s],rules),metrics[s])).ToArray();
        var watch=Stopwatch.StartNew();
        try
        {
            var game=new Engine(rules).Run(bots,plan.Seed,200,plan.Dealer);watch.Stop();ArenaCommand.Replay(game);
            using(var f=File.Create(Path.Combine(folder,name+".game.gz")))using(var zip=new GZipStream(f,CompressionLevel.SmallestSize))JsonSerializer.Serialize(zip,game,json);
            Write(name+".json",new{Plan=plan,game.Winner,game.Truncated,Rounds=game.Rounds.Count,Redeals=game.Rounds.Count(r=>r.Redeal),FinalScores=game.Rounds.Last().Scores,Seconds=watch.Elapsed.TotalSeconds,Metrics=metrics,
                Contracts=game.Rounds.Where(r=>!r.Redeal).Select(r=>new{r.Contract,r.Declarer,r.Made,r.Tricks,r.ScoreDelta}).ToArray()});
            Console.WriteLine($"MATCH {plan.Id+1}/180 winner={game.Winner} truncated={game.Truncated} seconds={watch.Elapsed.TotalSeconds:F1}");
        }
        catch(Exception ex){Write(name+".failure.json",new{Plan=plan,Error=ex.ToString(),Partial=ex.Data["record"] as GameRecord});throw;}
    }
}
else if(mode=="micro")
{
    var cases=new List<Case>();var used=new HashSet<string>();
    foreach(var plan in Read<Plan[]>("plan.json"))
    {
        using var f=File.OpenRead(Path.Combine(folder,$"game-{plan.Id:D3}.game.gz"));using var zip=new GZipStream(f,CompressionMode.Decompress);var game=JsonSerializer.Deserialize<GameRecord>(zip,json)!;
        var captured=new List<Case>();var actors=Enumerable.Range(0,4).Select(s=>(IPlayer)new Capture(new ReplayPlayer(game.Players[s],game.Rounds.SelectMany(r=>r.Decisions).Where(d=>d.Seat==s)),captured,plan.Id)).ToArray();
        var replay=new Engine(game.Rules).Run(actors,game.Seed,game.MaxRounds,game.InitialDealer);
        if(JsonSerializer.Serialize(replay,json)!=JsonSerializer.Serialize(game,json))throw new InvalidDataException("Source replay changed");
        foreach(string bucket in new[]{"bid","discard","early","middle","late","forced"})
        {
            if(cases.Count(c=>c.Bucket==bucket)>=12)continue;
            bool Eligible(Case c)=>bucket switch
            {
                "bid"=>c.Phase=="bid", "discard"=>c.Phase=="discard"&&c.Count>0,
                "early"=>c.Phase=="play"&&c.View.Hand.Count>=8&&c.Plays.Length>=4,
                "middle"=>c.Phase=="play"&&c.View.Hand.Count is >=4 and <=7&&c.Plays.Length>=3,
                "late"=>c.Phase=="play"&&c.View.Hand.Count<=3&&c.Plays.Length>=2,
                "forced"=>c.Phase=="play"&&c.Plays.Length==1,_=>false
            };
            var pick=captured.FirstOrDefault(c=>Eligible(c)&&!used.Contains(JsonSerializer.Serialize(c.View,json)));
            if(pick==null)continue;used.Add(JsonSerializer.Serialize(pick.View,json));cases.Add(pick with{Id=cases.Count,Bucket=bucket,Seed=493119001+cases.Count*7919});
        }
        if(cases.Count==72)break;
    }
    if(cases.Count!=72)throw new InvalidDataException("Incomplete new-rule micro corpus");
    Write("micro-cases.json",cases);Write("micro-registration.json",new{States=72,Repeats=3,Profiles=profiles,Selection="First eligible per game/stratum,12 per six strata,public-state deduplication,no result/timing selection;states sourced by authoritative replay of new default-rule matches.",Method="All profiles serial;three repeats,shuffle states,cyclic profile order;new instances/identical seeds,constructors excluded;root setup included;source-game cluster inference."});
    foreach(string bucket in new[]{"bid","discard","early","middle","late"})foreach(string profile in profiles)Measure(cases.First(c=>c.Bucket==bucket),profile);
    var ordered=cases.ToList();new SeededRandom(494119001).Shuffle(ordered);int order=0;var measurements=new List<CostRow>();
    foreach(var c in ordered)for(int repeat=0;repeat<3;repeat++)
    {
        var values=new Dictionary<string,Cost>();int offset=order++%9;
        for(int i=0;i<9;i++){string profile=profiles[(offset+i)%9];values[profile]=Measure(c,profile);}
        measurements.Add(new(c.Id,c.Source,c.Bucket,repeat,values));Console.WriteLine($"MICRO {measurements.Count}/216 {c.Bucket}");
    }
    Write("micro-measurements.json",measurements);
}
else throw new ArgumentException("prepare|run SHARD|micro");
Cost Measure(Case c,string profile)
{
    var player=Profiles.CreateForRules(profile,c.Seed,c.View.Rules);long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
    object? action=c.Phase switch{"bid"=>player.Bid(c.View,c.Bids),"discard"=>player.Discard(c.View,c.Count),_=>player.Play(c.View,c.Plays)};
    double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
    if(c.Phase=="bid"&&!c.Bids.Contains((Contract?)action)||c.Phase=="play"&&!c.Plays.Contains((PlayAction)action!))throw new InvalidDataException("Illegal micro action");
    if(c.Phase=="discard"){var cards=((IReadOnlyList<PlayingCard>)action!).ToArray();if(cards.Length!=c.Count||cards.Distinct().Count()!=cards.Length||cards.Any(x=>!c.View.Hand.Contains(x)))throw new InvalidDataException("Illegal micro discard");}
    return new(JsonSerializer.Serialize(action,json),ms,allocated,player is FiveHundredLab.Bots.FiveHundred.V11.FiveHundredRuleBot bot?bot.LastRollouts:0);
}
record Plan(int Id,int Seed,int Dealer,string[] Profiles,int[] BotSeeds);
record Case(int Id,int Source,string Phase,string Bucket,int Seed,Observation View,Contract?[] Bids,PlayAction[] Plays,int Count);
record Cost(string Action,double Milliseconds,long AllocatedBytes,int Rollouts);
record CostRow(int Id,int Source,string Bucket,int Repeat,Dictionary<string,Cost> Values);
sealed class Metrics(string profile){public string Profile{get;}=profile;public Dictionary<string,Metric> Phases{get;}=new();}
sealed class Metric{public int Calls{get;set;}public double Milliseconds{get;set;}public long AllocatedBytes{get;set;}}
sealed class Meter(IPlayer inner,Metrics metrics):IPlayer,IJokerDeclarationPlayer,IRuleVariationPlayer
{
    public string Name=>inner.Name;
    T Act<T>(string phase,Func<T> action){long alloc=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();try{return action();}finally{if(!metrics.Phases.TryGetValue(phase,out var m))metrics.Phases[phase]=m=new();m.Calls++;m.Milliseconds+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;m.AllocatedBytes+=GC.GetAllocatedBytesForCurrentThread()-alloc;}}
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>Act("bid",()=>inner.Bid(v,legal));
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>Act("discard",()=>inner.Discard(v,count));
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>Act("play",()=>inner.Play(v,legal));
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>Act("declare",()=>((IJokerDeclarationPlayer)inner).DeclareJokers(v));
    public bool RequestRedeal(Observation v)=>Act("redeal-request",()=>((IRuleVariationPlayer)inner).RequestRedeal(v));
    public bool DeclareGrandSlam(Observation v)=>Act("grand-slam",()=>((IRuleVariationPlayer)inner).DeclareGrandSlam(v));
    public PlayingCard TransferCard(Observation v,IReadOnlyList<PlayingCard> legal)=>Act("transfer",()=>((IRuleVariationPlayer)inner).TransferCard(v,legal));
}
sealed class Capture(ReplayPlayer inner,List<Case> captured,int source):IPlayer,IJokerDeclarationPlayer,IRuleVariationPlayer
{
    public string Name=>inner.Name;
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal){captured.Add(new(0,source,"bid","",0,v,legal.ToArray(),[],0));return inner.Bid(v,legal);}
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count){captured.Add(new(0,source,"discard","",0,v,[],[],count));return inner.Discard(v,count);}
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal){captured.Add(new(0,source,"play","",0,v,[],legal.ToArray(),0));return inner.Play(v,legal);}
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner.DeclareJokers(v);
    public bool RequestRedeal(Observation v)=>inner.RequestRedeal(v);
    public bool DeclareGrandSlam(Observation v)=>inner.DeclareGrandSlam(v);
    public PlayingCard TransferCard(Observation v,IReadOnlyList<PlayingCard> legal)=>inner.TransferCard(v,legal);
}
