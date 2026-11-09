using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FiveHundredArena;
using Trickster.cloud;
using Bots = FiveHundredLab.Bots.FiveHundred;

// Fixed unseen seeds, unfiltered hands; observations are captured after a legal forced 6NT auction.
string output = args[0]; Directory.CreateDirectory(output);
var plan = new { Queries = 64, SeedBase = 45119001, Step = 7919, Workers = 2,
    Contract = "6NT", Purpose = "Decision diagnostics, no win-rate measurement; no tuning" };
string planPath = Path.Combine(output, "discard-probe-plan.json");
string planText = JsonSerializer.Serialize(plan, ArenaCommand.Json);
if (File.Exists(planPath) && File.ReadAllText(planPath) != planText) throw new InvalidDataException("Probe plan mismatch");
File.WriteAllText(planPath, planText);
var rows = new ConcurrentBag<object>();
Parallel.For(0, plan.Queries, new ParallelOptions { MaxDegreeOfParallelism = plan.Workers }, index =>
{
    int seed = plan.SeedBase + index * plan.Step; bool captured = false;
    var probe = new CapturePlayer(view =>
    {
        var baseline = new Bots.StrongBidGuardBot(seed ^ 713);
        var subject = new Bots.StrongHandGuardBot(seed ^ 713);
        var watch = Stopwatch.StartNew(); var before = baseline.Discard(view, 3).ToArray(); watch.Stop();
        double beforeMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart(); var after = subject.Discard(view, 3).ToArray(); watch.Stop();
        var report = subject.LastDiscardGuard!;
        if (!before.ToHashSet().SetEquals(report.Original)) throw new InvalidDataException("Underlying search changed");
        if (after.Length != 3 || after.Distinct().Count() != 3 || !after.All(view.Hand.Contains)) throw new IllegalActionException("Illegal probe discard");
        var controls = view.Hand.Except(before).Where(c => c.IsJoker || c.Rank == 14);
        if (controls.Any(after.Contains)) throw new InvalidDataException("Repair sacrificed retained ace/Joker");
        rows.Add(new { Index = index, Seed = seed, Hand = view.Hand, Before = before, After = after,
            report.Changed, BaselineMilliseconds = beforeMs, SubjectMilliseconds = watch.Elapsed.TotalMilliseconds });
        captured = true;
    });
    try { new Engine().Run([probe, probe, probe, probe], seed, 1, 3); }
    catch (IllegalActionException ex) when (captured && ex.InnerException is ProbeStop) { }
    if (!captured) throw new InvalidDataException("Probe never reached legal discard phase");
});
File.WriteAllText(Path.Combine(output,"discard-probe.json"), JsonSerializer.Serialize(rows.OrderBy(x => JsonSerializer.Serialize(x,ArenaCommand.Json)).ToArray(),ArenaCommand.Json));
Console.WriteLine($"Captured and checked {rows.Count} independent legal discard observations");

sealed class ProbeStop : Exception;
sealed class CapturePlayer(Action<Observation> capture) : IPlayer
{
    public string Name => "legal-discard-probe";
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => view.Auction.Count == 0 ? new(6,Suit.Unknown) : null;
    public IReadOnlyList<PlayingCard> Discard(Observation view,int count) { capture(view); throw new ProbeStop(); }
    public PlayAction Play(Observation view,IReadOnlyList<PlayAction> legal) => throw new InvalidOperationException("Probe must stop at discard");
}
