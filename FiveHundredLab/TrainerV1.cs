using System.Text.Json;
using FiveHundredArena;
namespace FiveHundredLab;

public static class TrainerV1
{
    // Teacher labels use hypothetical deals drawn from each player's own observation, never actual hidden hands.
    public static void Train(string path, int games, int seed)
    {
        if(games<5)throw new ArgumentException("At least five independent training games required");
        if(File.Exists(path)||File.Exists(path+".rows.jsonl"))throw new IOException("Training output already exists");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var rowsFile=new StreamWriter(new FileStream(path+".rows.jsonl",FileMode.CreateNew));
        var rows=new List<TrainingRowV1>();
        for(int game=0;game<games;game++)
        {
            int groupSeed=unchecked(seed+game*7919);var observed=new List<(Observation V,string Phase)>();
            IPlayer[] players=Enumerable.Range(0,4).Select(s=>(IPlayer)new Collector(ContinuationPoolV1.Create((OpponentPolicyV1)(game%3),groupSeed+s),observed)).ToArray();
            new Engine(new()).Run(players,groupSeed,12,game%4);
            var random=new SeededRandom(unchecked(groupSeed^0x517cc1b7));
            foreach(var (v,phase) in observed)
            {
                var worlds=BeliefSampler.Sample(v,16,random);
                if(phase=="bid")
                {
                    var legal=v.Rules.LegalBids(v.Contract,v.Auction);var chosen=new CompactMediumV1().Bid(v,legal);
                    foreach(var bid in new Contract?[]{chosen,null}.Distinct())
                    {
                        double value=0,weight=0;
                        foreach(var w in worlds){value+=w.Weight*new CompactForwardV1(v,w).Bid(bid);weight+=w.Weight;}
                        Add(new(groupSeed,phase,FeaturesV1.Extract(v,phase,bid),value/weight,0,0));
                    }
                }
                else
                {
                    double value=0,made=0,tricks=0,weight=0;var policy=new TacticianV1();
                    var discard=phase=="discard"?policy.Discard(v,3):null;
                    var play=phase=="play"?policy.Play(v,v.Rules.LegalPlays(v.Hand,v.Trick,v.Contract!)):null;
                    for(int i=0;i<worlds.Count;i++)
                    {
                        var w=worlds[i];var model=new ShortForwardV1(v,w,99,null,groupSeed+i,ContinuationPoolV1.Infer(v,i));
                        var result=discard!=null?model.Discard(discard):model.Play(play!);
                        value+=w.Weight*result.Utility;made+=w.Weight*result.MadeProbability;tricks+=w.Weight*result.DeclarerTricks;weight+=w.Weight;
                    }
                    Add(new(groupSeed,phase,FeaturesV1.Extract(v,phase),value/weight,made/weight,tricks/weight));
                }
            }
            Console.WriteLine($"Teacher game {game+1}/{games}: {rows.Count} labeled observations");
            void Add(TrainingRowV1 row){rows.Add(row);rowsFile.WriteLine(JsonSerializer.Serialize(row,LabJson.Options));rowsFile.Flush();}
        }
        var fitted=ValueModelV1.Fit($"ridge-teacher16-v1-seed{seed}-g{games}",rows);
        using(var file=new FileStream(path,FileMode.CreateNew))JsonSerializer.Serialize(file,fitted,new JsonSerializerOptions(LabJson.Options){WriteIndented=true});
        Console.WriteLine(JsonSerializer.Serialize(fitted.Metrics));
    }
    private sealed class Collector(IPlayer inner,List<(Observation,string)> output):IPlayer
    {
        private readonly HashSet<(int,string,int)> captured=[];
        public string Name=>"collector-v1/"+inner.Name;
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)
        {if(captured.Add((v.Round,"bid",0)) && v.Seat==0 && v.Round<2)output.Add((v,"bid"));return inner.Bid(v,legal);}
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)
        {if(v.Round<5)output.Add((v,"discard"));return inner.Discard(v,count);}
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)
        {
            int completed=v.Players.Sum(p=>p.Tricks);
            if(v.Round<5 && v.Trick.Count==0 && completed is 1 or 4 or 7 && captured.Add((v.Round,"play",completed)))output.Add((v,"play"));
            return inner.Play(v,legal);
        }
    }
}
