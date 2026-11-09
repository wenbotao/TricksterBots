using System.Diagnostics;
using System.Text.Json;
using FiveHundredArena;
using Trickster.cloud;
using Bots = FiveHundredLab.Bots.FiveHundred;

string mode = args.Length > 0 ? args[0] : "validate";
string previous = "experiments/discard-refinement-v1-20261109/results";
string output = "experiments/discard-lean-v1-20261109/results";
Directory.CreateDirectory(output);
var json = ArenaCommand.Json;
var hands = JsonSerializer.Deserialize<Hand[]>(File.ReadAllText(Path.Combine(previous, "selected-hands.json")), json)!;
if (mode == "validate")
{
    int states = 0, rootSearches = 0;
    foreach (string path in Directory.GetFiles(Path.Combine(previous, "decisions"), "*.json").Order())
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));var row = doc.RootElement;
        var state = row.GetProperty("State");var hand = state.GetProperty("Hand").Deserialize<Hand>(json)!;
        var condition = state.GetProperty("Condition");
        var view = View(hand.Cards, condition.GetProperty("Tricks").GetInt32(), condition.GetProperty("Scores").Deserialize<int[]>(json)!);
        var proposal = row.GetProperty("Choices").GetProperty("original").Deserialize<PlayingCard[]>(json)!;
        var reference = Reference(hand.Seed);var lean = new Bots.StrongDiscardLeanBot(hand.Seed);
        var expected = reference.RefineProposedDiscard(view, proposal).ToArray();var actual = lean.RefineProposedDiscard(view, proposal).ToArray();
        Equal(expected, actual);Equal(row.GetProperty("Choices").GetProperty("v1").Deserialize<PlayingCard[]>(json)!, actual);
        GuardEqual(reference, lean);
        if (lean.LastWork!.FeatureEvaluations != 0 || lean.LastRollouts != 0) throw new InvalidDataException("Unexpected feature/audit work");
        states++;
    }
    foreach (var hand in hands)
    {
        var view = View(hand.Cards, 6, [0, 0]);var reference = Reference(hand.Seed);var lean = new Bots.StrongDiscardLeanBot(hand.Seed);
        Equal(reference.Discard(view, 3).ToArray(), lean.Discard(view, 3).ToArray());
        GuardEqual(reference, lean);SearchEqual(reference, lean);rootSearches++;
    }
    Save("equivalence.json", new { Passed = true, RepairStates = states, FullRootSearches = rootSearches, OrderedDiscardsIdentical = true, SearchReportsAndRolloutsIdentical = true, StructuralFeatureEvaluations = 0 });
    Console.WriteLine($"PASS {states} isolated repairs; {rootSearches} full root searches");
}
else if (mode == "bench")
{
    // Warm both implementations before collecting samples. Run serially after tests finish.
    foreach (bool lean in new[] { false, true }) Measure(hands[0], lean);
    var rows = new List<object>();var timesA = new List<double>();var timesB = new List<double>();
    var allocationsA = new List<long>();var allocationsB = new List<long>();
    int index = 0;
    foreach (var hand in hands.Take(8))
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Measurement a, b;
            if (index++ % 2 == 0) { a = Measure(hand, false); b = Measure(hand, true); }
            else { b = Measure(hand, true); a = Measure(hand, false); }
            Equal(a.Discard, b.Discard);
            if (a.Search != b.Search || a.Rollouts != b.Rollouts) throw new InvalidDataException("Search changed");
            timesA.Add(a.Milliseconds);timesB.Add(b.Milliseconds);allocationsA.Add(a.AllocatedBytes);allocationsB.Add(b.AllocatedBytes);
            rows.Add(new { Hand = hand.Id, Repeat = repeat, Reference = a, Lean = b, SameDecisionAndSearch = true });
            Console.WriteLine($"BENCH hand={hand.Id} repeat={repeat} reference={a.Milliseconds:F2}ms lean={b.Milliseconds:F2}ms bytes={a.AllocatedBytes}/{b.AllocatedBytes}");
        }
    Save("benchmark.json", new { Cases = 8, Repeats = 3, Pairs = rows.Count, AllDecisionsAndSearchesEqual = true,
        Method = "Serial, both policies warmed once; alternating pair order; same seeds, hands and observations; decision-body allocations on current thread; no forced GC",
        ReferenceMedianMs = Quantile(timesA, .5), LeanMedianMs = Quantile(timesB, .5), ReferenceP95Ms = Quantile(timesA, .95), LeanP95Ms = Quantile(timesB, .95),
        ReferenceMedianAllocatedBytes = Quantile(allocationsA.Select(x => (double)x), .5), LeanMedianAllocatedBytes = Quantile(allocationsB.Select(x => (double)x), .5), Rows = rows });
}
else throw new ArgumentException("validate|bench");

