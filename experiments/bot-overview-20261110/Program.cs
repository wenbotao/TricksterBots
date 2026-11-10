using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using FiveHundredLab.Usability;
using Bots = FiveHundredLab.Bots.FiveHundred;

const string folder = "experiments/bot-overview-20261110/results";
const string input = "experiments/incremental-vs-strongplus-expanded-20261110/results/micro-cases.json";
var json = ArenaCommand.Json;
string[] profiles = ["random-rules-v1", "random-pass-v1", "weak-rules-v1", "medium-rules-v1", "fast-rules-v1", "strong-rules-v1", "partner-rules-v1", "strong-plus-v1", "strong-flow-cached-v1"];
var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText(input), json)!;
void Write(string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, json));
IPlayer Create(string profile, int seed, Rules? rules = null) => Profiles.CreateForRules(profile, seed, rules ?? new Rules());
string mode = args.Length == 0 ? "bench" : args[0];
if (mode == "bench")
{
    if (File.Exists(Path.Combine(folder, "measurements.json"))) throw new IOException("Benchmark output exists");
    if (cases.Length != 144) throw new InvalidDataException("Expected full 144-state corpus");
    Write("registration.json", new
    {
        RegisteredBeforeTiming = true, Cases = cases.Length, Repeats = 3, Profiles = profiles,
        Source = input, SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))), Runtime = Environment.Version.ToString(),
        SourceHashes = Directory.GetFiles("FiveHundredLab/Bots/FiveHundred", "*.cs").ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))),
        Method = "All nine formal public classes, identical observations/seeds. 24 states per six stages, 12 each from incremental/plus trajectories. Per-stage warmup, fixed shuffled states, cyclic profile order balanced over 432 rows. Fresh instances;constructors excluded;decision-local setup included. Serial with no games/tests concurrent. Repeats deterministic;source-block clustered intervals. No new strength matches.",
        Coverage = "Default legacy Rules only;cost states use normal contracts. Reused corpus is an engineering comparison, not independent validation of strength. RandomPass discard is hypothetical reachability because this bot never declares. UX follows separately and is not used as latency data."
    });
    foreach (string bucket in new[] { "bid", "discard", "early", "middle", "late", "forced" })
        foreach (string profile in profiles) Measure(cases.First(c => c.Bucket == bucket), profile);
    var ordered = cases.ToList();new SeededRandom(451119999).Shuffle(ordered);
    var rows = new List<Row>();int order = 0;
    foreach (var c in ordered)
    for (int repeat = 0; repeat < 3; repeat++)
    {
        var measurements = new Dictionary<string, Measurement>();int offset = order++ % profiles.Length;
        for (int j = 0; j < profiles.Length; j++) { string profile = profiles[(j + offset) % profiles.Length];measurements[profile] = Measure(c, profile); }
        rows.Add(new(c.Id, c.SourceBlock, c.SourceArm, c.Phase, c.Bucket, repeat, measurements));
        Console.WriteLine($"PERF {rows.Count}/432 {c.Id}/{c.Bucket}/{repeat}");
    }
    Write("measurements.json", rows);Write("complete.json", new { States = cases.Length, Rows = rows.Count, DecisionCalls = rows.Count * profiles.Length });
}
else if (mode == "ux")
    UxEvaluationV1.Run(Path.Combine(folder, "ux"), trials: 4, seed: 452119001, profiles: profiles);
