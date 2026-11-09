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
if (File.Exists(versionPath) && File.ReadAllText(versionPath) != runner) throw new InvalidDataException("Runner changed; use new results directory");
File.WriteAllText(versionPath, runner);
string[] arms = ["unpatched", "guard", "lean"];
string[] profiles = ["weak-rules-v1", "medium-rules-v1", "fast-rules-v1", "strong-rules-v1", "partner-rules-v1"];
if (mode == "prepare")
{
    if (File.Exists(Path.Combine(root, "plan.json"))) throw new InvalidOperationException("Already prepared");
    var plans = Enumerable.Range(0, 32).Select(i =>
    {
        var random = new SeededRandom(121119001 + i * 7919);
        var roster = Enumerable.Range(0, 4).Select(_ => profiles[random.Next(profiles.Length)]).ToArray();
        return new Plan(i, 122119001 + i * 7919, i % 4, (i / 4) % 4, roster, Enumerable.Range(0, 4).Select(s => 123119001 + i * 7919 + s * 97).ToArray());
    }).ToArray();
    IO.Write(Path.Combine(root, "plan.json"), plans);
    IO.Write(Path.Combine(root, "registration.json"), new
    {
        RegisteredBeforeMatches = true, Blocks = 32, ActualMatches = 96, Rules = "Default Rules()", MaxRounds = 200, Workers = 2,
        Arms = new { unpatched = "strong-misere-proof-v1", guard = "strong-discard-guard-efficient-v1", lean = "strong-discard-guard-lean-v1" },
        Design = "No hand selection; subject seat balanced8 per seat; dealer balanced8 per seat; other seats independent uniform Weak/Medium/Fast/Strong/Partner, duplicates allowed; no parameters changed",
        Pairing = "Same game seed/dealer/seat/roster/bot seeds for all3 arms; independent instances; actual engine execution for every arm, no alias results",
        Primary = "Lean vs unpatched paired completed-match win difference; guard vs unpatched corroboration, lean vs guard behavior equivalence",
        Secondary = "Terminal relative score, played rounds, redeals, subject contracts/makes, natural discard-repair exposure and changes, first behavior divergence, phase timing/allocations/rollouts",
        Analysis = "Independent unit32 seed-roster blocks; discordant win pairs, exact McNemar and conservative CP difference bound; cluster paired means by block; descriptive roster strata only",
        Timing = "2 concurrent blocks; rotate arm order by block; natural timings include JIT; descriptive only, no isolated speedup claim; no Strong internal continuations",
        SourceHashes = new[] { "FiveHundredLab/Bots/FiveHundred/StrongProofAblationBot.cs", "FiveHundredLab/Bots/FiveHundred/StrongDiscardRefinedBot.cs", "FiveHundredLab/Bots/FiveHundred/StrongDiscardLeanBot.cs" }.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))
    });
    Console.WriteLine("PREPARED 32 seed-roster blocks;96 actual matches; no hand selection");
}
else if (mode == "run")
{
    var plans = IO.Read<Plan[]>(Path.Combine(root, "plan.json"));int done = 0;
    Parallel.ForEach(plans, new ParallelOptions { MaxDegreeOfParallelism = 2 }, plan =>
    {
        string directory = Path.Combine(root, "matches", $"block-{plan.Id:D3}");Directory.CreateDirectory(directory);
        foreach (int j in Enumerable.Range(0, 3))
        {
            string arm = arms[(j + plan.Id) % 3];string prefix = Path.Combine(directory, arm);
            if (File.Exists(prefix + ".json"))
            { if (!File.Exists(prefix + ".game.gz") || !File.Exists(prefix + ".calls.gz")) throw new InvalidDataException("Incomplete checkpoint");Interlocked.Increment(ref done);continue; }
            IPlayer subject = arm switch
            {
                "unpatched" => new Bots.StrongProofAblationBot(Bots.StrongProofProfile.MisereOnly, plan.BotSeeds[plan.SubjectSeat]),
                "guard" => new Bots.StrongDiscardRefinedBot(Bots.DiscardRefinementProfile.EfficientBaseline, plan.BotSeeds[plan.SubjectSeat]),
                "lean" => new Bots.StrongDiscardLeanBot(plan.BotSeeds[plan.SubjectSeat]),
                _ => throw new ArgumentException(arm)
            };
            var meter = new Meter(subject);
            var players = Enumerable.Range(0, 4).Select(s => s == plan.SubjectSeat ? meter : Profiles.CreateForRules(plan.Roster[s], plan.BotSeeds[s])).ToArray();
            var watch = Stopwatch.StartNew();GameRecord game;
            try { game = new Engine().Run(players, plan.GameSeed, 200, plan.Dealer); }
            catch (Exception ex) { IO.Write(prefix + ".failure.json", new { Error = ex.ToString(), Partial = ex.Data["record"] as GameRecord });throw; }
            watch.Stop();ArenaCommand.Replay(game);
            int[] scores = game.Rounds.Count == 0 ? [0, 0] : game.Rounds[^1].Scores;
            IO.Zip(prefix + ".game.gz", game);IO.Zip(prefix + ".calls.gz", meter.Calls);
            IO.Write(prefix + ".json", new { plan.Id, Arm = arm, plan.GameSeed, plan.SubjectSeat, plan.Dealer, plan.Roster, plan.BotSeeds,
                game.Winner, game.Truncated, SubjectWin = game.Winner == plan.SubjectSeat % 2, Scores = scores,
                RelativeScore = scores[plan.SubjectSeat % 2] - scores[1 - plan.SubjectSeat % 2], Rounds = game.Rounds.Count,
                PlayedDeals = game.Rounds.Count(r => !r.Redeal), Redeals = game.Rounds.Count(r => r.Redeal), SubjectContracts = game.Rounds.Count(r => !r.Redeal && r.Declarer == plan.SubjectSeat),
                SubjectMade = game.Rounds.Count(r => !r.Redeal && r.Declarer == plan.SubjectSeat && r.Made),
                SubjectNormalNoTrump = game.Rounds.Count(r => !r.Redeal && r.Declarer == plan.SubjectSeat && r.Contract is { IsMisere: false, Trump: Trickster.cloud.Suit.Unknown }),
                Seconds = watch.Elapsed.TotalSeconds, Calls = meter.Calls.Count, RepairEvents = meter.Calls.Count(c => c.Guard?.GetProperty("Changed").GetBoolean() == true),
                SubjectMilliseconds = meter.Calls.Sum(c => c.Milliseconds), SubjectAllocatedBytes = meter.Calls.Sum(c => c.AllocatedBytes), ReplayPassed = true });
            Console.WriteLine($"MATCH {Interlocked.Increment(ref done)}/96 block={plan.Id} arm={arm} win={game.Winner == plan.SubjectSeat % 2} rounds={game.Rounds.Count} seconds={watch.Elapsed.TotalSeconds:F1} repairs={meter.Calls.Count(c => c.Guard?.GetProperty("Changed").GetBoolean() == true)}");
        }
        IO.Write(Path.Combine(directory, "complete.json"), new { plan.Id, Passed = true });
    });
}
else if (mode == "verify")
{
    int matches = 0, equivalent = 0;var comparisons = new List<object>();
    foreach (var plan in IO.Read<Plan[]>(Path.Combine(root, "plan.json")))
    {
        string directory = Path.Combine(root, "matches", $"block-{plan.Id:D3}");if (!File.Exists(Path.Combine(directory, "complete.json"))) continue;
        var games = arms.ToDictionary(a => a, a => IO.ReadZip<GameRecord>(Path.Combine(directory, a + ".game.gz")));
        var calls = arms.ToDictionary(a => a, a => IO.ReadZip<Call[]>(Path.Combine(directory, a + ".calls.gz")));
        foreach (var game in games.Values)
        {
            ArenaCommand.Replay(game);matches++;
            foreach (var round in game.Rounds.Where(r => !r.Redeal)) if (round.TrickWinners.Count != 10 || round.Tricks.Sum() != 10) throw new InvalidDataException("Incomplete deal");
        }
        // Normalize the deliberately different subject name only; every other field must match.
        var guard = games["guard"];var lean = games["lean"];guard.Players[plan.SubjectSeat] = lean.Players[plan.SubjectSeat] = "subject";
        if (JsonSerializer.Serialize(guard, IO.Json) != JsonSerializer.Serialize(lean, IO.Json)) throw new InvalidDataException("Lean game changed");
        var cg = calls["guard"];var cl = calls["lean"];
        if (cg.Length != cl.Length) throw new InvalidDataException("Call counts changed");
        for (int i = 0; i < cg.Length; i++) if (cg[i].ContextHash != cl[i].ContextHash || cg[i].Action != cl[i].Action || cg[i].Rollouts != cl[i].Rollouts || cg[i].Search != cl[i].Search || cg[i].Guard?.GetRawText() != cl[i].Guard?.GetRawText()) throw new InvalidDataException("Lean context/action/search changed");
        equivalent++;
        var old = games["unpatched"];
        for (int i = 0; i < Math.Min(old.Rounds.Count, lean.Rounds.Count); i++)
        {
            var a = old.Rounds[i];var b = lean.Rounds[i];
            if (JsonSerializer.Serialize(a.Hands, IO.Json) != JsonSerializer.Serialize(b.Hands, IO.Json) || !a.Kitty.SequenceEqual(b.Kitty) || a.Dealer != b.Dealer) throw new InvalidDataException("Paired deal mismatch");
        }
        var baseline = calls["unpatched"];int first = 0;
        while (first < Math.Min(baseline.Length, cl.Length) && baseline[first].ContextHash == cl[first].ContextHash && baseline[first].Action == cl[first].Action) first++;
        bool same = first == baseline.Length && first == cl.Length;
        if (!same)
        {
            if (first >= Math.Min(baseline.Length, cl.Length) || baseline[first].ContextHash != cl[first].ContextHash || cl[first].Phase != "discard" || cl[first].Guard?.GetProperty("Changed").GetBoolean() != true) throw new InvalidDataException("Unexplained first divergence");
            var original = cl[first].Guard!.Value.GetProperty("Original").GetRawText();
            if (baseline[first].Action != original) throw new InvalidDataException("Baseline proposal mismatch");
        }
        else
        {
            old.Players[plan.SubjectSeat] = "subject";
            if (JsonSerializer.Serialize(old, IO.Json) != JsonSerializer.Serialize(lean, IO.Json)) throw new InvalidDataException("Unexplained non-action divergence");
        }
        comparisons.Add(new { plan.Id, SameActions = same, FirstDifference = same ? (Call?)null : cl[first], BaselineAction = same ? null : baseline[first].Action, GuardEvents = cl.Count(c => c.Guard?.GetProperty("Changed").GetBoolean() == true) });
    }
    IO.Write(Path.Combine(root, "comparisons.json"), comparisons);
    IO.Write(Path.Combine(root, "replay-validation.json"), new { MatchesReplayed = matches, EngineeringEquivalentBlocks = equivalent, PairedDealsVerified = true, FirstDivergencesExplained = true, Passed = true });
    Console.WriteLine($"VERIFIED {matches} matches; engineering equivalent blocks={equivalent}");
}
else throw new ArgumentException("prepare|run|verify");

