using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab;
using Bots = FiveHundredLab.Bots.FiveHundred;

string root = args[0], mode = args[1];Directory.CreateDirectory(root);
string runner = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Plan).Assembly.Location)));
string versionPath = Path.Combine(root, "runner-version.txt");
if (File.Exists(versionPath) && File.ReadAllText(versionPath) != runner) throw new InvalidDataException("Runner changed; use new output directory");
if (!File.Exists(versionPath)) File.WriteAllText(versionPath, runner);
string[] arms = ["incremental", "plus"];
string[] profiles = ["weak-rules-v1", "medium-rules-v1", "fast-rules-v1", "strong-rules-v1", "partner-rules-v1"];
if (mode == "prepare")
{
    if (File.Exists(Path.Combine(root, "plan.json"))) throw new InvalidOperationException("Already prepared");
    var plans = Enumerable.Range(0, 128).Select(i =>
    {
        var random = new SeededRandom(310119001 + i * 7919);
        return new Plan("replacement", i, 311119001 + i * 7919, i % 4, (i / 4) % 4,
            Enumerable.Range(0, 4).Select(_ => profiles[random.Next(5)]).ToArray(), Enumerable.Range(0, 4).Select(s => 312119001 + i * 7919 + s * 97).ToArray());
    }).Concat(Enumerable.Range(0, 32).Select(i => new Plan("direct", i, 320119001 + i * 7919, 0, i % 4,
        Enumerable.Repeat("medium-rules-v1", 4).ToArray(), Enumerable.Range(0, 4).Select(s => 321119001 + i * 7919 + s * 97).ToArray()))).ToArray();
    IO.Write(Path.Combine(root, "plan.json"), plans);
    IO.Write(Path.Combine(root, "registration.json"), new
    {
        RegisteredBeforeMatches = true, ReplacementBlocks = 128, ReplacementMatches = 256, DirectSeedPairs = 32, DirectMatches = 64, TotalMatches = 320,
        Rules = "Default Rules()", MaxRounds = 200, Workers = 4, Processes = 4, WorkersPerProcess = 1,
        Profiles = new { incremental = Bots.StrongPlayIncrementalBot.Profile, plus = "strong-plus-v1" },
        Replacement = "Same seed/seat/dealer/roster/bot seeds; subject seat and dealer each balanced32; other seats independent uniform Weak/Medium/Fast/Strong/Partner, duplicates allowed; no hand screening",
        Direct = "Two legs per seed: incremental seat0/plus seat1, then incremental seat1/plus seat0; Medium partners seats2,3; target RNG seeds follow bot identity, Medium seeds follow physical seat; same seed/dealer; no aliases",
        Primary = "Replacement paired match win difference,128 independent seed-roster blocks; exact discordant test and conservative CP difference interval",
        Secondary = "Direct win fraction clustered by32 seed pairs, phase timing/allocation, contracts, discard repairs, same-context action differences and first divergence",
        Timing = "Four independent single-worker process shards(plan index modulo4), natural time descriptive; separate serial matched-state costs after all matches",
        PlusRollouts = "Infer default search evaluations as new SearchReport.Samples*Candidates; reject stale report by reference identity; report zero on no-new-search; raw LastRollouts default0 is not used",
        CostPlan = "144 new public states:24 each bid/discard/early/middle/late/forced,12 originating from each profile per stratum;first eligible state per source match;exclude old46 and duplicate contexts;3 serial alternating repeats;no outcome/timing selection",
        PriorStudy = "experiments/incremental-vs-strongplus-20261109/results", PrimaryBatch = "New320 matches only;old80 pooled secondarily after separate reporting;fixed sample size,no significance stopping",
        SourceHashes = new[] { "FiveHundredLab/Bots/FiveHundred/StrongPlayIncrementalBot.cs", "FiveHundredLab/Bots/FiveHundred/StrongPlusBot.cs", "FiveHundredLab/Bots/FiveHundred/IncrementalPlayHistory.cs", "FiveHundredLab/Bots/FiveHundred/IncrementalPlayDataContext.cs", "FiveHundredLab/Bots/FiveHundred/IncrementalPlayRollout.cs" }.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))
    });
    Console.WriteLine("PREPARED 128 replacement pairs +32 mirrored direct pairs =320 new actual matches");
}
else if (mode == "run")
{
    int shard = args.Length > 2 ? int.Parse(args[2]) : 0, shards = args.Length > 3 ? int.Parse(args[3]) : 1;
    if (shards < 1 || shard < 0 || shard >= shards) throw new ArgumentException("Invalid shard");
    int done = 0;var plans = IO.Read<Plan[]>(Path.Combine(root, "plan.json")).Where((p, index) => index % shards == shard).ToArray();
    Parallel.ForEach(plans, new ParallelOptions { MaxDegreeOfParallelism = 1 }, p =>
    {
        string directory = Path.Combine(root, p.Group, $"block-{p.Id:D3}");Directory.CreateDirectory(directory);
        foreach (int j in Enumerable.Range(0, 2))
        {
            int leg = (j + p.Id) % 2;string arm = p.Group == "replacement" ? arms[leg] : "leg" + leg;string prefix = Path.Combine(directory, arm);
            if (File.Exists(prefix + ".json")) { if (!File.Exists(prefix + ".game.gz") || !File.Exists(prefix + ".calls.gz")) throw new InvalidDataException("Missing archive");Interlocked.Increment(ref done);continue; }
            var meters = new Dictionary<string, Meter>();IPlayer[] players;
            if (p.Group == "replacement")
            {
                var meter = new Meter(Create(arm, p.BotSeeds[p.SubjectSeat]));meters[arm] = meter;
                players = Enumerable.Range(0, 4).Select(s => s == p.SubjectSeat ? meter : Profiles.CreateForRules(p.Roster[s], p.BotSeeds[s])).ToArray();
            }
            else
            {
                players = Enumerable.Range(0, 4).Select(s => Profiles.CreateForRules("medium-rules-v1", p.BotSeeds[s])).ToArray();
                meters["incremental"] = new(Create("incremental", p.BotSeeds[0]));meters["plus"] = new(Create("plus", p.BotSeeds[1]));
                players[leg] = meters["incremental"];players[1 - leg] = meters["plus"];
            }
            var watch = Stopwatch.StartNew();GameRecord game;
            try { game = new Engine().Run(players, p.GameSeed, 200, p.Dealer); }
            catch (Exception ex) { IO.Write(prefix + ".failure.json", new { Error = ex.ToString(), Partial = ex.Data["record"] as GameRecord });throw; }
            watch.Stop();ArenaCommand.Replay(game);
            var subjects = meters.ToDictionary(x => x.Key, x =>
            {
                int seat = p.Group == "replacement" ? p.SubjectSeat : x.Key == "incremental" ? leg : 1 - leg;
                int[] scores = game.Rounds[^1].Scores;
                return new SubjectMetric(seat, game.Winner == seat % 2, scores[seat % 2] - scores[1 - seat % 2],
                    game.Rounds.Count(r => !r.Redeal && r.Declarer == seat), game.Rounds.Count(r => !r.Redeal && r.Declarer == seat && r.Made),
                    x.Value.Calls.Count, x.Value.Calls.Sum(c => c.Milliseconds), x.Value.Calls.Sum(c => c.AllocatedBytes), x.Value.Calls.Count(c => c.Guard?.GetProperty("Changed").GetBoolean() == true));
            });
            IO.Zip(prefix + ".game.gz", game);IO.Zip(prefix + ".calls.gz", meters.ToDictionary(x => x.Key, x => x.Value.Calls));
            IO.Write(prefix + ".json", new Metric(p.Group, p.Id, arm, p.GameSeed, p.Dealer, game.Players, game.Winner, game.Truncated,
                game.Rounds.Count, game.Rounds.Count(r => !r.Redeal), game.Rounds.Count(r => r.Redeal), watch.Elapsed.TotalSeconds, subjects));
            Console.WriteLine($"MATCH shard={shard} local={Interlocked.Increment(ref done)}/{plans.Length * 2} {p.Group}={p.Id} arm={arm} winner={game.Winner} rounds={game.Rounds.Count} seconds={watch.Elapsed.TotalSeconds:F1} incWin={subjects.GetValueOrDefault("incremental")?.Win}");
        }
        IO.Write(Path.Combine(directory, "complete.json"), new { p.Id, Passed = true });
    });
}
else if (mode == "verify")
{
    int matches = 0;var comparisons = new List<object>();
    foreach (var p in IO.Read<Plan[]>(Path.Combine(root, "plan.json")))
    {
        string dir = Path.Combine(root, p.Group, $"block-{p.Id:D3}");if (!File.Exists(Path.Combine(dir, "complete.json"))) continue;
        var names = p.Group == "replacement" ? arms : new[] { "leg0", "leg1" };
        var games = names.Select(a => IO.ReadZip<GameRecord>(Path.Combine(dir, a + ".game.gz"))).ToArray();
        foreach (var game in games)
        {
            ArenaCommand.Replay(game);matches++;
            foreach (var r in game.Rounds.Where(r => !r.Redeal)) if (r.TrickWinners.Count != 10 || r.Tricks.Sum() != 10) throw new InvalidDataException("Incomplete deal");
        }
        for (int i = 0; i < Math.Min(games[0].Rounds.Count, games[1].Rounds.Count); i++)
        {
            var a = games[0].Rounds[i];var b = games[1].Rounds[i];
            if (a.Dealer != b.Dealer || !a.Kitty.SequenceEqual(b.Kitty) || JsonSerializer.Serialize(a.Hands, IO.Json) != JsonSerializer.Serialize(b.Hands, IO.Json)) throw new InvalidDataException("Paired deals changed");
        }
        if (p.Group != "replacement") continue;
        var left = IO.ReadZip<Dictionary<string, Call[]>>(Path.Combine(dir, "incremental.calls.gz"))["incremental"];
        var right = IO.ReadZip<Dictionary<string, Call[]>>(Path.Combine(dir, "plus.calls.gz"))["plus"];
        var lookup = right.GroupBy(c => (c.Round, c.Phase, c.ContextHash)).ToDictionary(g => g.Key, g => new Queue<Call>(g));
        var aligned = new List<object>();int identical = 0;
        foreach (var c in left)
        {
            if (!lookup.TryGetValue((c.Round, c.Phase, c.ContextHash), out var q) || q.Count == 0) continue;
            var d = q.Dequeue();bool same = c.Action == d.Action;if (same) identical++;
            if (!same) aligned.Add(new { c.Round, c.Phase, c.Hand, c.Contract, c.Scores, c.LegalCount, Incremental = c.Action, Plus = d.Action });
        }
        int index = 0;
        while (index < Math.Min(left.Length, right.Length) && left[index].ContextHash == right[index].ContextHash && left[index].Action == right[index].Action) index++;
        comparisons.Add(new { p.Id, AlignedContexts = identical + aligned.Count, IdenticalActions = identical, Differences = aligned,
            FirstDifference = index < Math.Min(left.Length, right.Length) ? new { SameContext = left[index].ContextHash == right[index].ContextHash, Incremental = left[index], Plus = right[index] } : null });
    }
    IO.Write(Path.Combine(root, "comparisons.json"), comparisons);
    IO.Write(Path.Combine(root, "replay-validation.json"), new { Matches = matches, PairingVerified = true, Passed = true });
    Console.WriteLine($"REPLAY {matches} matches; paired deals verified");
}
else throw new ArgumentException("prepare|run|verify");

