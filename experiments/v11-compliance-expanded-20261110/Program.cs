using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
const string folder="experiments/v11-compliance-expanded-20261110/results";
var json=ArenaCommand.Json;
string[] profiles=["random-v1.1","random-pass-v1.1","weak-v1.1","medium-v1.1","fast-v1.1","strong-v1.1","partner-v1.1","strong-plus-v1.1","strong-flow-cached-v1.1"];
void Write(string file,object data)=>File.WriteAllText(Path.Combine(folder,file),JsonSerializer.Serialize(data,json));
T Read<T>(string file)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(folder,file)),json)!;
string mode=args[0];
if(mode=="prepare")
{
    if(File.Exists(Path.Combine(folder,"registration.json")))throw new IOException("Already registered");
    var columns=Enumerable.Range(0,4).Select(seat=>Enumerable.Range(0,540).Select(i=>i%9).ToList()).ToArray();
    for(int seat=0;seat<4;seat++)new SeededRandom(581119001+seat*7919).Shuffle(columns[seat]);
    var plans=Enumerable.Range(0,540).Select(id=>new Plan(id,591119001+id*7919,id%4,Enumerable.Range(0,4).Select(s=>profiles[columns[s][id]]).ToArray(),Enumerable.Range(0,4).Select(s=>592119001+id*31+s*97).ToArray())).ToArray();
    Write("plan.json",plans);
    Write("registration.json",new{FixedBeforeMatches=true,Matches=540,Profiles=profiles,SeatAppearancesPerProfile=240,PerPhysicalSeat=60,MaxRounds=200,Rules=RuleConfiguration.Load(),
        SourceHashes=Directory.GetFiles("FiveHundredLab/Bots/FiveHundred/V11","*.cs").Concat(new[]{"FiveHundredArena/EngineV11.cs","FiveHundredArena/ConfiguredRules.cs","FiveHundredArena/ConfiguredRules.V11.cs","FiveHundredArena/RuleConfigurationV11.cs","FiveHundredArena/Model.cs","FiveHundredArena/Engine.cs","FiveHundredArena/ConfigurationStructure.cs","FiveHundredArena/Program.cs","FiveHundredLab/Profiles.cs","FiveHundredRules/rules.schema.json","FiveHundredRules/v1.1/presets/au-default-4p-v1-1.json"}).ToDictionary(f=>f,f=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))),
        Design="540 pre-fixed random lineups. Each physical seat independently shuffles 60 copies of each of nine profiles;repeats within match allowed;independent instances and RNG. No hand/outcome filtering;all completed seats counted,truncate excluded. Match-cluster bootstrap for win rates. Old-rule data not pooled. No significance stopping.",
        Timing="Four independent single-worker shards;natural timings descriptive only. Phase timings descriptive only;no new absolute-cost benchmark. Previously measured v1.1 costs remain separate.",Runtime=Environment.Version.ToString()});
}
else if(mode=="run")
{
    int shard=int.Parse(args[1]);var plans=Read<Plan[]>("plan.json");
    var registration=Read<JsonElement>("registration.json");
    foreach(var source in registration.GetProperty("SourceHashes").EnumerateObject())
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.Name)))!=source.Value.GetString())throw new InvalidDataException("Source fingerprint changed: "+source.Name);
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
            Console.WriteLine($"MATCH {plan.Id+1}/540 winner={game.Winner} truncated={game.Truncated} seconds={watch.Elapsed.TotalSeconds:F1}");
        }
        catch(Exception ex){Write(name+".failure.json",new{Plan=plan,Error=ex.ToString(),Partial=ex.Data["record"] as GameRecord});throw;}
    }
}
else throw new ArgumentException("prepare|run SHARD");
record Plan(int Id,int Seed,int Dealer,string[] Profiles,int[] BotSeeds);
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