record Plan(int Id, int GameSeed, int SubjectSeat, int Dealer, string[] Roster, int[] BotSeeds);
record Call(int Round, string Phase, int Seat, string ContextHash, string Action, string Hand, Contract? Contract, int[] Scores, int LegalCount, double Milliseconds, long AllocatedBytes, int? Rollouts, SearchReport? Search, JsonElement? Guard);
sealed class Meter(IPlayer inner) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => inner.Name;public List<Call> Calls { get; } = [];
    private T Measure<T>(Observation view, string phase, int legal, Func<T> call)
    {
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(view, IO.Json)));
        long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        T result = call();double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        int? rollouts = phase == "declare" ? null : inner switch { Bots.StrongDiscardLeanBot b => b.LastRollouts, Bots.StrongDiscardRefinedBot b => b.LastRollouts, _ => null };
        SearchReport? search = phase == "declare" ? null : inner switch { Bots.StrongDiscardLeanBot b => b.LastDefaultSearch, Bots.StrongDiscardRefinedBot b => b.LastDefaultSearch, _ => null };
        object? guard = phase == "discard" ? inner switch { Bots.StrongDiscardLeanBot b => b.LastDiscardGuard, Bots.StrongDiscardRefinedBot b => b.LastDiscardGuard, _ => null } : null;
        Calls.Add(new(view.Round, phase, view.Seat, hash, JsonSerializer.Serialize(result, IO.Json), string.Join(" ", view.Hand), view.Contract, view.Scores.ToArray(), legal, milliseconds, allocated, rollouts, search, guard == null ? null : JsonSerializer.SerializeToElement(guard, IO.Json)));
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
    public static void Zip<T>(string path, T data) { using (var file = File.Create(path + ".tmp")) using (var gzip = new GZipStream(file, CompressionLevel.Fastest)) JsonSerializer.Serialize(gzip, data, Json);File.Move(path + ".tmp", path, true); }
    public static T ReadZip<T>(string path) { using var file = File.OpenRead(path);using var gzip = new GZipStream(file, CompressionMode.Decompress);return JsonSerializer.Deserialize<T>(gzip, Json)!; }
}