static IPlayer Create(string arm, int seed) => arm == "incremental" ? new Bots.StrongPlayIncrementalBot(seed) : arm == "plus" ? new Bots.StrongPlusBot(seed) : throw new ArgumentException(arm);
record Plan(string Group, int Id, int GameSeed, int SubjectSeat, int Dealer, string[] Roster, int[] BotSeeds);
record SubjectMetric(int Seat, bool Win, int RelativeScore, int Contracts, int Made, int Calls, double Milliseconds, long AllocatedBytes, int RepairEvents);
record Metric(string Group, int Id, string Arm, int Seed, int Dealer, string[] Players, int? Winner, bool Truncated, int Rounds, int PlayedDeals, int Redeals, double Seconds, Dictionary<string, SubjectMetric> Subjects);
record Call(int Round, string Phase, int Seat, string ContextHash, string Action, string Hand, Contract? Contract, int[] Scores, int LegalCount, double Milliseconds, long AllocatedBytes, int Rollouts, string RolloutSource, SearchReport? Search, JsonElement? Guard);
sealed class Meter(IPlayer inner) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => inner.Name;public List<Call> Calls { get; } = [];
    private SearchReport? Search() => inner switch { Bots.StrongPlayIncrementalBot b => b.LastDefaultSearch, Bots.StrongPlusBot b => b.LastDefaultSearch, _ => null };
    private T Measure<T>(Observation view, string phase, int legal, Func<T> call)
    {
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(view, IO.Json)));var previous = Search();
        long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        T result = call();double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var search = Search();if (ReferenceEquals(previous, search) || phase == "declare") search = null;
        int rollouts = phase == "declare" ? 0 : inner is Bots.StrongPlayIncrementalBot b ? b.LastRollouts : (search?.Samples ?? 0) * (search?.Candidates ?? 0);
        object? guard = phase == "discard" && inner is Bots.StrongPlayIncrementalBot inc ? inc.LastDiscardGuard : null;
        Calls.Add(new(view.Round, phase, view.Seat, hash, JsonSerializer.Serialize(result, IO.Json), string.Join(" ", view.Hand), view.Contract, view.Scores.ToArray(), legal, ms, allocated, rollouts,
            inner is Bots.StrongPlusBot ? "new-search-samples-times-candidates" : "actual-counter", search, guard == null ? null : JsonSerializer.SerializeToElement(guard, IO.Json)));
        return result;
    }
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => Measure(v, "bid", legal.Count, () => inner.Bid(v, legal));
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => Measure(v, "discard", count, () => inner.Discard(v, count));
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => Measure(v, "play", legal.Count, () => inner.Play(v, legal));
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v) => Measure(v, "declare", 0, () => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(v) : []);
}
static class IO
{
    public static readonly JsonSerializerOptions Json = ArenaCommand.Json;
    public static void Write<T>(string path, T data) { File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, Json));File.Move(path + ".tmp", path, true); }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)!;
    public static void Zip<T>(string path, T data) { using (var file = File.Create(path + ".tmp")) using (var zip = new GZipStream(file, CompressionLevel.Fastest)) JsonSerializer.Serialize(zip, data, Json);File.Move(path + ".tmp", path, true); }
    public static T ReadZip<T>(string path) { using var file = File.OpenRead(path);using var zip = new GZipStream(file, CompressionMode.Decompress);return JsonSerializer.Deserialize<T>(zip, Json)!; }
}
