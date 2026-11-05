using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiveHundredArena;

return ArenaCommand.Run(args);

public static class ArenaCommand
{
    public static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter() } };
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("FiveHundredArena run [--pairs 100] [--seed 1] [--a medium] [--b random-legal] [--strong-samples 8] [--max-rounds 200] [--out arena-results] [--rules profile.json] [--assembly plugin.dll]\nFiveHundredArena replay games.jsonl\nEach pair plays the same deals twice, exchanging teams. Plugins implement IPlayer with a public parameterless constructor.");
                return 0;
            }
            if (args[0] == "replay")
            {
                if (args.Length != 2) throw new ArgumentException("replay requires one JSONL path");
                int count = 0;
                foreach (var line in File.ReadLines(args[1]))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var saved = JsonSerializer.Deserialize<GameRecord>(line, Json) ?? throw new InvalidDataException("Empty game");
                    Replay(saved); count++;
                }
                if (count == 0) throw new InvalidDataException("No games to replay");
                Console.WriteLine($"Validated {count} games by replaying all legal decisions and comparing deals, trick winners and scores.");
                return 0;
            }
            if (args[0] != "run" || args.Length % 2 == 0) throw new ArgumentException("Expected run followed by --option value pairs");
            var known = new[] { "--pairs", "--seed", "--a", "--b", "--max-rounds", "--out", "--rules", "--assembly", "--strong-samples" };
            var options = new Dictionary<string, string>();
            for (int i = 1; i < args.Length; i += 2)
            {
                if (!known.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException($"Unknown or duplicate option {args[i]}");
            }
            string Get(string key, string fallback) => options.GetValueOrDefault(key, fallback);
            int pairs = int.Parse(Get("--pairs", "100")), seed = int.Parse(Get("--seed", "1")), maxRounds = int.Parse(Get("--max-rounds", "200"));
            if (pairs < 1 || maxRounds < 1) throw new ArgumentException("pairs and max-rounds must be positive");
            string a = Get("--a", "medium"), b = Get("--b", "random-legal"), output = Path.GetFullPath(Get("--out", "arena-results"));
            var rules = options.TryGetValue("--rules", out var path) ? RuleConfiguration.Load(path) : new Rules();
            rules.Validate();
            if (options.TryGetValue("--assembly", out var assembly)) Assembly.LoadFrom(Path.GetFullPath(assembly));
            var strongOptions = new StrongBotOptions { Samples = int.Parse(Get("--strong-samples", "8")) };
            strongOptions.Validate();
            IPlayer Legacy(string spec, int playerSeed) => spec switch
            {
                "medium" => new MediumBotPlayer(), "weak" => new WeakBot(),
                "strong" => new StrongBot(playerSeed, strongOptions), "random-legal" => new RandomLegalBot(playerSeed),
                "existing" => new ExistingBotPlayer(), "random" => new RandomPlayer("random", playerSeed),
                _ => (IPlayer)(Activator.CreateInstance(AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(spec)).FirstOrDefault(t => t != null) ?? throw new ArgumentException($"Unknown player {spec}")) ?? throw new ArgumentException("Cannot instantiate player"))
            };
            IPlayer Create(string spec,int playerSeed)
            {
                if(rules is not ConfiguredRules)return Legacy(spec,playerSeed);
                var level=spec switch{"random" or "random-legal"=>RuleBotLevel.Random,"weak"=>RuleBotLevel.Weak,"medium" or "existing"=>RuleBotLevel.Medium,"strong"=>RuleBotLevel.Strong,"fast"=>RuleBotLevel.Fast,"partner"=>RuleBotLevel.Partner,_=>throw new ArgumentException("Unknown rule-aware player "+spec)};
                return new RuleAwarePlayer(level,playerSeed,spec is "fast" or "partner"?new MediumBotPlayer():Legacy(spec,playerSeed),rules,sampleCount:level==RuleBotLevel.Strong?strongOptions.Samples:null);
            }
            Directory.CreateDirectory(output);
            string gamesPath = Path.Combine(output, "games.jsonl");
            // Never overwrite a previous experiment, including a partially failed one.
            using var games = new StreamWriter(new FileStream(gamesPath, FileMode.CreateNew));
            var summary = new ExperimentSummary { A = a, B = b, Seed = seed, PairsRequested = pairs, Rules = rules, Runtime = Environment.Version.ToString(), AssemblyVersion = typeof(Engine).Assembly.GetName().Version?.ToString() ?? "" };
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.IsDynamic && !string.IsNullOrEmpty(x.Location)))
                if (asm == typeof(Engine).Assembly || asm == typeof(Trickster.Bots.FiveHundredBot).Assembly || asm == typeof(Trickster.cloud.Card).Assembly || (options.TryGetValue("--assembly", out var plugin) && asm.Location == Path.GetFullPath(plugin)))
                    summary.AssemblySha256[Path.GetFileName(asm.Location)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asm.Location)));
            var paired = new List<double>();
            var watch = Stopwatch.StartNew();
            for (int pair = 0; pair < pairs; pair++)
            {
                double wins = 0; bool completePair = true;
                for (int leg = 0; leg < 2; leg++)
                {
                    int gameSeed = unchecked(seed + pair * 7919);
                    var bots = Enumerable.Range(0, 4).Select(s => (IPlayer)new TimedPlayer(Create(s % 2 == leg ? a : b, unchecked(gameSeed + 97 * (s + 1))), s % 2 == leg ? summary.TimingA : summary.TimingB)).ToArray();
                    GameRecord record;
                    try { record = new Engine(rules).Run(bots, gameSeed, maxRounds, pair % 4); }
                    catch (Exception ex)
                    {
                        var failure = new { Pair = pair, Leg = leg, Seed = gameSeed, Error = ex.ToString(), PartialGame = ex.Data["record"] as GameRecord };
                        File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(failure, Json));
                        throw new InvalidOperationException($"Experiment aborted at pair {pair}, leg {leg}. See {output}/failure.json; no invalid game counted.", ex);
                    }
                    games.WriteLine(JsonSerializer.Serialize(record, Json)); games.Flush();
                    summary.Games++;
                    summary.FinalScoreTotalA += record.Rounds.Last().Scores[leg];
                    summary.FinalScoreTotalB += record.Rounds.Last().Scores[1 - leg];
                    if (record.Truncated) { summary.Truncated++; completePair = false; }
                    else if (record.Winner == leg) { summary.AWins++; wins++; } else summary.BWins++;
                    foreach (var round in record.Rounds)
                    {
                        summary.Deals++;
                        if (round.Redeal) { summary.Redeals++; continue; }
                        string key = $"{(round.Declarer % 2 == leg ? "A" : "B")}:{round.Contract}";
                        if (!summary.Contracts.TryGetValue(key, out var stat)) summary.Contracts[key] = stat = new();
                        stat.Attempts++; if (round.Made) stat.Made++;
                        stat.Tricks += round.Tricks[round.Declarer!.Value] + (round.Contract!.IsMisere ? 0 : round.Tricks[(round.Declarer.Value + 2) % 4]);
                        stat.Points += round.ScoreDelta[round.Declarer.Value % 2];
                    }
                }
                if (completePair) paired.Add(wins / 2);
            }
            summary.ElapsedSeconds = watch.Elapsed.TotalSeconds;
            summary.CompletedPairs = paired.Count;
            summary.PairedWinRate = paired.Count == 0 ? null : paired.Average();
            if (paired.Count > 0)
            {
                double mean = paired.Average(), radius = Math.Sqrt(Math.Log(40) / (2 * paired.Count));
                summary.Pair95HoeffdingInterval = [Math.Max(0, mean - radius), Math.Min(1, mean + radius)];
            }
            // Pair is the independent sampling unit; do not treat the two duplicate games as independent.
            if (paired.Count > 1)
            {
                double mean = paired.Average(), se = Math.Sqrt(paired.Sum(x => (x - mean) * (x - mean)) / (paired.Count - 1) / paired.Count);
                summary.PairStandardError = se;
            }
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions(Json) { WriteIndented = true }));
            Console.WriteLine($"Games={summary.Games}, A wins={summary.AWins}, B wins={summary.BWins}, truncated={summary.Truncated}, deals={summary.Deals}, complete pairs={summary.CompletedPairs}");
            Console.WriteLine($"Results: {output}"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    public static void Replay(GameRecord saved)
    {
        if (saved.SchemaVersion != 1 || saved.EngineVersion != "1") throw new InvalidDataException("Unsupported replay version");
        if (saved.Rules.GetType() != typeof(Rules) && saved.Rules is not ConfiguredRules) throw new InvalidDataException("Custom rule subclasses require their own replay resolver");
        var bots = Enumerable.Range(0, 4).Select(s => new ReplayPlayer(saved.Players[s], saved.Rounds.SelectMany(r => r.Decisions).Where(d => d.Seat == s))).ToArray();
        var actual = new Engine(saved.Rules).Run(bots, saved.Seed, saved.MaxRounds, saved.InitialDealer);
        if (bots.Any(b => !b.Complete) || JsonSerializer.Serialize(actual, Json) != JsonSerializer.Serialize(saved, Json)) throw new InvalidDataException("Replay diverged from recorded game");
    }
}

