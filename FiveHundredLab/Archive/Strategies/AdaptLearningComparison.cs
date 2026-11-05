using System.Text.Json;
using FiveHundredArena;
namespace FiveHundredLab;

public static class AdaptLearningComparison
{
    public static string[] Pool=>AdaptComparison.Pool;
    public static AdaptMatchPlan[] Plan(int games,int seed)=>AdaptComparison.Plan(games,seed);
    private static IPlayer Make(string name,int seed)=>name=="Adapt-v2"?new AdaptV2(seed):name=="Adapt"?new Adapt(seed):new StrongBot(seed);
    public static void Run(string output,int games=60,int seed=2811001)
    {
        Directory.CreateDirectory(output);var plan=Plan(games,seed);
        if(Directory.EnumerateFiles(output).Any())throw new IOException("Use an empty output directory");
        File.WriteAllText(Path.Combine(output,"plan.json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions(LabJson.Options){WriteIndented=true}));
        // Independent unmeasured deals warm both root implementations.
        foreach(var name in new[]{"Adapt-v2","Adapt","Strong"})new Engine().Run([Make(name,7),new MediumBotPlayer(),new MediumBotPlayer(),new MediumBotPlayer()],-2711001,1);
        var rows=new List<AdaptMatchResult>();
        using var log=new StreamWriter(new FileStream(Path.Combine(output,"games.jsonl"),FileMode.CreateNew));
        foreach(var item in plan)
        {
            var records=new List<GameRecord>();
            // Alternate order to reduce JIT/load ordering bias; every game gets fresh players.
            foreach(var name in Enumerable.Range(0,3).Select(k=>new[]{"Adapt-v2","Adapt","Strong"}[(item.Index+k)%3]))
            {
                var root=Make(name,item.PlayerSeeds[item.RootSeat]);var meter=new Telemetry();
                var players=Enumerable.Range(0,4).Select(s=>s==item.RootSeat?(IPlayer)new MeteredPlayer(root,meter,false):Profiles.Create(item.OtherProfiles[s],item.PlayerSeeds[s])).ToArray();
                GameRecord game;
                try{game=new Engine().Run(players,item.Seed,200,item.Dealer);ArenaCommand.Replay(game);}
                catch(Exception ex){File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new{item,name,Error=ex.ToString(),Partial=ex.Data["record"] as GameRecord},LabJson.Options));throw;}
                records.Add(game);log.WriteLine(JsonSerializer.Serialize(game,LabJson.Options));log.Flush();
                int team=item.RootSeat%2;var score=game.Rounds.Last().Scores;
                rows.Add(new(item.Index,name,game.Truncated?null:game.Winner==team,game.Truncated,game.Rounds.Count,score[team],score[1-team],meter.Summary(),root is AdaptV2 v2?v2.Beliefs.BidUpdates:root is Adapt v1?v1.Beliefs.BidUpdates:0,root is AdaptV2 p2?p2.Beliefs.PlayUpdates:root is Adapt p1?p1.Beliefs.PlayUpdates:0));
            }
            // Same seed must yield identical initial hands and kitty in every common deal,
            // even if the two trajectories terminate after different numbers of deals.
            for(int other=1;other<3;other++)for(int i=0;i<Math.Min(records[0].Rounds.Count,records[other].Rounds.Count);i++)
            {
                var a=records[0].Rounds[i];var b=records[other].Rounds[i];
                if(JsonSerializer.Serialize(a.Hands,LabJson.Options)!=JsonSerializer.Serialize(b.Hands,LabJson.Options)||!a.Kitty.SequenceEqual(b.Kitty))throw new InvalidDataException("Paired deals diverged");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions(LabJson.Options){WriteIndented=true}));
            var pair=rows.Where(r=>r.Index==item.Index).ToArray();Console.WriteLine($"{item.Index+1}/{games}: "+string.Join(", ",pair.Select(r=>$"{r.Bot} {(r.Won==true?"win":r.Won==false?"loss":"truncated")} {r.TeamScore}:{r.OpponentScore}")));
        }
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{Schema="adapt-learning-threeway-v1",seed,games,Rules=new Rules(),Pool,Explanation="Other seats sampled independently with replacement; same roster, actor seeds and deal schedule per pair. Fresh Adapt memory per game. Common deal prefixes verified; all games replayed. Strong is original search Strong, not rule-aware heuristic bidding.",Results=rows},new JsonSerializerOptions(LabJson.Options){WriteIndented=true}));
    }
}
