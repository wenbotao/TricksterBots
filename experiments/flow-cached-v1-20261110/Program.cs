using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using Bots = FiveHundredLab.Bots.FiveHundred;

const string folder = "experiments/flow-cached-v1-20261110/results";
const string input = "experiments/incremental-vs-strongplus-expanded-20261110/results/micro-cases.json";
var json = ArenaCommand.Json;
var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText(input), json)!;
if (cases.Length != 144) throw new InvalidDataException("Expected the full previous 144-state corpus");
var source = new[] { "StrongFlowCachedBot.cs", "FlowPolicyCache.cs", "FlowCachedRollout.cs" }
    .ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("FiveHundredLab/Bots/FiveHundred/" + f))));
void Write(string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, json));
Write("registration.json", new
{
    Cases = cases.Length, Repeats = 3, Profiles = new[] { Bots.StrongPlayIncrementalBot.Profile, Bots.StrongFlowCachedBot.Profile, "strong-plus-v1" },
    Source = input, SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))), SourceHashes = source,
    RegisteredBeforeTiming = true, Runtime = Environment.Version.ToString(),
    Method = "Same 144 public states as expanded study, no screening. Three profiles freshly measured serially. New instances and identical seeds; construction excluded. Warm each phase; fixed state shuffle;rotate all 3 execution orders. State repeat medians then paired log ratio, equal source-block aggregation/bootstrap. Primary comparison flow/incremental;plus auxiliary. Fixed corpus reused for engineering optimization, not independent strength validation."
});
foreach (string bucket in new[] { "bid", "discard", "early", "middle", "late" })
    for (int profile = 0; profile < 3; profile++) Measure(cases.First(c => c.Bucket == bucket), profile);
var ordered = cases.ToList();new SeededRandom(441119999).Shuffle(ordered);
var rows = new List<Row>();int sequence = 0;
foreach (var c in ordered)
for (int repeat = 0; repeat < 3; repeat++)
{
    var measurements = new Measurement[3];
    int offset = sequence++ % 3;
    for (int j = 0; j < 3; j++) { int profile = (j + offset) % 3;measurements[profile] = Measure(c, profile); }
    if (measurements[0].Action != measurements[1].Action || measurements[0].Rollouts != measurements[1].Rollouts
        || measurements[0].Search != measurements[1].Search || measurements[0].Tactics != measurements[1].Tactics || measurements[0].Guard != measurements[1].Guard)
        throw new InvalidDataException($"Behavior differs: {c.Id}/{repeat}");
    rows.Add(new(c.Id, c.SourceBlock, c.SourceArm, c.Phase, c.Bucket, repeat, measurements));
    Console.WriteLine($"COST {rows.Count}/432 {c.Id}/{c.Bucket}/{repeat} ms inc={measurements[0].Milliseconds:F3} flow={measurements[1].Milliseconds:F3} plus={measurements[2].Milliseconds:F3}");
}
Write("measurements.json", rows);
Write("complete.json", new { States = cases.Length, Pairs = rows.Count, ActionsAndSearchEqual = true });
Measurement Measure(Case c, int profile)
{
    var old = profile == 0 ? new Bots.StrongPlayIncrementalBot(c.Seed) : null;
    var flow = profile == 1 ? new Bots.StrongFlowCachedBot(c.Seed) : null;
    var plus = profile == 2 ? new Bots.StrongPlusBot(c.Seed) : null;
    IPlayer bot = profile == 0 ? old! : profile == 1 ? flow! : plus!;
    long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
    object? action = c.Phase switch { "bid" => bot.Bid(c.View, c.Bids), "discard" => bot.Discard(c.View, c.DiscardCount), _ => bot.Play(c.View, c.Plays) };
    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    if (c.Phase == "bid" && !c.Bids.Contains((Contract?)action)) throw new InvalidDataException("Illegal bid");
    if (c.Phase == "discard") { var cards = ((IReadOnlyList<PlayingCard>)action!).ToArray();if (cards.Length != c.DiscardCount || cards.Distinct().Count() != cards.Length || cards.Any(x => !c.View.Hand.Contains(x))) throw new InvalidDataException("Illegal discard"); }
    if (c.Phase == "play" && !c.Plays.Contains((PlayAction)action!)) throw new InvalidDataException("Illegal play");
    var search = old?.LastDefaultSearch ?? flow?.LastDefaultSearch ?? plus?.LastDefaultSearch;
    int rollouts = profile == 2 ? (search?.Samples ?? 0) * (search?.Candidates ?? 0) : profile == 0 ? old!.LastRollouts : flow!.LastRollouts;
    return new(JsonSerializer.Serialize(action, json), search, rollouts, ms, allocated,
        profile == 0 ? JsonSerializer.Serialize(old!.LastTactics, json) : profile == 1 ? JsonSerializer.Serialize(flow!.LastTactics, json) : "null",
        profile == 0 ? JsonSerializer.Serialize(old!.LastDiscardGuard, json) : profile == 1 ? JsonSerializer.Serialize(flow!.LastDiscardGuard, json) : "null",
        profile == 1 ? JsonSerializer.Serialize(flow!.LastDataWork, json) : null);
}
record Case(int Id, string Origin, int SourceBlock, string SourceArm, string Phase, string Bucket, int Seed, Observation View, Contract?[] Bids, PlayAction[] Plays, int DiscardCount, string PublicContextHash);
record Measurement(string Action, SearchReport? Search, int Rollouts, double Milliseconds, long AllocatedBytes, string Tactics, string Guard, string? DataWork);
record Row(int Id, int SourceBlock, string SourceArm, string Phase, string Bucket, int Repeat, Measurement[] Measurements);
