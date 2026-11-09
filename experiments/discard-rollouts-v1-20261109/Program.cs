using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using Bots=FiveHundredLab.Bots.FiveHundred;

if(args.Length<2)throw new ArgumentException("root validate|run|smoke|verify [prior-results]");
string root=args[0],mode=args[1],prior=args.Length>2?args[2]:"experiments/guard-benefits-v1-20261109/results";
Directory.CreateDirectory(root);
string version=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DiscardStudy).Assembly.Location)));
string versionPath=Path.Combine(root,"runner-version.txt");
if(File.Exists(versionPath)&&File.ReadAllText(versionPath)!=version)throw new InvalidDataException("Runner changed; use a new output directory");
File.WriteAllText(versionPath,version);
string source=Path.Combine(prior,"selected-discards.json"),target=Path.Combine(root,"selected-discards.json");
var bytes=File.ReadAllBytes(source);
if(mode!="smoke"&&File.Exists(target)&&!File.ReadAllBytes(target).SequenceEqual(bytes))throw new InvalidDataException("Selected cases changed");
File.Copy(source,target,true);
var cases=IO.Read<DecisionCase[]>(target);
if(cases.Length!=128||cases.Select(c=>c.Template).Distinct().Count()!=94||cases.Any(c=>c.Template==0))throw new InvalidDataException("Unexpected source corpus");
switch(mode)
{
 case "validate":
  Parallel.ForEach(cases,new ParallelOptions{MaxDegreeOfParallelism=2},c=>
  {
   var bot=new Bots.StrongDiscardGuardBot(c.Seed);var actual=bot.Discard(DiscardStudy.View(c.Hand),3);
   if(!actual.ToHashSet().SetEquals(c.After)||!bot.LastDiscardGuard!.Original.ToHashSet().SetEquals(c.Before)||!bot.LastDiscardGuard.Changed)
    throw new InvalidDataException("Standalone repair differs from frozen decisions");
  });
  IO.Write(Path.Combine(root,"implementation-validation.json"),new{Cases=cases.Length,Templates=94,OriginalAndRepairedDiscardsUnchanged=true});
  Console.WriteLine("IMPLEMENTATION 128 decisions matched frozen original/repaired discards");break;
 case "run":DiscardStudy.Continue(root);break;
 case "smoke":IO.Write(target,cases.Take(1).ToArray());DiscardStudy.Continue(root,2);break;
 case "verify":
  int n=0;
  foreach(var f in Directory.GetFiles(Path.Combine(root,"continuations"),"*.game.gz",SearchOption.AllDirectories))
  {DiscardStudy.Replay(IO.ReadZip<FixtureArchive>(f));n++;}
  IO.Write(Path.Combine(root,"replay-validation.json"),new{NewConditionalDeals=n,Passed=true});
  Console.WriteLine($"REPLAY {n} new conditional deals");break;
 default:throw new ArgumentException(mode);
}
static class IO
{
 public static readonly JsonSerializerOptions Json=ArenaCommand.Json;
 public static void Write<T>(string path,T value){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value,Json));File.Move(path+".tmp",path,true);}
 public static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json)!;
 public static void Zip<T>(string path,T value){using var f=File.Create(path);using var z=new GZipStream(f,CompressionLevel.Fastest);JsonSerializer.Serialize(z,value,Json);}
 public static T ReadZip<T>(string path){using var f=File.OpenRead(path);using var z=new GZipStream(f,CompressionMode.Decompress);return JsonSerializer.Deserialize<T>(z,Json)!;}
}
