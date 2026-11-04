using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FiveHundredArena;
namespace FiveHundredLab.Usability;

public sealed record UxTrialV1(string Scenario,string Profile,int Seed,string HandOrder,UxActionV1? Action,bool Legal,string? Error,double Milliseconds,IReadOnlyDictionary<string,bool> Checks);
public sealed record UxRateV1(string Scenario,string Check,string Profile,UxLevelV1 Level,int Trials,int Passed,int Errors,IReadOnlyDictionary<string,int> Actions);
public static class UxEvaluationV1
{
    public static readonly string[] TestedProfiles=["random-legal-v1","weak-v1","medium-v1","visibility-fast-v8","strong-v1"];
    public static IReadOnlyList<CapturedUxV1> Build()=>UxFixturesV1.Scenarios().Select(UxCaptureV1.Capture).ToArray();
    public static UxTrialV1 Evaluate(CapturedUxV1 captured,string profile,int seed,bool reverse,Func<IPlayer>? custom=null)
    {
        var original=captured.View;
        var hand=(reverse?original.Hand.Reverse():original.Hand).ToArray();
        var v=original with{Hand=hand,Players=original.Players.Select(p=>p with{Bids=p.Bids.ToArray(),Taken=p.Taken.ToArray(),ExposedHand=p.ExposedHand?.ToArray()}).ToArray(),Auction=original.Auction.ToArray(),Trick=original.Trick.ToArray(),History=original.History.ToArray(),Scores=original.Scores.ToArray(),OwnDiscards=original.OwnDiscards.ToArray(),Rules=new Rules()};
        var player=custom?.Invoke()??Profiles.Create(profile,seed);var watch=Stopwatch.StartNew();
        UxActionV1? action=null;bool legal=false;string? error=null;
        try
        {
            switch(captured.Scenario.Phase)
            {
                case "bid": action=new(Bid:player.Bid(v,captured.LegalBids.ToArray()));legal=captured.LegalBids.Contains(action.Bid);break;
                case "discard":
                    var discards=player.Discard(v,captured.DiscardCount).ToArray();action=new(Discards:discards);
                    legal=discards.Length==captured.DiscardCount&&discards.Distinct().Count()==captured.DiscardCount&&discards.All(original.Hand.Contains);break;
                case "play":
                    var choices=v.Rules.LegalPlays(v.Hand,v.Trick,v.Contract!);var play=player.Play(v,choices.ToArray());action=new(Play:play);legal=captured.LegalPlays.Contains(play);break;
                default:throw new InvalidDataException("Unknown UX phase");
            }
            if(!legal)error="Returned an illegal action";
        }
        catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
        watch.Stop();
        var checks=captured.Scenario.Checks.ToDictionary(c=>c.Id,c=>legal&&action!=null&&c.Accept(original,action));
        return new(captured.Scenario.Id,profile,seed,reverse?"reversed":"canonical",action,legal,error,watch.Elapsed.TotalMilliseconds,checks);
    }
    public static string Describe(UxActionV1? action,string phase)
    {
        if(action==null)return "ERROR";
        if(phase=="bid")return action.Bid==null?"Pass":action.Bid.ToString();
        if(phase=="discard")return string.Join(" ",action.Discards!.OrderBy(c=>(int)c.Suit).ThenBy(c=>c.Rank));
        return action.Play!.Card+(action.Play.JokerLeadSuit==Trickster.cloud.Suit.Unknown?"":" -> "+action.Play.JokerLeadSuit);
    }
    public static void Run(string output,int trials=16,int seed=314159,IReadOnlyList<string>? profiles=null)
    {
        if(trials<2||trials>256||trials%2!=0)throw new ArgumentException("Use an even trial count between 2 and 256");
        var tested=profiles?.ToArray()??TestedProfiles.ToArray();
        if(tested.Length==0||tested.Length>16||tested.Distinct().Count()!=tested.Length)throw new ArgumentException("Use 1–16 distinct UX profiles");
        foreach(var profile in tested)Profiles.Create(profile,seed); // Reject invalid profiles before creating output.
        string resultPath=Path.Combine(output,"ux-results.json");
        if(File.Exists(resultPath)||File.Exists(Path.Combine(output,"trials.jsonl")))throw new IOException("UX output already exists");
        var scenarios=Build(); // Validate every script before evaluating or creating output.
        Directory.CreateDirectory(output);var results=new List<UxTrialV1>();
        using(var log=new StreamWriter(new FileStream(Path.Combine(output,"trials.jsonl"),FileMode.CreateNew)))
        for(int index=0;index<scenarios.Count;index++)
        {
            var scenario=scenarios[index];
            foreach(var profile in tested)
            for(int i=0;i<trials;i++)
            {
                int actionSeed=unchecked(seed+index*104729+(i/2)*7919);
                var row=Evaluate(scenario,profile,actionSeed,i%2!=0);results.Add(row);log.WriteLine(JsonSerializer.Serialize(row,LabJson.Options));
            }
            log.Flush();Console.WriteLine($"UX {index+1}/{scenarios.Count}: {scenario.Scenario.Id}");
        }
        var rates=new List<UxRateV1>();
        foreach(var scenario in scenarios)
        foreach(var check in scenario.Scenario.Checks)
        foreach(var profile in tested)
        {
            var rows=results.Where(r=>r.Scenario==scenario.Scenario.Id&&r.Profile==profile).ToArray();
            rates.Add(new(scenario.Scenario.Id,check.Id,profile,check.Level,rows.Length,rows.Count(r=>r.Checks[check.Id]),rows.Count(r=>r.Error!=null),rows.GroupBy(r=>Describe(r.Action,scenario.Scenario.Phase)).ToDictionary(g=>g.Key,g=>g.Count())));
        }
        var report=new{Schema="human-partner-ux-v1",Seed=seed,TrialsPerScenario=trials,HumanSeat=2,TestedSeat=0,Profiles=tested,
            Disclaimer="Scripted legal decision probes, not a human study or real-match error frequency; advisory checks depend on stated conventions; controls are excluded from experience rates.",
            Scenarios=scenarios.Select(c=>new{c.Scenario.Id,c.Scenario.Title,c.Scenario.Phase,c.Scenario.Convention,Checks=c.Scenario.Checks.Select(k=>new{k.Id,k.Expectation,k.Level}),Observation=c.View,LegalBids=c.LegalBids,LegalPlays=c.LegalPlays,DiscardCount=c.DiscardCount,InitialHandsForAuditOnly=c.InitialHands,KittyForAuditOnly=c.Kitty,ScriptedAuction=c.Scenario.Auction,ScriptedPlays=c.Scenario.Plays}),
            Results=rates,TechnicalErrors=results.Count(r=>r.Error!=null),
            Assemblies=new[]{typeof(UxEvaluationV1).Assembly,typeof(Engine).Assembly,typeof(Trickster.Bots.FiveHundredBot).Assembly}.ToDictionary(a=>a.GetName().Name!,a=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))))};
        using(var stream=new FileStream(resultPath,FileMode.CreateNew))JsonSerializer.Serialize(stream,report,new JsonSerializerOptions(LabJson.Options){WriteIndented=true});
        WriteMarkdown(Path.Combine(output,"REPORT.md"),scenarios,rates,results,tested);
        Console.WriteLine($"UX complete: {scenarios.Count} scenarios, {results.Count} decisions, {results.Count(r=>r.Error!=null)} technical errors. {resultPath}");
    }
    private static void WriteMarkdown(string path,IReadOnlyList<CapturedUxV1> scenarios,IReadOnlyList<UxRateV1> rates,IReadOnlyList<UxTrialV1> trials,IReadOnlyList<string> profiles)
    {
        var b=new StringBuilder("# 人类搭档体验代理测试 v1\n\n固定局面、多个搜索种子与两种手牌顺序；不是实战失误频率或真人满意度。Critical 是本测试定义的明确体验约束，Advisory 是战术/约定提示，Control 不参与体验合格率。\n\n| 场景 / 检查 | 级别 |");
        foreach(var profile in profiles)b.Append($" {profile} |");
        b.Append("\n|---|---|");foreach(var _ in profiles)b.Append("---:|");b.AppendLine();
        foreach(var c in scenarios)foreach(var check in c.Scenario.Checks)
        {
            b.Append($"| {c.Scenario.Title} / {check.Id} | {check.Level} |");
            foreach(var profile in profiles){var r=rates.Single(r=>r.Scenario==c.Scenario.Id&&r.Check==check.Id&&r.Profile==profile);b.Append($" {r.Passed}/{r.Trials} |");}b.AppendLine();
        }
        b.AppendLine("\n## 分层汇总\n\n| 策略 | 明确约束通过 | 战术/约定提示通过 | 控制场景通过 | 非法动作/异常 |\n|---|---:|---:|---:|---:|");
        foreach(var profile in profiles)
        {
            string Total(UxLevelV1 level){var r=rates.Where(x=>x.Profile==profile&&x.Level==level).ToArray();return $"{r.Sum(x=>x.Passed)}/{r.Sum(x=>x.Trials)}";}
            b.AppendLine($"| {profile} | {Total(UxLevelV1.Critical)} | {Total(UxLevelV1.Advisory)} | {Total(UxLevelV1.Control)} | {trials.Count(t=>t.Profile==profile&&t.Error!=null)} |");
        }
        b.AppendLine("\n全牌分配只供审计；被测玩家不会额外获知其他玩家的隐藏手牌、未见底牌、其他玩家弃牌或固定发牌规则。本人按规则取得的底牌和自己的弃牌正常可见。不同级别不合并成一个总分。对约定相关的不符合，不能直接认定为程序错误。动作分布和每次种子详见 JSON/JSONL。\n");
        File.WriteAllText(path,b.ToString());
    }
}
