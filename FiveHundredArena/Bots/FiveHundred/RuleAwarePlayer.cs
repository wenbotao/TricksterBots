using System.Text.Json.Nodes;
using Trickster.cloud;
namespace FiveHundredArena;

public enum RuleBotLevel
{
    Random, Weak, Medium, Fast, Strong, Partner
}

/// <summary>Versioned rule-aware policies. Legacy policies are delegated to unchanged for unconfigured games.</summary>
public sealed class RuleAwarePlayer : IPlayer, IJokerDeclarationPlayer
{
    private readonly IPlayer legacy;
    private readonly Rules bound;
    private readonly SeededRandom random;
    private readonly RuleBotLevel level;
    private readonly int samples;
    public string Name => $"{level.ToString().ToLowerInvariant()}-rules-v1" + (samples != (level == RuleBotLevel.Strong ? 8 : 4) ? $"-s{samples}" : "");
    public int LastRollouts
    {
        get; private set;
    }
    public RuleAwarePlayer(RuleBotLevel level, int seed, IPlayer legacy, Rules? rules = null, int? sampleCount = null)
    {
        samples = sampleCount ?? (level == RuleBotLevel.Strong ? 8 : 4);
        if (samples is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        this.level = level;
        this.legacy = legacy;
        bound = rules ?? new Rules();
        bound.Validate();
        random = new(seed);
    }
    public static RuleAwarePlayer FromFile(RuleBotLevel level, int seed, IPlayer legacy, string? path = null) => new(level, seed, legacy, RuleConfiguration.Load(path));
    private ConfiguredRules? RequireMatchingRules(Observation view)
    {
        if (bound is ConfiguredRules expected)
        {
            if (view.Rules is not ConfiguredRules actual || (actual.Document != expected.Document && !JsonNode.DeepEquals(JsonNode.Parse(expected.Document), JsonNode.Parse(actual.Document))))
                throw new ArgumentException("Bot and engine rule configurations differ");
            return actual;
        }
        if (view.Rules is ConfiguredRules || view.Rules != bound)
            throw new ArgumentException("Bot and engine rules differ");
        return null;
    }
    // Inkle is information only; ordinary contracts compete by configured expected score.
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return legacy.Bid(view, legal);
        if (level == RuleBotLevel.Random)
            return legal[random.Next(legal.Count)];
        if (rules.PassIsPermanent && view.Declarer == (view.Seat + 2) % 4 && view.Contract is { } current && view.Scores[view.Seat % 2] + rules.Value(current) >= rules.WinScore && view.Players.Where(p => p.Seat % 2 != view.Seat % 2).All(p => view.Auction.Any(a => a.Seat == p.Seat && a.Bid == null)))
            return null;
        Contract? best = null;
        double bestUtility = 0;
        foreach (var bid in legal.Where(b => b != null).Select(b => b!))
        {
            if (bid.Kind == ContractKind.Inkle)
                continue;
            if (bid.IsMisere)
            {
                if (level == RuleBotLevel.Weak)
                    continue;
                double risk = view.Hand.Sum(c => Danger(view, c) / (1 + view.Hand.Count(x => x.Suit == c.Suit && Danger(view, x) < Danger(view, c))));
                if (risk > 27)
                    continue;
                double p = Math.Clamp(.85 - risk / 100, .05, .95);
                var made = new int[4];
                made[(view.Seat + 1) % 4] = 10;
                var failed = new int[4];
                failed[view.Seat] = 1;
                failed[(view.Seat + 1) % 4] = 9;
                double misereUtility = p * RuleRollout.Utility(view, rules, bid, view.Seat, made) + (1 - p) * RuleRollout.Utility(view, rules, bid, view.Seat, failed);
                if (misereUtility > bestUtility)
                {
                    bestUtility = misereUtility;
                    best = bid;
                }
                continue;
            }
            double estimate = HandValue(view.Hand, bid, rules) + (level == RuleBotLevel.Weak ? 1.2 : 2.1);
            var support = view.Auction.LastOrDefault(a => a.Seat == (view.Seat + 2) % 4 && a.Bid?.Kind == ContractKind.Normal)?.Bid;
            if (support?.Trump == bid.Trump)
                estimate += .8;
            if (estimate < bid.Tricks + (level == RuleBotLevel.Weak ? .6 : 0))
                continue;
            double probability = Math.Clamp(.65 + (estimate - bid.Tricks) * .12, .1, .97);
            var success = new int[4];
            success[view.Seat] = bid.Tricks;
            success[(view.Seat + 1) % 4] = 10 - bid.Tricks;
            var failure = new int[4];
            failure[view.Seat] = bid.Tricks - 1;
            failure[(view.Seat + 1) % 4] = 11 - bid.Tricks;
            double utility = probability * RuleRollout.Utility(view, rules, bid, view.Seat, success) + (1 - probability) * RuleRollout.Utility(view, rules, bid, view.Seat, failure);
            if (utility > bestUtility)
            {
                bestUtility = utility;
                best = bid;
            }
        }
        if (best != null)
            return best;
        var inkle = legal.Where(c => c?.Kind == ContractKind.Inkle).Select(c => c!).OrderByDescending(c => view.Hand.Count(x => rules.EffectiveSuit(x, c) == c.Trump)).FirstOrDefault();
        return inkle != null && view.Hand.Count(c => rules.EffectiveSuit(c, inkle) == inkle.Trump) >= 4 ? inkle : null;
    }
    internal static double HandValue(IReadOnlyList<PlayingCard> hand, Contract c, ConfiguredRules rules)
    {
        return AdjustJokerValue(hand, c, rules, HandEvaluator.HandValue(hand, c, rules));
    }
    private static double AdjustJokerValue(IReadOnlyList<PlayingCard> hand, Contract c, ConfiguredRules rules, double value)
    {
        if (!c.IsMisere && c.Trump == Suit.Unknown && hand.Any(x => x.IsJoker))
        {
            value -= 1.2;
            bool high = ConfiguredRules.Text(rules.ContractOptions(c).GetProperty("joker"), "unassignedRank") == "highest";
            value += hand.Where(x => x.IsJoker).Sum(x => rules.Declarations.ContainsKey(x) ? .9 : high ? 1.2 : 0);
        }
        return value;
    }
    internal static double RetainedValue(IReadOnlyList<PlayingCard> hand, Contract c, ConfiguredRules rules)
    {
        // The same base estimate was previously recomputed while applying the Joker correction.
        double baseValue = HandEvaluator.HandValue(hand, c, rules);
        return HandEvaluator.RetainedValue(hand, c, rules) - baseValue + AdjustJokerValue(hand, c, rules, baseValue);
    }
    internal static double Danger(Observation view, PlayingCard c)
    {
        if (c.IsJoker && view.Rules is ConfiguredRules rules && ConfiguredRules.Text(rules.ContractOptions(view.Contract ?? new(0, Suit.Unknown, ContractKind.Misere)).GetProperty("joker"), "unassignedRank") == "lowest" && !rules.Declarations.ContainsKey(c))
            return 0;
        return c.IsJoker ? 30 : c.Rank;
    }
    // Rank retained hands first, then spend the fixed search budget on the best candidates.
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return legacy.Discard(view, count);
        if (count == 0)
            return Array.Empty<PlayingCard>();
        if (level == RuleBotLevel.Random)
        {
            var cards = view.Hand.ToArray();
            random.Shuffle(cards);
            return cards.Take(count).ToArray();
        }
        if (level == RuleBotLevel.Weak)
            return view.Hand.OrderBy(c => view.Contract!.IsMisere ? -Danger(view, c) : c.IsJoker ? (Danger(view, c) > 0 ? 100 : 0) : rules.EffectiveSuit(c, view.Contract) == view.Contract.Trump ? 50 + rules.Rank(c, view.Contract) : c.Rank).Take(count).ToArray();
        var danger = view.Hand.ToDictionary(card => card, card => Danger(view, card));
        double Value(PlayingCard[] discard)
        {
            var kept = view.Hand.Except(discard).ToArray();
            return view.Contract!.IsMisere ? -kept.Sum(c => danger[c] / Math.Max(1, kept.Count(x => x.Suit == c.Suit && danger[x] <= danger[c]))) : RetainedValue(kept, view.Contract, rules);
        }
        var choices = HandEvaluator.DiscardSets(view.Hand, count).OrderByDescending(Value).Take(level == RuleBotLevel.Strong ? 12 : level is RuleBotLevel.Fast or RuleBotLevel.Partner ? 4 : 1).ToArray();
        if (level == RuleBotLevel.Medium)
            return choices[0];
        var selected = SelectByRollout(view, rules, choices, (world, move) => RuleRollout.Discard(view, rules, world, move));
        return level == RuleBotLevel.Partner ? RuleTactics.RepairDiscards(view, rules, selected) : selected;
    }
    // Tactical certainties precede sampled search for the partner-oriented strategy.
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return legacy.Play(view, legal);
        if (level == RuleBotLevel.Random)
            return legal[random.Next(legal.Count)];
        if (legal.Count == 1)
            return legal[0];
        if (level == RuleBotLevel.Partner && view.Contract!.IsMisere && view.Declarer == view.Seat && view.Trick.Count == 0)
        {
            var safe = legal.Where(p => !RuleTactics.GuaranteedLead(view, rules, p)).ToArray();
            if (safe.Length > 0)
                legal = safe;
        }
        var baseline = RuleTactics.Play(view, rules, legal, level == RuleBotLevel.Weak);
        if (level is RuleBotLevel.Weak or RuleBotLevel.Medium)
            return baseline;
        if (level == RuleBotLevel.Partner && RuleTactics.CertainAction(view, rules, legal) is { } certain)
            return certain;
        var choices = legal.OrderBy(p => p == baseline ? 0 : 1).Take(level == RuleBotLevel.Strong ? legal.Count : 4).ToArray();
        return SelectByRollout(view, rules, choices, (world, move) => RuleRollout.Play(view, rules, world, move));
    }
    private T SelectByRollout<T>(Observation view, ConfiguredRules rules, T[] choices, Func<SampledDeal, T, double> evaluate)
    {
        if (choices.Length == 1)
            return choices[0];
        int count = samples;
        var worlds = RuleSamples.Sample(view, rules, count, random);
        var values = new double[choices.Length];
        foreach (var world in worlds)
            for (int i = 0; i < choices.Length; i++)
            {
                values[i] += evaluate(world, choices[i]);
                LastRollouts++;
            }
        return choices[Array.IndexOf(values, values.Max())];
    }
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view)
    {
        RequireMatchingRules(view);
        if (level == RuleBotLevel.Random)
        {
            var named = new List<PlayAction>();
            foreach (var card in view.Hand.Where(c => c.IsJoker))
            {
                int choice = random.Next(5);
                if (choice > 0)
                    named.Add(new(card, Rules.Suits[choice - 1]));
            }
            return named;
        }
        // Deliberately decline optional naming: avoids accidentally converting a universally high Joker
        // into a side-suit card. All policies still understand declarations made by other players.
        return Array.Empty<PlayAction>();
    }
}
