using FiveHundredArena;
namespace FiveHundredLab;

// Conservative rank pruning: current-trick cards remain barriers even though they are public.
internal static class FastPruningV8
{
    internal static int WinnerAfter(Observation v, PlayAction move) =>
        CardTableV1.Get(v.Rules, v.Contract!).Winner(v.Trick.Append(new PublicPlay(v.Seat, move)).ToArray(), v.Contract!);

    internal static IReadOnlyList<PlayAction> Representatives(Observation v, IReadOnlyList<PlayAction> legal, PlayAction baseline)
    {
        if (v.Contract!.IsMisere) return legal;
        var frame = new CardFrameV1(v);
        ulong barriers = frame.Unseen;
        foreach (var played in v.Trick) barriers |= frame.Table.Bit(played.Action.Card);
        var groups = new List<List<PlayAction>>();
        foreach (var move in legal.OrderBy(p => frame.Table.Suit(p.Card)).ThenBy(p => frame.Table.Strength(p.Card)))
        {
            var group = groups.LastOrDefault();
            bool merge = group != null && !move.Card.IsJoker && !group[^1].Card.IsJoker && frame.Table.Suit(group[^1].Card) == frame.Table.Suit(move.Card);
            if (merge)
            {
                int lo = frame.Table.Strength(group![^1].Card), hi = frame.Table.Strength(move.Card);
                merge = !frame.Table.Deck.Any(c => (barriers & frame.Table.Bit(c)) != 0 && frame.Table.Suit(c) == frame.Table.Suit(move.Card) && frame.Table.Strength(c) > lo && frame.Table.Strength(c) < hi);
                if (merge && v.Trick.Count > 0) merge = WinnerAfter(v, group[^1]) == WinnerAfter(v, move);
            }
            if (merge) group!.Add(move); else groups.Add([move]);
        }
        return groups.Select(g => g.Contains(baseline) ? baseline : g[0]).ToArray();
    }

    internal static IReadOnlyList<PlayAction> LastSeatChoices(Observation v, IReadOnlyList<PlayAction> legal, PlayAction baseline)
    {
        // Retain the cheap specialist in Misere, and never search a forced move.
        if (v.Contract!.IsMisere || v.Hand.Count <= 1 || legal.Count <= 1) return [baseline];
        var frame = new CardFrameV1(v);
        int declaringTeam = v.Declarer!.Value % 2;
        int won = v.Players.Where(p => p.Seat % 2 == declaringTeam).Sum(p => p.Tricks);
        int needed = v.Contract.Tricks - won;
        bool close = needed is > 0 and <= 2 || v.Hand.Count <= 3;
        bool entry = v.Hand.GroupBy(frame.Table.Suit).Any(g => g.Key != (int)v.Contract.Trump && g.Count() >= 3 && g.Count(frame.Boss) >= 2);
        if (!close && !entry) return [baseline];
        int winner = WinnerAfter(v, baseline);
        double Cost(PlayAction p) => p.Card.IsJoker ? 1000 : frame.Table.Suit(p.Card) == (int)v.Contract.Trump ? 200 + frame.Table.Strength(p.Card) : frame.Table.Strength(p.Card);
        var alternative = legal.Where(p => WinnerAfter(v, p) != winner).OrderBy(Cost).FirstOrDefault();
        return alternative == null ? [baseline] : [baseline, alternative];
    }
}

internal sealed record WeightedEvidenceV8(double Mean, double EffectiveSamples, double StandardError, double Improvement);
internal static class RiskEstimateV8
{
    internal static WeightedEvidenceV8 Evaluate(IReadOnlyList<double> deltas, IReadOnlyList<double> weights)
    {
        if (deltas.Count == 0 || deltas.Count != weights.Count || deltas.Any(x => !double.IsFinite(x)) || weights.Any(w => !double.IsFinite(w) || w <= 0)) throw new ArgumentException("Finite deltas and positive weights required");
        double sum = weights.Sum(), effective = sum * sum / weights.Sum(w => w * w);
        double mean = deltas.Select((d, i) => d * weights[i]).Sum() / sum;
        double variance = deltas.Select((d, i) => weights[i] * Math.Pow(d - mean, 2)).Sum() / sum;
        double error = Math.Sqrt(variance / Math.Max(1, effective - 1));
        // A heavily dominated sample should not overturn the cheap baseline.
        double improvement = effective < 1.5 ? double.NegativeInfinity : mean - 8 - .75 * error;
        return new(mean, effective, error, improvement);
    }
}
