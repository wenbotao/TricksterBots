using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiveHundredArena;

namespace FiveHundredLab;

public static class LabJson
{
    public static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
}
public sealed record PhaseTiming(long Count, double MeanMs, double P50Ms, double P95Ms, double MeanBytes, long Changes);
public sealed class Telemetry
{
    private readonly Dictionary<string, List<(double Ms, long Bytes, bool Changed)>> phases = [];
    public void Add(string phase, double ms, long bytes, bool changed)
    { if (!phases.TryGetValue(phase, out var list)) phases[phase] = list = []; list.Add((ms, bytes, changed)); }
    public Dictionary<string, PhaseTiming> Summary() => phases.ToDictionary(p => p.Key, p =>
    {
        var a = p.Value.OrderBy(x => x.Ms).ToArray();
        return new PhaseTiming(a.Length, a.Average(x => x.Ms), a[(a.Length - 1) / 2].Ms, a[(int)Math.Ceiling(.95 * a.Length) - 1].Ms, a.Average(x => (double)x.Bytes), a.Count(x => x.Changed));
    });
}
internal sealed class MeteredPlayer(IPlayer inner, Telemetry meter, bool compare) : IPlayer, IJokerDeclarationPlayer
{
    private readonly MediumBotPlayer reference = new();
    public string Name => inner.Name;
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view)=>inner is IJokerDeclarationPlayer p?p.DeclareJokers(view):[];
    private T Measure<T>(string phase, Func<T> call, Func<T> baseline, Func<T, T, bool> equals)
    {
        long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        T action = call(); double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        bool changed = compare && !equals(action, baseline()); // outside the measured decision
        meter.Add(phase, ms, bytes, changed); return action;
    }
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => Measure("bid", () => inner.Bid(v, legal), () => reference.Bid(v, legal), (a, b) => a == b);
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => Measure("discard", () => inner.Discard(v, count), () => reference.Discard(v, count), (a, b) => a.ToHashSet().SetEquals(b));
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => Measure("play", () => inner.Play(v, legal), () => reference.Play(v, legal), (a, b) => a == b);
}
public sealed class StageAblationV1(int seed, string phase) : IPlayer
{
    private readonly StrongBot strong = new(seed);
    private readonly MediumBotPlayer medium = new();
    public string Name => "ablation-strong1-" + phase;
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => (phase == "bid" ? (IPlayer)strong : medium).Bid(v, legal);
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => (phase == "discard" ? (IPlayer)strong : medium).Discard(v, count);
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => (phase == "play" ? (IPlayer)strong : medium).Play(v, legal);
}
public static class LabRunner
{
    public static object Bench(string a, string b, int pairs, int seed, int maxRounds, string output, Func<string, int, IPlayer> factory, bool compare = true, bool mixed = false, Rules? rules = null)
    {
        rules ??= new Rules();
        if (pairs < 1 || maxRounds < 1) throw new ArgumentException("Positive pairs and round cap required");
        Directory.CreateDirectory(output);
        using var log = new StreamWriter(new FileStream(Path.Combine(output, "games.jsonl"), FileMode.CreateNew));
        // Warm JIT on independent, unmeasured deals for both sides.
        new Engine(rules).Run([factory(a, 7), factory(b, 8), factory(a, 9), factory(b, 10)], -987654, 1);
        var ma = new Telemetry(); var mb = new Telemetry(); var partnerMeter = new Telemetry(); var pairScores = new List<double>();
        int aw = 0, bw = 0, truncated = 0, deals = 0; var watch = Stopwatch.StartNew();
        for (int pair = 0; pair < pairs; pair++)
        {
            double wins = 0; bool complete = true;
            for (int leg = 0; leg < 2; leg++)
            {
                int gameSeed = unchecked(seed + pair * 7919);
                var players = Enumerable.Range(0, 4).Select(s =>
                {
                    bool sideA=s%2==leg, partner=mixed&&sideA&&s!=(pair%2)*2+leg;
                    IPlayer player=partner?(rules is ConfiguredRules ? Profiles.CreateForRules("medium-v1",unchecked(gameSeed+97*(s+1)),rules) : new MediumBotPlayer()):factory(sideA?a:b,unchecked(gameSeed+97*(s+1)));
                    return (IPlayer)new MeteredPlayer(player,partner?partnerMeter:sideA?ma:mb,compare && rules is not ConfiguredRules);
                }).ToArray();
                GameRecord game;
                try { game = new Engine(rules).Run(players, gameSeed, maxRounds, pair % 4); }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new { pair, leg, gameSeed, Error = ex.ToString(), Partial = ex.Data["record"] as GameRecord }, LabJson.Options)); throw;
                }
                log.WriteLine(JsonSerializer.Serialize(game, LabJson.Options)); log.Flush(); deals += game.Rounds.Count;
                if (game.Truncated) { truncated++; complete = false; }
                else if (game.Winner == leg) { aw++; wins++; } else bw++;
            }
            if (complete) pairScores.Add(wins / 2);
        }
        double? rate = pairScores.Count == 0 ? null : pairScores.Average();
        double? se = pairScores.Count < 2 ? null : Math.Sqrt(pairScores.Sum(x => Math.Pow(x - rate!.Value, 2)) / (pairScores.Count - 1) / pairScores.Count);
        var result = new { A = a, B = b, Seed = seed, Pairs = pairs, Games = 2 * pairs, AWins = aw, BWins = bw, Truncated = truncated, Deals = deals, CompletedPairs = pairScores.Count, PairedWinRate = rate, PairStandardError = se, ElapsedSeconds = watch.Elapsed.TotalSeconds, Rules = rules,
            MixedPartner = mixed, TimingA = ma.Summary(), TimingB = mb.Summary(), TimingAPartner = partnerMeter.Summary(), Runtime = Environment.Version.ToString(),
            AssemblySha256 = new[] { typeof(LabRunner).Assembly, typeof(Engine).Assembly, typeof(Trickster.Bots.FiveHundredBot).Assembly }.ToDictionary(x => x.GetName().Name!, x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Location)))) };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions(LabJson.Options) { WriteIndented = true }));
        Console.WriteLine($"{a} vs {b}: {aw}:{bw}, truncated={truncated}, pairs={pairs}, {watch.Elapsed.TotalSeconds:F1}s"); return result;
    }
}
public static class LabCommand
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "help")
            {
                Console.WriteLine("FiveHundredLab bench|bench-mixed --a PROFILE --b PROFILE --pairs N --seed N --out DIR\nFiveHundredLab profile --input games.jsonl --profiles strong-v1,visibility-fast-v6,visibility-fast-v8 --games 1 --decisions 16 --out timings.json\nFiveHundredLab ux --trials 16 --seed 314159 --out DIR\nFiveHundredLab ablate --pairs N --seed N --out DIR\nFiveHundredLab verify-frozen --root REPO\nRecommended: visibility-fast-v8 (repaired speed); visibility-fast-v6 remains frozen, visibility-quality-v7 (quality), visibility-strong-v5 (original decisions). Pretraining is deferred; see README.");
                return 0;
            }
            var opts = new Dictionary<string, string>();
            string[] known = ["--root","--seed","--pairs","--out","--a","--b","--max-rounds","--games","--model","--input","--profiles","--decisions","--trials","--rules"];
            for (int i = 1; i < args.Length; i += 2) { if (i + 1 >= args.Length || !known.Contains(args[i]) || !opts.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Expected unique --key value pairs"); }
            string[] permitted=args[0] switch
            {
                "bench" or "bench-mixed" => ["--a","--b","--pairs","--seed","--out","--max-rounds","--model","--rules"],
                "adapt-learning-check" => ["--out","--games","--seed"],
                "adapt-check" => ["--out","--games","--seed"],
                "opponent-check" => ["--out","--seed","--pairs","--rules","--max-rounds"],
                "ux" => ["--out","--trials","--seed","--profiles"],
                "profile" => ["--input","--profiles","--games","--decisions","--out"],
                "ablate" => ["--pairs","--seed","--out"],
                "verify-frozen" => ["--root"],
                "train" or "train-phase-v2" => ["--games","--seed","--out"],
                _ => throw new ArgumentException("Unknown command")
            };
            if(opts.Keys.Any(k=>!permitted.Contains(k)))throw new ArgumentException("Option does not apply to this command");
            string Get(string key, string fallback) => opts.GetValueOrDefault(key, fallback);
            if (args[0] == "verify-frozen")
            {
                string root = Path.GetFullPath(Get("--root", "."));
                var manifests = new[] { Path.Combine(AppContext.BaseDirectory,"frozen-baselines-v1.json") }.Concat(Directory.GetFiles(AppContext.BaseDirectory,"frozen-profiles-*.json"));
                int count=0;
                foreach(var manifest in manifests)
                {
                    var entries=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest))!;
                    foreach(var e in entries)
                    {
                        if(FrozenSourceHashV1.Compute(Path.Combine(root,e.Key))!=e.Value)throw new InvalidDataException($"Frozen source changed: {e.Key}");count++;
                    }
                }
                Console.WriteLine($"Verified {count} frozen source/model files"); return 0;
            }
            if(args[0]=="profile")
            {
                string input=opts.GetValueOrDefault("--input")??throw new ArgumentException("profile requires --input");
                CorpusProfilerV1.Run(input,Get("--out","timings.json"),Get("--profiles","strong-v1,visibility-fast-v6,visibility-fast-v8").Split(','),int.Parse(Get("--games","1")),int.Parse(Get("--decisions","16")));return 0;
            }
            if(args[0]=="adapt-learning-check")
            {AdaptLearningComparison.Run(Get("--out","adapt-learning-results"),int.Parse(Get("--games","60")),int.Parse(Get("--seed","2811001")));return 0;}
            if(args[0]=="adapt-check")
            {AdaptComparison.Run(Get("--out","adapt-results"),int.Parse(Get("--games","20")),int.Parse(Get("--seed","2711001")));return 0;}
            if(args[0]=="opponent-check")
            {OpponentValidation.Run(Get("--out","opponent-results"),int.Parse(Get("--pairs","1")),int.Parse(Get("--seed","2707001")),RuleConfiguration.Load(opts.GetValueOrDefault("--rules")),int.Parse(Get("--max-rounds","60")));return 0;}
            if(args[0]=="ux")
            {
                Usability.UxEvaluationV1.Run(Get("--out","ux-results"),int.Parse(Get("--trials","16")),int.Parse(Get("--seed","314159")),opts.TryGetValue("--profiles",out var uxProfiles)?uxProfiles.Split(','):null);return 0;
            }
            int seed = int.Parse(Get("--seed", "601000")), pairs = int.Parse(Get("--pairs", "2"));
            string output = Get("--out", "lab-results");
            var selectedRules=RuleConfiguration.Load(opts.GetValueOrDefault("--rules"));
            IPlayer Factory(string name, int s) => opts.ContainsKey("--rules")?Profiles.CreateForRules(name,s,selectedRules):Profiles.Create(name, s, opts.GetValueOrDefault("--model"));
            if (args[0] is "bench" or "bench-mixed") LabRunner.Bench(Get("--a", "visibility-fast-v8"), Get("--b", "medium-v1"), pairs, seed, int.Parse(Get("--max-rounds", "200")), output, Factory, mixed:args[0]=="bench-mixed",rules:selectedRules);
            else if (args[0] == "ablate")
                foreach (var phase in new[] { "bid", "discard", "play", "all" })
                    LabRunner.Bench("ablation-" + phase, "medium-v1", pairs, seed, 200, Path.Combine(output, phase), (n, s) => n == "medium-v1" ? new MediumBotPlayer() : phase == "all" ? new StrongBot(s) : new StageAblationV1(s, phase));
            else if (args[0] == "train-phase-v2") TrainerPhaseV2.Train(output,int.Parse(Get("--games","32")),seed);
            else if (args[0] == "train") TrainerV1.Train(output, int.Parse(Get("--games", "100")), seed);
            else throw new ArgumentException("Unknown command");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
