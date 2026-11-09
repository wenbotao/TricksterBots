using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using FiveHundredLab.Usability;
using Trickster.cloud;
using Bots = FiveHundredLab.Bots.FiveHundred;

string root = args[0], mode = args[1];
Directory.CreateDirectory(root);
var json = ArenaCommand.Json;
IPlayer Create(string name, int seed) => name switch
{
    "all32" => new Bots.StrongAblationBot(Bots.StrongAblationProfile.All32, seed),
    "tactical" => new Bots.StrongTacticalBot(seed),
    "misere-only" => new Bots.StrongProofAblationBot(Bots.StrongProofProfile.MisereOnly, seed),
    "contract-only" => new Bots.StrongProofAblationBot(Bots.StrongProofProfile.ContractOnly, seed),
    _ => throw new ArgumentException(name)
};
if (mode == "audit")
{
    var rows = new List<object>();
    foreach (var fixture in UxEvaluationV1.Build())
    foreach (int seed in new[] { 71, 83, 97, 109 })
    foreach (string name in new[] { "all32", "tactical", "misere-only", "contract-only" })
    {
        var bot = Create(name, seed); var v = fixture.View;
        var action = fixture.Scenario.Phase switch
        {
            "bid" => new UxActionV1(Bid: bot.Bid(v, fixture.LegalBids)),
            "discard" => new UxActionV1(Discards: bot.Discard(v, fixture.DiscardCount).ToArray()),
            _ => new UxActionV1(Play: bot.Play(v, fixture.LegalPlays))
        };
        bool legal = fixture.Scenario.Phase switch
        {
            "bid" => fixture.LegalBids.Contains(action.Bid),
            "discard" => action.Discards!.Length == fixture.DiscardCount && action.Discards.Distinct().Count() == fixture.DiscardCount && action.Discards.All(v.Hand.Contains),
            _ => fixture.LegalPlays.Contains(action.Play!)
        };
        if (!legal) throw new IllegalActionException(fixture.Scenario.Id + "/" + name);
        rows.Add(new { Scenario = fixture.Scenario.Id, Phase = fixture.Scenario.Phase, Seed = seed, Profile = name,
            Action = action, Legal = legal, Checks = fixture.Scenario.Checks.Select(c => new { c.Id, c.Level, Pass = c.Accept(v, action) }).ToArray() });
    }
    File.WriteAllText(Path.Combine(root, "audit.json"), JsonSerializer.Serialize(rows, json));
    Console.WriteLine($"AUDIT {rows.Count}"); return;
}
if (mode != "deals") throw new ArgumentException("Use audit or deals");

// Declarer receives the second card of each round-robin packet for dealer=(declarer+2)%4.
var rules = new Rules();
var pool = Enumerable.Range(0, 256).Select(index =>
{
    int seed = 34119001 + index * 7919;
    var deck = rules.Deck(); new SeededRandom(seed).Shuffle(deck);
    var hand = Enumerable.Range(0, 10).Select(i => deck[4 * i + 1]).ToArray();
    double risk = hand.Sum(c => c.IsJoker ? 25 : Math.Max(0, c.Rank - 6) / (double)(1 + hand.Count(x => x.Suit == c.Suit && x.Rank < c.Rank)));
    return new SeedCase(index, seed, risk, hand);
}).ToArray();
var selected = pool.Take(8).Select((s, slot) => (Seed: s, Slot: slot, Cohort: "unfiltered"))
    .Concat(pool.Skip(8).OrderBy(s => s.Risk).ThenBy(s => s.Index).Take(8).Select((s, slot) => (Seed: s, Slot: slot + 8, Cohort: "lower-risk"))).ToArray();
var cases = selected.SelectMany(s => new[] { ContractKind.Misere, ContractKind.OpenMisere }.Select(kind =>
    new DealCase($"{kind}-{s.Cohort}-{s.Slot:D2}", s.Cohort, s.Seed, s.Slot % 4, new(0, Suit.Unknown, kind))))
    .Concat(selected.Take(8).Select(s => new DealCase($"Normal-unfiltered-{s.Slot:D2}", s.Cohort, s.Seed, s.Slot % 4, new(7, Rules.Suits[s.Slot % 4])))).ToArray();
