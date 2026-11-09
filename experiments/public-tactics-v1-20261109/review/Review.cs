using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using FiveHundredArena;
using Bots = FiveHundredLab.Bots.FiveHundred;

// Offline review only: feed the recorded decisions to the engine. Fresh counterfactual bots
// see precisely the same observation and seed; their suggested actions are never executed.
string root = args[0];
var rows = new ConcurrentBag<object>();
var paths = Directory.GetFiles(Path.Combine(root, "validation"), "pair-*-leg-*.json", SearchOption.AllDirectories);
if (paths.Length == 0) throw new InvalidDataException("No saved games to review");
Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, path =>
{
    using var metrics = JsonDocument.Parse(File.ReadAllText(path));
    using var stream = File.OpenRead(path + ".gz");
    using var zip = new GZipStream(stream, CompressionMode.Decompress);
    var saved = JsonSerializer.Deserialize<GameRecord>(zip, ArenaCommand.Json)!;
    var reports = metrics.RootElement.GetProperty("Players").EnumerateArray().Select(p =>
        JsonSerializer.Deserialize<Bots.StrongTacticalBot.TacticalReport[]>(p.GetProperty("Tactics").GetRawText(), ArenaCommand.Json)!).ToArray();
    var scripts = Enumerable.Range(0, 4).Select(s => new ReplayPlayer(saved.Players[s], saved.Rounds.SelectMany(r => r.Decisions).Where(d => d.Seat == s))).ToArray();
    var players = scripts.Select((script, seat) => new ReviewingPlayer(script, reports[seat], (view, legal, report, index, actual) =>
    {
        int seed = unchecked(saved.Seed + 97 * (seat + 1) + 7919 * (index + 1));
        var baseline = new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32, seed, view.Rules);
        var tactical = new Bots.StrongTacticalBot(seed, view.Rules);
        var before = baseline.Play(view, legal);
        var after = tactical.Play(view, legal);
        if (tactical.LastTactics != report) throw new InvalidDataException("Proof diagnostic changed during replay");
        rows.Add(new { Game = Path.GetRelativePath(root, path), Seat = seat, view.Round, PlayIndex = index,
            CounterfactualSeed = seed, report.Reason, Before = before, After = after, Actual = actual, Changed = before != after });
    })).ToArray();
    var replay = new Engine(saved.Rules).Run(players, saved.Seed, saved.MaxRounds, saved.InitialDealer);
    if (scripts.Any(s => !s.Complete) || players.Any(p => !p.Complete) || JsonSerializer.Serialize(saved, ArenaCommand.Json) != JsonSerializer.Serialize(replay, ArenaCommand.Json))
        throw new InvalidDataException("Review replay differs from saved game");
});
// Stable order regardless of parallel completion.
var ordered = rows.OrderBy(r => JsonSerializer.Serialize(r, ArenaCommand.Json)).ToArray();
File.WriteAllText(Path.Combine(root, "intervention-review.json"), JsonSerializer.Serialize(new { GamesReplayed = paths.Length, Queries = ordered.Length, Rows = ordered }, ArenaCommand.Json));
Console.WriteLine($"Reviewed {ordered.Length} interventions in {paths.Length} saved games without generating new matches");

sealed class ReviewingPlayer(ReplayPlayer script, Bots.StrongTacticalBot.TacticalReport[] reports,
    Action<Observation, IReadOnlyList<PlayAction>, Bots.StrongTacticalBot.TacticalReport, int, PlayAction> inspect) : IPlayer, IJokerDeclarationPlayer
{
    private int playIndex;
    public string Name => script.Name;
    public bool Complete => reports.Length == 0 || playIndex == reports.Length;
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => script.Bid(view, legal);
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count) => script.Discard(view, count);
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view) => script.DeclareJokers(view);
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var actual = script.Play(view, legal);
        if (reports.Length > 0)
        {
            var report = reports[playIndex];
            if (report.Reason != null) inspect(view, legal, report, playIndex, actual);
        }
        playIndex++;
        return actual;
    }
}
