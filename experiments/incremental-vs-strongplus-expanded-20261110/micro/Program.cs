using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using Bots = FiveHundredLab.Bots.FiveHundred;

string root = "experiments/incremental-vs-strongplus-expanded-20261110/results";var json = ArenaCommand.Json;string mode = args[0];
if (mode == "prepare")
{
    if (File.Exists(Path.Combine(root, "micro-cases.json"))) throw new InvalidOperationException("Already prepared");
    var cases = new List<Case>();var used = new HashSet<string>();
    using (var old = JsonDocument.Parse(File.ReadAllText("experiments/incremental-vs-strongplus-20261109/results/micro-cases.json")))
        foreach (var item in old.RootElement.EnumerateArray()) used.Add(Hash(item.GetProperty("View").Deserialize<Observation>(json)!));
    string[] buckets = ["bid", "discard", "early", "middle", "late", "forced"];
    using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "plan.json")));
    foreach (var p in plan.RootElement.EnumerateArray().Where(p => p.GetProperty("Group").GetString() == "replacement"))
    {
        int block = p.GetProperty("Id").GetInt32(), subject = p.GetProperty("SubjectSeat").GetInt32();
        var arms = block % 2 == 0 ? new[] { "incremental", "plus" } : new[] { "plus", "incremental" };
        foreach (string arm in arms)
        {
            string path = Path.Combine(root, "replacement", $"block-{block:D3}", arm + ".game.gz");
            using var f = File.OpenRead(path);using var zip = new GZipStream(f, CompressionMode.Decompress);var game = JsonSerializer.Deserialize<GameRecord>(zip, json)!;
            var captured = new List<Captured>();
            var players = Enumerable.Range(0, 4).Select(s => (IPlayer)new Capture(new ReplayPlayer(game.Players[s], game.Rounds.SelectMany(r => r.Decisions).Where(d => d.Seat == s)), s == subject ? captured : null)).ToArray();
            var replay = new Engine(game.Rules).Run(players, game.Seed, game.MaxRounds, game.InitialDealer);
            if (JsonSerializer.Serialize(replay, json) != JsonSerializer.Serialize(game, json)) throw new InvalidDataException("Source replay changed");
            foreach (string bucket in buckets)
            {
                if (cases.Count(c => c.Bucket == bucket && c.SourceArm == arm) >= 12) continue;
                bool Eligible(Captured c) => bucket switch
                {
                    "bid" => c.Phase == "bid",
                    "discard" => c.Phase == "discard" && c.DiscardCount > 0,
                    "early" => c.Phase == "play" && c.View.Hand.Count >= 8 && c.Plays.Length >= 5,
                    "middle" => c.Phase == "play" && c.View.Hand.Count is >= 4 and <= 7 && c.Plays.Length >= 3,
                    "late" => c.Phase == "play" && c.View.Hand.Count <= 3 && c.Plays.Length >= 2,
                    "forced" => c.Phase == "play" && c.Plays.Length == 1,
                    _ => false
                };
                var state = captured.FirstOrDefault(c => Eligible(c) && !used.Contains(Hash(c.View)));
                if (state == null) continue;
                string hash = Hash(state.View);used.Add(hash);
                cases.Add(new(cases.Count, "expanded-natural", block, arm, state.Phase, bucket, 330119001 + cases.Count * 7919, state.View, state.Bids, state.Plays, state.DiscardCount, hash));
            }
        }
        if (buckets.All(b => cases.Count(c => c.Bucket == b) == 24)) break;
    }
    if (cases.Count != 144 || cases.Select(c => c.PublicContextHash).Distinct().Count() != 144) throw new InvalidDataException("Missing or duplicate strata");
    IO.Write(Path.Combine(root, "micro-cases.json"), cases);
    IO.Write(Path.Combine(root, "micro-registration.json"), new { RegisteredBeforeTiming = true, Cases = 144, Repeats = 3, Runtime = Environment.Version.ToString(),
        Groups = cases.GroupBy(c => c.Bucket + "/" + c.SourceArm).ToDictionary(g => g.Key, g => g.Count()),
        Selection = "144 unique fresh public states;24 per bid/discard/early/middle/late/forced,12 from each strategy trajectory;first eligible per source match/bucket,alternate source priority;exclude previous46 and duplicates;no winner/action/timing selection",
        Method = "Serial after all new games;warm both per nonforced stratum;deterministically shuffle state order,alternate pair order;new bot instances and same seeds;decision-body time/allocation;3 repeats",
        Inference = "State-repeat medians, cost ratios clustered by originating game seed block;old/new natural timing not pooled;no overall-speedup extrapolation",
        InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "micro-cases.json")))) });
    Console.WriteLine("MICRO PREPARED 144 new unique states;3 repeats;balanced12 per source strategy in every stratum");
}
else if (mode == "bench")
{
    var cases = IO.Read<Case[]>(Path.Combine(root, "micro-cases.json"));
    foreach (string phase in new[] { "bid", "discard", "early", "middle", "late" })
    {
        var warm = cases.First(c => c.Bucket == phase) with { Seed = 232119000 };
        Measure(warm, false);Measure(warm, true);
    }
    int order = 0;var rows = new List<object>();
    var ordered = cases.ToList();new SeededRandom(331119999).Shuffle(ordered);
    foreach (var c in ordered)
    for (int repeat = 0; repeat < 3; repeat++)
    {
        Measurement a, b;
        if (order++ % 2 == 0) { a = Measure(c, false);b = Measure(c, true); } else { b = Measure(c, true);a = Measure(c, false); }
        rows.Add(new { c.Id, c.SourceBlock, c.SourceArm, c.Origin, c.Phase, c.Bucket, Repeat = repeat, HandCount = c.View.Hand.Count, LegalCount = c.Phase == "bid" ? c.Bids.Length : c.Phase == "play" ? c.Plays.Length : c.DiscardCount,
            Incremental = a, Plus = b, SameAction = a.Action == b.Action });
        Console.WriteLine($"MICRO {rows.Count}/{3 * cases.Length} {c.Origin}/{c.Phase} {c.Id}/{repeat} same={a.Action == b.Action} ms={a.Milliseconds:F2}/{b.Milliseconds:F2}");
    }
    IO.Write(Path.Combine(root, "micro-benchmark.json"), rows);
}
else throw new ArgumentException("prepare|bench");
Measurement Measure(Case c, bool plus)
{
    var a = plus ? null : new Bots.StrongPlayIncrementalBot(c.Seed);var b = plus ? new Bots.StrongPlusBot(c.Seed) : null;IPlayer bot = plus ? b! : a!;
    long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();object? action;
    if (c.Phase == "bid") action = bot.Bid(c.View, c.Bids);
    else if (c.Phase == "discard") action = bot.Discard(c.View, c.DiscardCount);
    else action = bot.Play(c.View, c.Plays);
    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    if (c.Phase == "bid" && !c.Bids.Contains((Contract?)action)) throw new InvalidDataException("Illegal bid");
    if (c.Phase == "discard") { var d = ((IReadOnlyList<PlayingCard>)action!).ToArray();if (d.Length != c.DiscardCount || d.Distinct().Count() != d.Length || d.Any(x => !c.View.Hand.Contains(x))) throw new InvalidDataException("Illegal discard"); }
    if (c.Phase == "play" && !c.Plays.Contains((PlayAction)action!)) throw new InvalidDataException("Illegal play");
    var search = plus ? b!.LastDefaultSearch : a!.LastDefaultSearch;int rollouts = plus ? (search?.Samples ?? 0) * (search?.Candidates ?? 0) : a!.LastRollouts;
    JsonElement? ux = null;
    if (c.Origin == "ux-misere")
    {
        var fixture = FiveHundredLab.Usability.UxEvaluationV1.Build().Single(f => f.Scenario.Id == c.Bucket);
        ux = JsonSerializer.SerializeToElement(fixture.Scenario.Checks.Select(check => new { check.Id, check.Level, check.Expectation, Passed = check.Accept(c.View, new FiveHundredLab.Usability.UxActionV1(Play: (PlayAction)action!)) }).ToArray(), json);
    }
    return new(JsonSerializer.Serialize(action, json), search, rollouts, ms, allocated, ux,
        !plus && c.Phase == "play" ? JsonSerializer.SerializeToElement(a!.LastTactics, json) : null,
        !plus && c.Phase == "discard" ? JsonSerializer.SerializeToElement(a!.LastDiscardGuard, json) : null);
}
string Hash(Observation view) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(view, json)));
record Case(int Id, string Origin, int SourceBlock, string SourceArm, string Phase, string Bucket, int Seed, Observation View, Contract?[] Bids, PlayAction[] Plays, int DiscardCount, string PublicContextHash);
record Captured(string Phase, Observation View, Contract?[] Bids, PlayAction[] Plays, int DiscardCount);
record Measurement(string Action, SearchReport? Search, int Rollouts, double Milliseconds, long AllocatedBytes, JsonElement? UxChecks, JsonElement? Tactics, JsonElement? Guard);
sealed class Capture(IPlayer inner, List<Captured>? captured) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => inner.Name;
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) { captured?.Add(new("bid", v, legal.ToArray(), [], 0));return inner.Bid(v, legal); }
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count) { captured?.Add(new("discard", v, [], [], count));return inner.Discard(v, count); }
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) { captured?.Add(new("play", v, [], legal.ToArray(), 0));return inner.Play(v, legal); }
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v) => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(v) : [];
}
static class IO
{
    public static readonly JsonSerializerOptions Json = ArenaCommand.Json;
    public static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)!;
}
