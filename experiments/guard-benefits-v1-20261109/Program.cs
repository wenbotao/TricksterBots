using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;

if(args.Length!=2)throw new ArgumentException("root matches|select|continue|verify");
string root=args[0],mode=args[1];Directory.CreateDirectory(root);
string version=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MatchStudy).Assembly.Location)));
string path=Path.Combine(root,"runner-version.txt");
if(File.Exists(path)&&File.ReadAllText(path)!=version)throw new InvalidDataException("Runner changed: use a new output directory");
File.WriteAllText(path,version);
switch(mode)
{
 case "matches":MatchStudy.Run(root);break;
 case "select":DiscardStudy.Select(root);break;
 case "continue":DiscardStudy.Continue(root);break;
 case "smoke":
  var hand=FiveHundredLab.Usability.UxFixturesV1.Cards("KS 5S AC QC 8C 8D 9D AH 8H 9H 4D 4H 5C");
  IO.Write(Path.Combine(root,"selected-discards.json"),new[]{new DecisionCase(0,0,71,hand,FiveHundredLab.Usability.UxFixturesV1.Cards("5S 4D 4H"),FiveHundredLab.Usability.UxFixturesV1.Cards("4D 4H 5C"),true)});
  DiscardStudy.Continue(root);break;
 case "verify":
  int n=0;
  foreach(var f in Directory.GetFiles(Path.Combine(root,"matches"),"*.game.gz",SearchOption.AllDirectories))
  {var g=IO.ReadZip<GameRecord>(f);ArenaCommand.Replay(g);n++;}
  int m=0;
  foreach(var f in Directory.GetFiles(Path.Combine(root,"continuations"),"*.game.gz",SearchOption.AllDirectories))
  {DiscardStudy.Replay(IO.ReadZip<FixtureArchive>(f));m++;}
  IO.Write(Path.Combine(root,"replay-validation.json"),new{Matches=n,ConditionalSingleDeals=m,Passed=true});
  Console.WriteLine($"REPLAY matches={n} conditional-deals={m}");break;
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
