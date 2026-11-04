using System.Diagnostics;
using System.Text.Json;
using FiveHundredArena;
namespace FiveHundredLab;

// Returns recorded moves to the engine; measured candidate moves never alter the observation corpus.
public static class CorpusProfilerV1
{
    public static void Run(string input,string output,IReadOnlyList<string> names,int games,int decisions)
    {
        if(games<1||decisions<1||names.Count==0||names.Distinct().Count()!=names.Count)throw new ArgumentException("Positive limits and distinct profiles required");
        if(File.Exists(output))throw new IOException("Profiling output already exists");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var corpus=File.ReadLines(input).Where(l=>!string.IsNullOrWhiteSpace(l)).Take(games).Select(l=>JsonSerializer.Deserialize<GameRecord>(l,LabJson.Options)!).ToArray();
        if(corpus.Length==0)throw new InvalidDataException("Empty observation corpus");
        var result=new Dictionary<string,Dictionary<string,PhaseTiming>>();
        foreach(var name in names)
        {
            // Warm on a separate unmeasured game, not on measured states.
            new Engine(new()).Run(Enumerable.Range(0,4).Select(s=>Profiles.Create(name,17+s)).ToArray(),-276315,1);
            var meter=new Telemetry();var counts=new Dictionary<string,int>();
            foreach(var game in corpus)
            {
                var scripts=Enumerable.Range(0,4).Select(s=>new Script(Profiles.Create(name,unchecked(game.Seed+97*(s+1))),game.Rounds.SelectMany(r=>r.Decisions).Where(d=>d.Seat==s),meter,counts,decisions)).ToArray();
                var replay=new Engine(game.Rules).Run(scripts,game.Seed,game.MaxRounds,game.InitialDealer);replay.Players=game.Players;
                if(scripts.Any(s=>!s.Complete)||JsonSerializer.Serialize(replay,LabJson.Options)!=JsonSerializer.Serialize(game,LabJson.Options))throw new InvalidDataException("Corpus replay diverged");
            }
            result[name]=meter.Summary();
        }
        using var file=new FileStream(output,FileMode.CreateNew);
        JsonSerializer.Serialize(file,new{Schema="same-observation-profile-v1",Games=corpus.Length,DecisionLimitPerPhase=decisions,InputSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),Profiles=result},new JsonSerializerOptions(LabJson.Options){WriteIndented=true});
        Console.WriteLine($"Profiled {names.Count} policies on the same observations; {decisions} decisions/phase maximum");
    }
    private sealed class Script(IPlayer candidate,IEnumerable<Decision> decisions,Telemetry meter,Dictionary<string,int> counts,int limit):IPlayer
    {
        private readonly Queue<Decision> queue=new(decisions);
        public string Name=>candidate.Name;public bool Complete=>queue.Count==0;
        private Decision Next(string phase){if(!queue.TryDequeue(out var d)||d.Phase!=phase)throw new InvalidDataException("Bad corpus decision sequence");return d;}
        private void Measure<T>(string phase,Func<T> call,Func<T,bool> legal)
        {
            int count=counts.GetValueOrDefault(phase);if(count>=limit)return;counts[phase]=count+1;
            long before=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();var action=call();double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            if(!legal(action))throw new IllegalActionException("Profiled candidate returned an illegal action");meter.Add(phase,ms,bytes,false);
        }
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal){Measure("bid",()=>candidate.Bid(v,legal),b=>legal.Contains(b));return Next("bid").Bid;}
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count){Measure("discard",()=>candidate.Discard(v,count),d=>d.Count==count&&d.Distinct().Count()==count&&d.All(v.Hand.Contains));return Next("discard").Discards!;}
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal){Measure("play",()=>candidate.Play(v,legal),p=>legal.Contains(p));return Next("play").Play!;}
    }
}
