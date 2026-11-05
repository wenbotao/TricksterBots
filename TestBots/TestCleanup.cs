#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Security.Cryptography;
using FiveHundredArena;
using FiveHundredLab;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace TestBots;
[TestClass]
public class TestCleanup
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string p="")=>Path.GetDirectoryName(Path.GetDirectoryName(p))!;
    [TestMethod]
    public void RuleBotDecisionSnapshotsStayUnchanged()
    {
        var actual=new Dictionary<string,string>();
        foreach(var dir in new[]{"presets","custom","examples"})foreach(var file in Directory.GetFiles(Path.Combine(Root(),"FiveHundredRules",dir),"*.json"))foreach(var name in new[]{"random-legal-v1","weak-v1","medium-v1","visibility-fast-v8","strong-v1","partner-fast-v9"})
        {
            var r=RuleConfiguration.Load(file);var root=new Capture(Profiles.CreateForRules(name,31,r));
            var game=new Engine(r).Run([root,new Passing(32),new Passing(33),new Passing(34)],11731,1,3);
            var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{root.ActualBid,game.Rounds},ArenaCommand.Json));actual[Path.GetFileName(file)+":"+name]=Convert.ToHexString(SHA256.HashData(bytes));
        }
        var expected=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Root(),"TestBots/RuleBotDecisionSnapshots.json")))!;
        Assert.AreEqual(36,actual.Count);foreach(var pair in actual)Assert.AreEqual(expected[pair.Key],pair.Value,pair.Key);
    }
    private sealed class Capture(IPlayer inner):IPlayer,IJokerDeclarationPlayer
    {
        public Contract? ActualBid;public string Name=>inner.Name;
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal){ActualBid=inner.Bid(v,legal);return legal.First(c=>c?.Kind==ContractKind.Normal);}
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
        public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner is IJokerDeclarationPlayer d?d.DeclareJokers(v):[];
    }
    private sealed class Passing(int seed):IPlayer
    {
        private readonly RandomLegalBot inner=new(seed);public string Name=>"context";
        public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>null;
        public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
        public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
    }
}
