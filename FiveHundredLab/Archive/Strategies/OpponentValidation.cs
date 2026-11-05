using System.Diagnostics;
using System.Text.Json;
using FiveHundredArena;
namespace FiveHundredLab;

public static class OpponentValidation
{
    public static void Run(string output,int pairs,int seed,Rules rules,int maxRounds)
    {
        if(pairs<1||maxRounds<1)throw new ArgumentException("Positive limits required");
        Directory.CreateDirectory(output);
        using var log=new StreamWriter(new FileStream(Path.Combine(output,"games.jsonl"),FileMode.CreateNew));
        var rows=new List<object>();
        foreach(var mode in new[]{"FixedMedium","FixedMixture","Bidding","Belief","Play"})
        {
            var stage=mode=="Belief"?OpponentAdaptationStage.Belief:mode=="Play"?OpponentAdaptationStage.Play:OpponentAdaptationStage.Bidding;
            int wins=0,losses=0,truncated=0;var watch=Stopwatch.StartNew();
            for(int pair=0;pair<pairs;pair++)for(int leg=0;leg<2;leg++)
            {
                int gameSeed=unchecked(seed+pair*7919),rootSeat=leg;var bot=new OpponentStrong(gameSeed+97*(rootSeat+1),stage,rules:rules,learn:!mode.StartsWith("Fixed"),fixedMedium:mode=="FixedMedium");var timing=new DecisionTiming();
                var players=Enumerable.Range(0,4).Select(s=>s==rootSeat?(IPlayer)new TimedPlayer(bot,timing):OpponentStrong.CreateStyleOpponent(s==(rootSeat+2)%4?0:s==(rootSeat+1)%4?1:2,s==(rootSeat+1)%4?2:0)).ToArray();
                var game=new Engine(rules).Run(players,gameSeed,maxRounds,pair%4);ArenaCommand.Replay(game);log.WriteLine(JsonSerializer.Serialize(game,LabJson.Options));log.Flush();
                if(game.Truncated)truncated++;else if(game.Winner==rootSeat%2)wins++;else losses++;
                rows.Add(new{Stage=mode,pair,leg,gameSeed,rootSeat,game.Winner,game.Truncated,Deals=game.Rounds.Count,timing.Decisions,timing.Milliseconds,BidUpdates=bot.Beliefs.BidUpdates,PlayUpdates=bot.Beliefs.PlayUpdates,BidWeights=Enumerable.Range(0,4).Select(bot.Beliefs.BidWeights).ToArray(),PlayWeights=Enumerable.Range(0,4).Select(bot.Beliefs.PlayWeights).ToArray()});
            }
            Console.WriteLine($"{mode}: {wins}:{losses}, truncated={truncated}, {watch.Elapsed.TotalSeconds:F1}s");
        }
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{Schema="opponent-strong-check-v1",seed,pairs,maxRounds,Rules=rules,Explanation="Root plus conservative partner versus conventional/aggressive scripted opponents; duplicate deals exchange root team, not a statistical strength study. Default 8 samples/12 discard candidates.",Games=rows},new JsonSerializerOptions(LabJson.Options){WriteIndented=true}));
    }
}
