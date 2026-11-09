using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using Bots = FiveHundredLab.Bots.FiveHundred;

string root = "experiments/incremental-vs-strongplus-20261109/results";var json = ArenaCommand.Json;string mode = args[0];
if (mode == "prepare")
{
    if (File.Exists(Path.Combine(root, "micro-cases.json"))) throw new InvalidOperationException("Already prepared");
    var cases = new List<Case>();
    using (var old = JsonDocument.Parse(File.ReadAllText("experiments/play-incremental-v1-20261109/results/cases.json")))
        foreach (var item in old.RootElement.EnumerateArray()) cases.Add(new(cases.Count, "natural-play-regression", item.GetProperty("SourceBlock").GetInt32(), "play", item.GetProperty("Bucket").GetString()!,
            item.GetProperty("Seed").GetInt32(), item.GetProperty("View").Deserialize<Observation>(json)!, [], item.GetProperty("Legal").Deserialize<PlayAction[]>(json)!, 0));
    using var plan = JsonDocument.Parse(File.ReadAllText("experiments/natural-matches-v1-20261109/results/plan.json"));
    foreach (var p in plan.RootElement.EnumerateArray())
    {
        int block = p.GetProperty("Id").GetInt32(), subject = p.GetProperty("SubjectSeat").GetInt32();
        string path = $"experiments/natural-matches-v1-20261109/results/matches/block-{block:D3}/lean.game.gz";
        using var f = File.OpenRead(path);using var zip = new GZipStream(f, CompressionMode.Decompress);var game = JsonSerializer.Deserialize<GameRecord>(zip, json)!;
        var captured = new List<Captured>();
        var players = Enumerable.Range(0, 4).Select(s => (IPlayer)new Capture(new ReplayPlayer(game.Players[s], game.Rounds.SelectMany(r => r.Decisions).Where(d => d.Seat == s)), s == subject ? captured : null)).ToArray();
        var replay = new Engine(game.Rules).Run(players, game.Seed, game.MaxRounds, game.InitialDealer);
        if (JsonSerializer.Serialize(replay, json) != JsonSerializer.Serialize(game, json)) throw new InvalidDataException("Source replay changed");
        foreach (string phase in new[] { "bid", "discard" })
        {
            if (cases.Count(c => c.Phase == phase) >= 8) continue;
            var state = captured.FirstOrDefault(c => c.Phase == phase);if (state == null) continue;
            cases.Add(new(cases.Count, "natural-other-phase", block, phase, phase, 230119001 + block * 7919, state.View, state.Bids, [], state.DiscardCount));
        }
        if (cases.Count(c => c.Phase == "bid") == 8 && cases.Count(c => c.Phase == "discard") == 8) break;
    }
    foreach (var f in FiveHundredLab.Usability.UxEvaluationV1.Build().Where(f => f.Scenario.Phase == "play" && f.View.Contract?.IsMisere == true && f.View.Rules.GetType() == typeof(Rules)))
        cases.Add(new(cases.Count, "ux-misere", -1, "play", f.Scenario.Id, 231119001 + cases.Count * 97, f.View, [], f.LegalPlays.ToArray(), 0));
    IO.Write(Path.Combine(root, "micro-cases.json"), cases);
    IO.Write(Path.Combine(root, "micro-registration.json"), new { RegisteredBeforeTiming = true, Cases = cases.Count, Repeats = 2,
        Groups = cases.GroupBy(c => c.Origin + "/" + c.Phase).ToDictionary(g => g.Key, g => g.Count()),
        Selection = "Previous unchanged24 play states plus first8 subject bids/first8 subject discards from recorded natural games, one per source match/phase, and all existing default Misere UX play scenes; no outcome/timing selection",
        Method = "Serial after full tests and matches; warm both implementations on one unmeasured call per phase; alternate execution order; same public view/seed/legal actions; decision-body time/allocation excluding serialization and constructors",
        Rollouts = "Incremental actual counter; Plus fresh report Samples*Candidates, no stale report",
        InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "micro-cases.json")))) });
    Console.WriteLine($"MICRO PREPARED {cases.Count} states;2 repeats");
}
else if (mode == "bench")
{
    var cases = IO.Read<Case[]>(Path.Combine(root, "micro-cases.json"));
    foreach (string phase in new[] { "bid", "discard", "play" })
    {
        var warm = cases.First(c => c.Phase == phase && (phase != "play" || c.Plays.Length > 1)) with { Seed = 232119000 };
        Measure(warm, false);Measure(warm, true);
    }
    int order = 0;var rows = new List<object>();
    foreach (var c in cases)
    for (int repeat = 0; repeat < 2; repeat++)
    {
        Measurement a, b;
        if (order++ % 2 == 0) { a = Measure(c, false);b = Measure(c, true); } else { b = Measure(c, true);a = Measure(c, false); }
        rows.Add(new { c.Id, c.SourceBlock, c.Origin, c.Phase, c.Bucket, Repeat = repeat, HandCount = c.View.Hand.Count, LegalCount = c.Phase == "bid" ? c.Bids.Length : c.Phase == "play" ? c.Plays.Length : c.DiscardCount,
            Incremental = a, Plus = b, SameAction = a.Action == b.Action });
        Console.WriteLine($"MICRO {rows.Count}/{2 * cases.Length} {c.Origin}/{c.Phase} {c.Id}/{repeat} same={a.Action == b.Action} ms={a.Milliseconds:F2}/{b.Milliseconds:F2}");
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
record Case(int Id, string Origin, int SourceBlock, string Phase, string Bucket, int Seed, Observation View, Contract?[] Bids, PlayAction[] Plays, int DiscardCount);
record Captured(string Phase, Observation View, Contract?[] Bids, int DiscardCount);
record Measurement(string Action, SearchReport? Search, int Rollouts, double Milliseconds, long AllocatedBytes, JsonElement? UxChecks, JsonElement? Tactics, JsonElement? Guard);
sealed class Capture(IPlayer inner, List<Captured>? captured) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => inner.Name;
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) { captured?.Add(new("bid", v, legal.ToArray(), 0));return inner.Bid(v, legal); }
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count) { captured?.Add(new("discard", v, [], count));return inner.Discard(v, count); }
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => inner.Play(v, legal);
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v) => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(v) : [];
}
static class IO
{
    public static readonly JsonSerializerOptions Json = ArenaCommand.Json;
    public static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)!;
}