public sealed class DecisionTiming
{
    public long Decisions { get; set; }
    public double Milliseconds { get; set; }
}
public sealed class TimedPlayer(IPlayer inner, DecisionTiming timing) : IPlayer, IJokerDeclarationPlayer
{
    public string Name => inner.Name;
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view)=>inner is IJokerDeclarationPlayer p?p.DeclareJokers(view):[];
    private T Time<T>(Func<T> action) { var watch = Stopwatch.StartNew(); try { return action(); } finally { timing.Decisions++; timing.Milliseconds += watch.Elapsed.TotalMilliseconds; } }
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => Time(() => inner.Bid(v, legal));
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count) => Time(() => inner.Discard(v, count));
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal) => Time(() => inner.Play(v, legal));
}
public sealed class ContractStatistics
{
    public int Attempts { get; set; }
    public int Made { get; set; }
    public long Tricks { get; set; }
    public long Points { get; set; }
}
public sealed class ExperimentSummary
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public int Seed { get; set; }
    public int PairsRequested { get; set; }
    public Rules Rules { get; set; } = new();
    public string Runtime { get; set; } = "";
    public string AssemblyVersion { get; set; } = "";
    public int Games { get; set; }
    public int AWins { get; set; }
    public int BWins { get; set; }
    public int Truncated { get; set; }
    public long FinalScoreTotalA { get; set; }
    public long FinalScoreTotalB { get; set; }
    public Dictionary<string, string> AssemblySha256 { get; set; } = [];
    public int Deals { get; set; }
    public int Redeals { get; set; }
    public int CompletedPairs { get; set; }
    public double? PairedWinRate { get; set; }
    public double? PairStandardError { get; set; }
    public double[]? Pair95HoeffdingInterval { get; set; }
    public double ElapsedSeconds { get; set; }
    public DecisionTiming TimingA { get; set; } = new();
    public DecisionTiming TimingB { get; set; } = new();
    public Dictionary<string, ContractStatistics> Contracts { get; set; } = [];
}