else if (mode == "rules")
{
    var paths = new[] { "presets", "custom", "examples" }.SelectMany(d => Directory.GetFiles("FiveHundredRules/" + d, "*.json")).Order().ToArray();
    var checks = new List<object>();
    foreach (string? path in new string?[] { null }.Concat(paths))
    {
        var rules = RuleConfiguration.Load(path);
        // The new formal bot is tested at all four seats over all shipped configurations.
        // Only one round: interface/scoring/replay smoke, not win-rate measurement.
        int seed = 453119001 + checks.Count * 7919;
        var game = new Engine(rules).Run(Enumerable.Range(0, 4).Select(s => Create("strong-flow-cached-v1", seed + s, rules)).ToArray(), seed, 1);
        ArenaCommand.Replay(game);
        bool rejected = false;
        try { new Bots.StrongFlowCachedBot(7).Bid(new Observation(0, 0, 1, [], [], [], null, null, [], [], [0,0], RuleConfiguration.Load("FiveHundredRules/presets/au-reference-4p-v1.json"), []), [null]); }
        catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("Rule mismatch accepted");
        string name = path == null ? "default" : Path.GetFileNameWithoutExtension(path);
        Write("rules-" + name + ".game.json", game);
        bool supplement = !game.Rounds.SelectMany(r => r.Decisions).Any(d => d.Phase == "play");
        if (supplement)
        {
            // A passed-out smoke deal exercises bidding only. Use an explicit legal auction
            // fixture to cover pickup/play; never report this harness choice as a bot decision.
            var forced = new Engine(rules).Run(Enumerable.Range(0, 4).Select(s => (IPlayer)new ForcedAuction(Create("strong-flow-cached-v1", seed + s, rules))).ToArray(), seed, 1);
            ArenaCommand.Replay(forced);
            if (!forced.Rounds.SelectMany(r => r.Decisions).Any(d => d.Phase == "play")) throw new InvalidDataException("No play coverage");
            Write("rules-" + name + "-forced-auction.game.json", forced);
        }
        checks.Add(new { Configuration = path ?? "default", AllFourSeats = "StrongFlowCachedBot", Rounds = game.Rounds.Count, Replayed = true, MismatchRejected = rejected, ForcedAuctionSupplement = supplement });
        Console.WriteLine("RULES " + name);
    }
    Write("rule-validation.json", checks);
}
else throw new ArgumentException("bench|ux|rules");
Measurement Measure(Case c, string profile)
{
    var bot = Create(profile, c.Seed);
    long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
    object? action = c.Phase switch { "bid" => bot.Bid(c.View, c.Bids), "discard" => bot.Discard(c.View, c.DiscardCount), _ => bot.Play(c.View, c.Plays) };
    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    if (c.Phase == "bid" && !c.Bids.Contains((Contract?)action)) throw new InvalidDataException("Illegal bid");
    if (c.Phase == "discard") { var d = ((IReadOnlyList<PlayingCard>)action!).ToArray();if (d.Length != c.DiscardCount || d.Distinct().Count() != d.Length || d.Any(x => !c.View.Hand.Contains(x))) throw new InvalidDataException("Illegal discard"); }
    if (c.Phase == "play" && !c.Plays.Contains((PlayAction)action!)) throw new InvalidDataException("Illegal play");
    // Old public wrappers expose zero rollouts by historical convention; do not treat them as zero work.
    return new(JsonSerializer.Serialize(action, json), ms, allocated);
}
record Case(int Id, string Origin, int SourceBlock, string SourceArm, string Phase, string Bucket, int Seed, Observation View, Contract?[] Bids, PlayAction[] Plays, int DiscardCount, string PublicContextHash);
record Measurement(string Action, double Milliseconds, long AllocatedBytes);
record Row(int Id, int SourceBlock, string SourceArm, string Phase, string Bucket, int Repeat, Dictionary<string, Measurement> Measurements);

// Functional coverage fixture only: bidding is scripted, discard/play/declarations use the formal bot.
sealed class ForcedAuction(IPlayer inner) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => "flow-rules-scripted-auction";
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => view.Auction.Count == 0
        ? legal.First(c => c is { Kind: ContractKind.Normal }) : null;
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count) => inner.Discard(view, count);
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal) => inner.Play(view, legal);
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view) => ((IJokerDeclarationPlayer)inner).DeclareJokers(view);
}
