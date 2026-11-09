using System.Diagnostics;
using System.Text.Json;
using FiveHundredArena;
using Bots=FiveHundredLab.Bots.FiveHundred;

string root=args[0];var json=ArenaCommand.Json;
using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"selected-hands.json")));
var rows=new List<object>();int i=0;
foreach(var h in document.RootElement.EnumerateArray().Take(8))
{
 var hand=h.GetProperty("Hand").Deserialize<PlayingCard[]>(json)!;int seed=h.GetProperty("Seed").GetInt32();
 var contract=new Contract(6,Trickster.cloud.Suit.Unknown);
 var view=new Observation(0,3,0,hand,Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,s==0?13:10,0,s==0?new Contract?[]{contract}:new Contract?[]{null},0,[],null)).ToArray(),[new(0,contract),new(1,null),new(2,null),new(3,null)],contract,0,[],[],[0,0],new Rules(),[]);
 var reference=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.Shape,seed);
 var cached=new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientShape,seed);
 PlayingCard[] a=[],b=[];double ams=0,bms=0;
 void A(){var w=Stopwatch.StartNew();a=reference.Discard(view,3).ToArray();w.Stop();ams=w.Elapsed.TotalMilliseconds;}
 void B(){var w=Stopwatch.StartNew();b=cached.Discard(view,3).ToArray();w.Stop();bms=w.Elapsed.TotalMilliseconds;}
 if(i++%2==0){A();B();}else{B();A();}
 if(!a.SequenceEqual(b)||reference.LastDefaultSearch!=cached.LastDefaultSearch||reference.LastRollouts!=cached.LastRollouts)throw new InvalidDataException("Cache changed decisions or search");
 rows.Add(new{Hand=h.GetProperty("Id").GetInt32(),ReferenceMilliseconds=ams,CachedMilliseconds=bms,Reference=reference.LastWork,Cached=cached.LastWork,Rollouts=cached.LastRollouts,SameDecision=true});
}
File.WriteAllText(Path.Combine(root,"engineering-benchmark.json"),JsonSerializer.Serialize(rows,json));Console.WriteLine($"BENCH {rows.Count} decisions/searches identical; alternating execution order");
