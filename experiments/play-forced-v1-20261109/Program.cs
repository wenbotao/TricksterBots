using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using Bots = FiveHundredLab.Bots.FiveHundred;

string root = "experiments/play-forced-v1-20261109/results";Directory.CreateDirectory(root);
string mode = args[0];var json = ArenaCommand.Json;
if (mode == "prepare")
{
    if (File.Exists(Path.Combine(root, "cases.json"))) throw new InvalidOperationException("Prepared already");
    var buckets = new[] { "early", "middle", "late", "forced" };var cases = new List<Case>();
    using var plan = JsonDocument.Parse(File.ReadAllText("experiments/natural-matches-v1-20261109/results/plan.json"));
    foreach (var block in plan.RootElement.EnumerateArray())
    {
        int id = block.GetProperty("Id").GetInt32(), subject = block.GetProperty("SubjectSeat").GetInt32();
        string path = $"experiments/natural-matches-v1-20261109/results/matches/block-{id:D3}/lean.game.gz";
        using var file = File.OpenRead(path);using var zip = new GZipStream(file, CompressionMode.Decompress);var game = JsonSerializer.Deserialize<GameRecord>(zip, json)!;
        var views = new List<(Observation View, PlayAction[] Legal)>();
        var players = Enumerable.Range(0, 4).Select(s => (IPlayer)new Capture(new ReplayPlayer(game.Players[s], game.Rounds.SelectMany(r => r.Decisions).Where(d => d.Seat == s)), s == subject ? views : null)).ToArray();
        var replay = new Engine(game.Rules).Run(players, game.Seed, game.MaxRounds, game.InitialDealer);
        if (JsonSerializer.Serialize(replay, json) != JsonSerializer.Serialize(game, json)) throw new InvalidDataException("Source replay changed");
        foreach (string bucket in buckets)
        {
            if (cases.Count(c => c.Bucket == bucket) == 6) continue;
            var eligible = views.Where(x => bucket switch
            {
                "early" => x.View.Hand.Count >= 8 && x.Legal.Length >= 5,
                "middle" => x.View.Hand.Count is >= 4 and <= 7 && x.Legal.Length >= 3,
                "late" => x.View.Hand.Count <= 3 && x.Legal.Length >= 2,
                "forced" => x.Legal.Length == 1,
                _ => false
            }).ToArray();
            if (eligible.Length > 0) cases.Add(new(cases.Count, id, bucket, 191119001 + id * 7919, eligible[0].View, eligible[0].Legal));
        }
        if (buckets.All(b => cases.Count(c => c.Bucket == b) == 6)) break;
    }
    if (cases.Count != 24) throw new InvalidDataException("Missing strata");
    Save("cases.json", cases);
    Save("registration.json", new { Cases = 24, PerBucket = 6, Buckets = buckets, Repeats = 3, SeedBase = 191119001,
        Selection = "First eligible state per source match/bucket; first6 eligible source matches; no timing or outcome selection; replay natural saved games through engine to recover observations",
        Benchmark = "Serial after full tests; warm both on independent unmeasured state; alternate A/B pair order; new instances, same seed/view/actions; measure decision body and current-thread allocation, no forced GC",
        Limits = "Stratified small regression/performance sample, no natural weighted latency or win-rate estimate; source matches have normal suit/NT only; Misere covered by direct unit traces",
        SourceHashes = new[] { "FiveHundredLab/Bots/FiveHundred/StrongDiscardLeanBot.cs", "FiveHundredLab/Bots/FiveHundred/StrongPlayForcedBot.cs", "FiveHundredLab/Bots/FiveHundred/ForcedPlayRollout.cs", "FiveHundredLab/Bots/FiveHundred/ForcedConfiguredPlayRollout.cs" }.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))) });
    Console.WriteLine("PREPARED 24 observations;6 per bucket;no outcome/timing selection");
}
else if (mode == "bench")
{
    var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText(Path.Combine(root, "cases.json")), json)!;
    // Both implementations warmed on the same independent UX fixture; it is not a measured case.
    var warm = FiveHundredLab.Usability.UxEvaluationV1.Build().First(x => x.Scenario.Phase == "play" && x.LegalPlays.Count > 1);
    var warmCase = new Case(-1, -1, "warm", 191119000, warm.View, warm.LegalPlays.ToArray());
    Measure(warmCase, false);Measure(warmCase, true);
    var rows = new List<object>();int order = 0;
    foreach (var c in cases)
    for (int repeat = 0; repeat < 3; repeat++)
    {
        Measurement a, b;
        if (order++ % 2 == 0) { a = Measure(c, false);b = Measure(c, true); }
        else { b = Measure(c, true);a = Measure(c, false); }
        if (a.Action != b.Action || a.Search != b.Search || a.Rollouts != b.Rollouts) throw new InvalidDataException("Decision/search changed");
        if (b.Work!.Evaluations != b.Rollouts) throw new InvalidDataException("Counter mismatch");
        rows.Add(new { c.Id, c.SourceBlock, c.Bucket, Repeat = repeat, LegalCount = c.Legal.Length, HandCount = c.View.Hand.Count, Reference = a, Optimized = b, SameActionAndSearch = true });
        Console.WriteLine($"BENCH {rows.Count}/72 {c.Bucket} case={c.Id} repeat={repeat} ms={a.Milliseconds:F2}/{b.Milliseconds:F2} forced={b.Work.ForcedMoves}/{b.Work.ForcedMoves+b.Work.PolicyMoves}");
    }
    Save("benchmark.json", rows);
}
else throw new ArgumentException("prepare|bench");
void Save(string name, object value) => File.WriteAllText(Path.Combine(root, name), JsonSerializer.Serialize(value, json));
Measurement Measure(Case c, bool optimized)
{
    var a = optimized ? null : new Bots.StrongDiscardLeanBot(c.Seed);var b = optimized ? new Bots.StrongPlayForcedBot(c.Seed) : null;
    long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
    var action = optimized ? b!.Play(c.View, c.Legal) : a!.Play(c.View, c.Legal);
    double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    return new(action, optimized ? b!.LastDefaultSearch : a!.LastDefaultSearch, optimized ? b!.LastRollouts : a!.LastRollouts, optimized ? b!.LastPlayWork : null, milliseconds, allocated);
}
record Case(int Id, int SourceBlock, string Bucket, int Seed, Observation View, PlayAction[] Legal);
record Measurement(PlayAction Action, SearchReport? Search, int Rollouts, Bots.StrongPlayForcedBot.PlayWork? Work, double Milliseconds, long AllocatedBytes);
sealed class Capture(IPlayer inner, List<(Observation View, PlayAction[] Legal)>? views) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => inner.Name;
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => inner.Bid(v, legal);
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => inner.Discard(v, count);
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) { views?.Add((v, legal.ToArray()));return inner.Play(v, legal); }
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v) => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(v) : [];
}
