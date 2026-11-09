using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;

string root=args[0],mode=args[1];Directory.CreateDirectory(root);
string version=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(RefinementStudy).Assembly.Location)));
string versionPath=Path.Combine(root,"runner-version.txt");
if(File.Exists(versionPath)&&File.ReadAllText(versionPath)!=version)throw new InvalidDataException("Runner version changed");
File.WriteAllText(versionPath,version);
if(mode=="prepare")RefinementStudy.Prepare(root);
else if(mode=="run")RefinementStudy.Run(root);
else if(mode=="verify")
{
 int n=0;foreach(var f in Directory.GetFiles(Path.Combine(root,"outcomes"),"*.game.gz",SearchOption.AllDirectories)){RefinementStudy.Replay(IO.ReadZip<Archive>(f));n++;}
 IO.Write(Path.Combine(root,"replay-validation.json"),new{PhysicalDeals=n,Passed=true});Console.WriteLine($"REPLAY {n}");
}
else throw new ArgumentException("prepare|run|verify");
static class IO
{
 public static readonly JsonSerializerOptions Json=ArenaCommand.Json;
 public static void Write<T>(string p,T value){Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p+".tmp",JsonSerializer.Serialize(value,Json));File.Move(p+".tmp",p,true);}
 public static T Read<T>(string p)=>JsonSerializer.Deserialize<T>(File.ReadAllText(p),Json)!;
 public static void Zip<T>(string p,T value){using var f=File.Create(p);using var z=new GZipStream(f,CompressionLevel.Fastest);JsonSerializer.Serialize(z,value,Json);}
 public static T ReadZip<T>(string p){using var f=File.OpenRead(p);using var z=new GZipStream(f,CompressionMode.Decompress);return JsonSerializer.Deserialize<T>(z,Json)!;}
}
