using System.Text.Json;
using FiveHundredArena;
namespace FiveHundredLab;

public sealed record AdaptMatchPlan(int Index,int Seed,int RootSeat,int Dealer,string[] OtherProfiles,int[] PlayerSeeds);
public sealed record AdaptMatchResult(int Index,string Bot,bool? Won,bool Truncated,int Deals,int TeamScore,int OpponentScore,Dictionary<string,PhaseTiming> Timing,int BidUpdates,int PlayUpdates);
public static class AdaptComparison
{
    public static readonly string[] Pool=["random-legal-v1","weak-v1","medium-v1","visibility-fast-v8","strong-v1","partner-fast-v9"];
    public static AdaptMatchPlan[] Plan(int games,int seed)
    {
        if(games<1||games>1000)throw new ArgumentException("games must be 1..1000");var rng=new SeededRandom(seed);
        return Enumerable.Range(0,games).Select(i=>new AdaptMatchPlan(i,unchecked(seed+1000003+i*7919),i%4,(i/4)%4,Enumerable.Range(0,4).Select(s=>s==i%4?"ROOT":Pool[rng.Next(Pool.Length)]).ToArray(),Enumerable.Range(0,4).Select(s=>unchecked(seed+i*7919+97*(s+1))).ToArray())).ToArray();
    }
    public static void Run(string output,int games=20,int seed=2711001)
    {
        Directory.CreateDirectory(output);var plan=Plan(games,seed);
        if(Directory.EnumerateFiles(output).Any())throw new IOException("Use an empty output directory");
        File.WriteAllText(Path.Combine(output,"plan.json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions(LabJson.Options){WriteIndented=true}));
        // Independent unmeasured deals warm both root implementations.
        foreach(bool adaptive in new[]{true,false})new Engine().Run([adaptive?new Adapt(7):new StrongBot(7),new MediumBotPlayer(),new MediumBotPlayer(),new MediumBotPlayer()],-2711001,1);
        var rows=new List<AdaptMatchResult>();
        using var log=new StreamWriter(new FileStream(Path.Combine(output,"games.jsonl"),FileMode.CreateNew));
        foreach(var item in plan)
        {
            var records=new List<GameRecord>();
            // Alternate order to reduce JIT/load ordering bias; every game gets fresh players.
            foreach(bool adaptive in item.Index%2==0?new[]{true,false}:new[]{false,true})
            {
                var adapt=adaptive?new Adapt(item.PlayerSeeds[item.RootSeat]):null;var meter=new Telemetry();
                var players=Enumerable.Range(0,4).Select(s=>s==item.RootSeat?(IPlayer)new MeteredPlayer(adaptive?adapt!:new StrongBot(item.PlayerSeeds[s]),meter,false):Profiles.Create(item.OtherProfiles[s],item.PlayerSeeds[s])).ToArray();
                GameRecord game;
                try{game=new Engine().Run(players,item.Seed,200,item.Dealer);ArenaCommand.Replay(game);}
                catch(Exception ex){File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new{item,adaptive,Error=ex.ToString(),Partial=ex.Data["record"] as GameRecord},LabJson.Options));throw;}
                records.Add(game);log.WriteLine(JsonSerializer.Serialize(game,LabJson.Options));log.Flush();
                int team=item.RootSeat%2;var score=game.Rounds.Last().Scores;
                rows.Add(new(item.Index,adaptive?"Adapt":"Strong",game.Truncated?null:game.Winner==team,game.Truncated,game.Rounds.Count,score[team],score[1-team],meter.Summary(),adapt?.Beliefs.BidUpdates??0,adapt?.Beliefs.PlayUpdates??0));
            }
            // Same seed must yield identical initial hands and kitty in every common deal,
            // even if the two trajectories terminate after different numbers of deals.
            for(int i=0;i<Math.Min(records[0].Rounds.Count,records[1].Rounds.Count);i++)
            {
                var a=records[0].Rounds[i];var b=records[1].Rounds[i];
                if(JsonSerializer.Serialize(a.Hands,LabJson.Options)!=JsonSerializer.Serialize(b.Hands,LabJson.Options)||!a.Kitty.SequenceEqual(b.Kitty))throw new InvalidDataException("Paired deals diverged");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions(LabJson.Options){WriteIndented=true}));
            var pair=rows.Where(r=>r.Index==item.Index).ToArray();Console.WriteLine($"{item.Index+1}/{games}: "+string.Join(", ",pair.Select(r=>$"{r.Bot} {(r.Won==true?"win":r.Won==false?"loss":"truncated")} {r.TeamScore}:{r.OpponentScore}")));
        }
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{Schema="adapt-strong-paired-v1",seed,games,Rules=new Rules(),Pool,Explanation="Other seats sampled independently with replacement; same roster, actor seeds and deal schedule per pair. Fresh Adapt memory per game. Common deal prefixes verified; all games replayed. Strong is original search Strong, not rule-aware heuristic bidding.",Results=rows},new JsonSerializerOptions(LabJson.Options){WriteIndented=true}));
    }
}
