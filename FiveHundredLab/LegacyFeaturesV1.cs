using Trickster.cloud;

using FiveHundredArena;

namespace FiveHundredLab;

// Cheap candidate ranking; final choices are evaluated by information-limited Medium continuations.
internal static class LegacyFeaturesV1
{
    internal static double HandValue(IReadOnlyList<PlayingCard> hand, Contract c, Rules r)
    {
        if (c.IsMisere)
        {
            double risk = hand.Sum(x => x.IsJoker ? 9 : Math.Max(0, x.Rank - 8) * .25);
            return -risk + Rules.Suits.Sum(s => Math.Max(0, 2 - hand.Count(x => x.Suit == s))) * .3;
        }
        double score = 0;
        foreach (var suit in Rules.Suits)
        {
            var cards = hand.Where(x => r.EffectiveSuit(x, c) == suit).OrderByDescending(x => r.Rank(x, c)).ToArray();
            bool trump = suit == c.Trump;
            if (trump)
            {
                score += cards.Sum(x => x.IsJoker ? 1.25 : r.Rank(x, c) >= 98 ? 1 : x.Rank == 14 ? .9 : x.Rank == 13 ? .7 : x.Rank == 12 ? .5 : .15);
                score += Math.Max(0, cards.Length - 4) * .5;
            }
            else
            {
                bool ace = cards.Any(x => x.Rank == 14), king = cards.Any(x => x.Rank == 13);
                score += ace ? .95 : 0;
                score += king ? (cards.Length >= 2 ? .55 : .15) : 0;
                score += cards.Any(x => x.Rank == 12) && cards.Length >= 3 ? .25 : 0;
                if (c.Trump == Suit.Unknown) score += Math.Max(0, cards.Length - 3) * (ace || king ? .25 : .05);
                else if (hand.Count(x => r.EffectiveSuit(x, c) == c.Trump) >= 3 && cards.Length <= 1) score += .35;
            }
        }
        if (c.Trump == Suit.Unknown && hand.Any(x => x.IsJoker)) score += 1.2;
        return score;
    }
    internal static double RetainedValue(IReadOnlyList<PlayingCard> hand, Contract c, Rules r)
    {
        if (c.IsMisere)
        {
            // High cards protected by several lower cards are safer than short high suits.
            return -hand.Sum(x => x.IsJoker ? 25 : Math.Max(0, x.Rank - 6) /
                (double)(1 + hand.Count(y => y.Suit == x.Suit && y.Rank < x.Rank)));
        }
        double value = HandValue(hand, c, r);
        if (c.Trump == Suit.Unknown)
            foreach (var suit in Rules.Suits)
            {
                var cards = hand.Where(x => x.Suit == suit).ToArray();
                if (cards.Any(x => x.Rank == 13) && cards.Length < 2) value -= .7;
                if (cards.Any(x => x.Rank == 12) && cards.Length < 3) value -= .35;
                if (cards.Length == 0 && !hand.Any(x => x.IsJoker)) value -= .35;
            }
        return value;
    }
    internal static IEnumerable<PlayingCard[]> DiscardSets(IReadOnlyList<PlayingCard> hand, int count)
    {
        if (count < 0 || count > hand.Count) throw new ArgumentOutOfRangeException(nameof(count));
        return Generate(0, []);
        IEnumerable<PlayingCard[]> Generate(int from, List<PlayingCard> chosen)
        {
            if (chosen.Count == count) { yield return chosen.ToArray(); yield break; }
            for (int i = from; i <= hand.Count - (count - chosen.Count); i++)
            {
                chosen.Add(hand[i]); foreach (var set in Generate(i + 1, chosen)) yield return set; chosen.RemoveAt(chosen.Count - 1);
            }
        }
    }
}
