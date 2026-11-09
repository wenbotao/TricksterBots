using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using Bots = FiveHundredLab.Bots.FiveHundred;

string root = "experiments/play-cached-v1-20261109/results";Directory.CreateDirectory(root);
string mode = args[0];var json = ArenaCommand.Json;
if (mode == "prepare")
{
    if (File.Exists(Path.Combine(root, "cases.json"))) throw new InvalidOperationException("Prepared already");
    string input = "experiments/play-forced-v1-20261109/results/cases.json";
    File.Copy(input, Path.Combine(root, "cases.json"));
    Save("registration.json", new { Cases = 24, Repeats = 3, Reference = "strong-play-forced-v1", Candidate = "strong-play-cached-v1",
        Input = input, InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
        Selection = "Reuse unchanged24 regression states from previous step; same seed/observation/legal actions; not a fresh strength holdout",
        Benchmark = "Serial after full tests; alternate pair order; independent unmeasured UX warmup; new instances outside measured call; current-thread decision-body allocation; no forced GC",
        Scope = "Default play data preparation only; configured execution, bidding, discard, policy instances and history reconstruction unchanged; no cache tuning on results",
        SourceHashes = new[] { "FiveHundredLab/Bots/FiveHundred/StrongPlayForcedBot.cs", "FiveHundredLab/Bots/FiveHundred/StrongPlayCachedBot.cs", "FiveHundredLab/Bots/FiveHundred/PlayDataContext.cs", "FiveHundredLab/Bots/FiveHundred/CachedPlayRollout.cs" }.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))) });
    Console.WriteLine("PREPARED same24 public states;3 paired repeats;previous forced version as reference");
}
else if (mode == "bench")
{
    var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText(Path.Combine(root, "cases.json")), json)!;
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
        if (a.Action != b.Action || a.Search != b.Search || a.Rollouts != b.Rollouts || a.Work != b.Work) throw new InvalidDataException("Decision/search/continuation work changed");
        if (b.Work.Evaluations != b.Rollouts) throw new InvalidDataException("Counter mismatch");
        if (b.Data != null && (b.Data.RootContexts != 1 || b.Data.EncodedCards != 43 || b.Data.OptionsCreated > 5 || b.Data.AdapterCalls != b.Work.PolicyMoves)) throw new InvalidDataException("Data reuse counter mismatch");
        rows.Add(new { c.Id, c.SourceBlock, c.Bucket, Repeat = repeat, LegalCount = c.Legal.Length, HandCount = c.View.Hand.Count, Reference = a, Optimized = b, SameActionAndSearch = true, SameContinuationWork = true });
        Console.WriteLine($"BENCH {rows.Count}/72 {c.Bucket} case={c.Id} repeat={repeat} ms={a.Milliseconds:F2}/{b.Milliseconds:F2} data-options={b.Data?.OptionsCreated} adapted={b.Data?.AdapterCalls}");
    }
    Save("benchmark.json", rows);
}
else throw new ArgumentException("prepare|bench");
void Save(string name, object value) => File.WriteAllText(Path.Combine(root, name), JsonSerializer.Serialize(value, json));
Measurement Measure(Case c, bool optimized)
{
    var a = optimized ? null : new Bots.StrongPlayForcedBot(c.Seed);var b = optimized ? new Bots.StrongPlayCachedBot(c.Seed) : null;
    long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
    var action = optimized ? b!.Play(c.View, c.Legal) : a!.Play(c.View, c.Legal);
    double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var work = optimized ? new Work(b!.LastPlayWork!.Evaluations, b.LastPlayWork.ForcedMoves, b.LastPlayWork.PolicyMoves) : new Work(a!.LastPlayWork!.Evaluations, a.LastPlayWork.ForcedMoves, a.LastPlayWork.PolicyMoves);
    return new(action, optimized ? b!.LastDefaultSearch : a!.LastDefaultSearch, optimized ? b!.LastRollouts : a!.LastRollouts, work, optimized ? b!.LastDataWork : null, milliseconds, allocated);
}
record Case(int Id, int SourceBlock, string Bucket, int Seed, Observation View, PlayAction[] Legal);
record Work(long Evaluations, long ForcedMoves, long PolicyMoves);
record Measurement(PlayAction Action, SearchReport? Search, int Rollouts, Work Work, Bots.StrongPlayCachedBot.DataWork? Data, double Milliseconds, long AllocatedBytes);