var jobs = cases.SelectMany(c => new[] { "baseline", "declarer-proof", "defense-proof" }.Select(arm => new Job(c, arm))).ToList();
new SeededRandom(34119999).Shuffle(jobs);
string plan = JsonSerializer.Serialize(new { Workers = 4, SeedPool = pool, Selected = selected.Select(s => new { s.Seed.Index, s.Seed.Seed, s.Seed.Risk, s.Slot, s.Cohort }), Cases = cases, Jobs = jobs }, json);
string planPath = Path.Combine(root, "plan.json");
if (File.Exists(planPath) && File.ReadAllText(planPath) != plan) throw new InvalidDataException("Plan mismatch");
File.WriteAllText(planPath, plan);
string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Bots.StrongProofAblationBot).Assembly.Location)));
string hashPath = Path.Combine(root, "strategy-version.txt");
if (File.Exists(hashPath) && File.ReadAllText(hashPath) != hash) throw new InvalidDataException("Strategy assembly changed");
File.WriteAllText(hashPath, hash);
int completed = 0, skipped = 0;
Parallel.ForEach(Partitioner.Create(jobs, EnumerablePartitionerOptions.NoBuffering), new ParallelOptions { MaxDegreeOfParallelism = 4 }, job =>
{
    var c = job.Case;
    string directory = Path.Combine(root, "deals", c.Id); Directory.CreateDirectory(directory);
    string prefix = Path.Combine(directory, job.Arm);
    if (File.Exists(prefix + ".json"))
    {
        using var saved = JsonDocument.Parse(File.ReadAllText(prefix + ".json"));
        if (saved.RootElement.GetProperty("Case").GetProperty("Id").GetString() != c.Id ||
            saved.RootElement.GetProperty("Arm").GetString() != job.Arm || !File.Exists(prefix + ".json.gz")) throw new InvalidDataException("Checkpoint mismatch");
        Interlocked.Increment(ref skipped); return;
    }
    var players = Enumerable.Range(0, 4).Select(seat =>
    {
        bool subject = job.Arm == "declarer-proof" && seat == c.Declarer || job.Arm == "defense-proof" && seat % 2 != c.Declarer % 2;
        string profile = subject ? c.Contract.IsMisere ? "misere-only" : "contract-only" : "all32";
        return new ConditionedPlayer(Create(profile, unchecked(c.Seed.Seed + 97 * (seat + 1))), c);
    }).ToArray();
    var watch = Stopwatch.StartNew();
    var game = new Engine(rules).Run(players, c.Seed.Seed, 1, (c.Declarer + 2) % 4);
    watch.Stop();
    var deal = game.Rounds.Single();
    if (deal.Redeal || deal.Contract != c.Contract || deal.Declarer != c.Declarer || deal.TrickWinners.Count != 10 || !deal.Hands[c.Declarer].SequenceEqual(c.Seed.Hand))
        throw new InvalidDataException("Incomplete or mismatched conditioned deal");
    using (var file = File.Create(prefix + ".json.gz"))
    using (var zip = new GZipStream(file, CompressionLevel.Fastest)) JsonSerializer.Serialize(zip, game, json);
    var metrics = new { Case = c, job.Arm, deal.Made, deal.Tricks, deal.ScoreDelta,
        Discards = deal.Decisions.Single(d => d.Phase == "discard").Discards, CompletedDeal = true,
        ExpectedMatchTruncated = game.Truncated, Seconds = watch.Elapsed.TotalSeconds,
        Players = players.Select((p, seat) => new { Seat = seat, p.Name, p.PlayCalls, p.PlayMilliseconds, p.Tactics }).ToArray() };
    File.WriteAllText(prefix + ".json.tmp", JsonSerializer.Serialize(metrics, json)); File.Move(prefix + ".json.tmp", prefix + ".json");
    Console.WriteLine($"DEAL {Interlocked.Increment(ref completed)} {c.Id} {job.Arm} made={deal.Made}");
});
Console.WriteLine($"COMPLETE {completed}, skipped {skipped}");

record SeedCase(int Index, int Seed, double Risk, PlayingCard[] Hand);
record DealCase(string Id, string Cohort, SeedCase Seed, int Declarer, Contract Contract);
record Job(DealCase Case, string Arm);
record TacticalEvent(int Turn, string? Reason, int OriginalCandidates, int RetainedCandidates, int ProofNodes, PlayAction Action);
sealed class ConditionedPlayer(IPlayer inner, DealCase fixture) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => "conditioned/" + inner.Name;
    public int PlayCalls { get; private set; }
    public double PlayMilliseconds { get; private set; }
    public List<TacticalEvent> Tactics { get; } = [];
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        Contract? bid = view.Auction.Count == 0 && fixture.Contract.IsMisere
            ? new(fixture.Contract.Kind == ContractKind.OpenMisere ? 8 : 7, Suit.Spades)
            : view.Seat == fixture.Declarer && !view.Auction.Any(a => a.Seat == fixture.Declarer && a.Bid != null) ? fixture.Contract : null;
        if (!legal.Contains(bid)) throw new IllegalActionException("Conditioned auction is not legal");
        return bid;
    }
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count) => inner.Discard(view, count);
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view) => inner is IJokerDeclarationPlayer p ? p.DeclareJokers(view) : [];
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var watch = Stopwatch.StartNew(); var move = inner.Play(view, legal); watch.Stop();
        PlayCalls++; PlayMilliseconds += watch.Elapsed.TotalMilliseconds;
        if (inner is Bots.StrongProofAblationBot bot && bot.LastTactics is { } t)
            Tactics.Add(new(view.History.Count, t.Reason, t.OriginalCandidates, t.RetainedCandidates, t.ProofNodes, move));
        return move;
    }
}