Bots.StrongDiscardRefinedBot Reference(int seed) => new(Bots.DiscardRefinementProfile.EfficientBaseline, seed);
void Save(string name, object data) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(data, json));
void Equal(PlayingCard[] expected, PlayingCard[] actual) { if (!expected.SequenceEqual(actual)) throw new InvalidDataException("Ordered discard changed"); }
void GuardEqual(Bots.StrongDiscardRefinedBot a, Bots.StrongDiscardLeanBot b)
{
    if (JsonSerializer.Serialize(a.LastDiscardGuard, json) != JsonSerializer.Serialize(b.LastDiscardGuard, json)) throw new InvalidDataException("Guard diagnostics changed");
}
void SearchEqual(Bots.StrongDiscardRefinedBot a, Bots.StrongDiscardLeanBot b)
{ if (a.LastDefaultSearch != b.LastDefaultSearch || a.LastRollouts != b.LastRollouts) throw new InvalidDataException("Search changed"); }
Measurement Measure(Hand hand, bool useLean)
{
    var view = View(hand.Cards, 6, [0, 0]);
    var reference = useLean ? null : Reference(hand.Seed);var lean = useLean ? new Bots.StrongDiscardLeanBot(hand.Seed) : null;
    long before = GC.GetAllocatedBytesForCurrentThread();var watch = Stopwatch.StartNew();
    var discard = useLean ? lean!.Discard(view, 3).ToArray() : reference!.Discard(view, 3).ToArray();
    watch.Stop();long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    return new(discard, useLean ? lean!.LastDefaultSearch : reference!.LastDefaultSearch, useLean ? lean!.LastRollouts : reference!.LastRollouts,
        useLean ? lean!.LastWork!.StaticEvaluations : reference!.LastWork!.StaticEvaluations,
        useLean ? lean!.LastWork!.FeatureEvaluations : reference!.LastWork!.FeatureEvaluations, watch.Elapsed.TotalMilliseconds, allocated);
}
static double Quantile(IEnumerable<double> items, double fraction)
{
    var values = items.Order().ToArray();double position = (values.Length - 1) * fraction;int lower = (int)position;
    return values[lower] + (values[Math.Min(lower + 1, values.Length - 1)] - values[lower]) * (position - lower);
}
static Observation View(PlayingCard[] hand, int tricks, int[] scores) => new(0, 3, 0, hand,
    Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, s == 0 ? 13 : 10, scores[s % 2], s == 0 ? new Contract?[] { new(tricks, Suit.Unknown) } : new Contract?[] { null }, 0, [], null)).ToArray(),
    [new(0, new(tricks, Suit.Unknown)), new(1, null), new(2, null), new(3, null)], new(tricks, Suit.Unknown), 0, [], [], scores, new Rules(), []);
record Hand(int Id, string Family, [property: System.Text.Json.Serialization.JsonPropertyName("Hand")] PlayingCard[] Cards, int Seed);
record Measurement(PlayingCard[] Discard, SearchReport? Search, int Rollouts, int StaticEvaluations, int FeatureEvaluations, double Milliseconds, long AllocatedBytes);
