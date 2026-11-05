using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab;

public sealed class TacticianV1 : IPlayer
{
    private readonly CompactMediumV1 medium = new();
    private readonly PublicTrackerV1 tracker = new();
    public string Name => "tactician-v1";
    public Contract? Bid(Observation v, IReadOnlyList<Contract?> legal) => medium.Bid(v, legal);
    public IReadOnlyList<PlayingCard> Discard(Observation v, int count)
    {
        var baseline = medium.Discard(v, count);
        double Score(IEnumerable<PlayingCard> d) => Retained(v.Hand.Except(d).ToArray(), v.Contract!, v.Rules);
        var best = LegacyFeaturesV1.DiscardSets(v.Hand, count).MaxBy(Score)!;
        return Score(best) > Score(baseline) + .25 ? best : baseline;
    }
    public static double Retained(IReadOnlyList<PlayingCard> hand, Contract c, Rules r)
    {
        double score = LegacyFeaturesV1.RetainedValue(hand, c, r);
        if (c.IsMisere)
        {
            foreach (var suit in Rules.Suits)
            {
                var cards = hand.Where(x => x.Suit == suit).OrderBy(x => x.Rank).ToArray();
                if (cards.Length == 1) score -= Math.Max(0, cards[0].Rank - 7) * .6;
                if (cards.Length > 0 && cards[0].Rank <= 6) score += .3;
            }
            return score;
        }
        foreach (var suit in Rules.Suits)
        {
            var cards = hand.Where(x => r.EffectiveSuit(x, c) == suit).ToArray();
            if (suit == c.Trump) continue;
            if (c.Trump == Suit.Unknown)
            {
                if (cards.Any(x => x.Rank == 13) && !cards.Any(x => x.Rank == 14)) score += cards.Length >= 2 ? .45 : -.6;
                if (cards.Any(x => x.Rank == 12) && !cards.Any(x => x.Rank >= 13)) score += cards.Length >= 3 ? .25 : -.35;
                if (cards.Length >= 4 && cards.Any(x => x.Rank >= 13)) score += .15 * (cards.Length - 3);
            }
            else if (cards.Length == 0 && hand.Count(x => r.EffectiveSuit(x, c) == c.Trump) >= 3) score += .4;
        }
        return score;
    }
    public PlayAction Play(Observation v, IReadOnlyList<PlayAction> legal)
    {
        if (legal.Count == 1) return legal[0];
        var baseline = medium.Play(v, legal); var c = v.Contract!; var f = new CardFrameV1(v, tracker);
        if (c.IsMisere) return baseline; // The existing specialist remains the low-risk default.
        double Cost(PlayAction p)
        {
            var x = p.Card; int suit = f.Table.Suit(x);
            double cost = x.IsJoker ? 200 : suit == (int)c.Trump ? 40 + f.Table.Strength(x) : x.Rank;
            if (!x.IsJoker && suit != (int)c.Trump)
            {
                if (f.Boss(x)) cost += 25;
                if (x.Rank == 13 && f.Length[suit] == 2) cost += 12;
                if (x.Rank == 12 && f.Length[suit] == 3) cost += 6;
                if (f.Length[suit] == 1 && !f.Boss(x)) cost -= 5;
            }
            return cost;
        }
        if (v.Trick.Count == 3)
        {
            int current = f.Table.Winner(v.Trick, c);
            if (current % 2 == v.Seat % 2) return legal.OrderBy(Cost).First();
            var win = legal.Where(p => f.Table.Winner(v.Trick.Append(new(v.Seat, p)).ToArray(), c) == v.Seat).OrderBy(Cost).FirstOrDefault();
            return win ?? legal.OrderBy(Cost).First();
        }
        if (v.Trick.Count > 0 && f.Table.Winner(v.Trick, c) % 2 == v.Seat % 2)
        {
            var winner = v.Trick.Single(p => p.Seat == f.Table.Winner(v.Trick, c));
            if (f.Boss(winner.Action.Card)) return legal.OrderBy(Cost).First();
        }
        if (v.Trick.Count == 0 && v.Seat % 2 == v.Declarer % 2)
        {
            int won = v.Players.Where(p => p.Seat % 2 == v.Seat % 2).Sum(p => p.Tricks);
            int needed = Math.Max(0, c.Tricks - won);
            var safeWinners = legal.Where(p => f.Boss(p.Card) && (f.Table.Suit(p.Card) == (int)c.Trump || c.Trump == Suit.Unknown || Enumerable.Range(0, 4).Where(s => s % 2 != v.Seat % 2).All(s => !f.IsVoid(s, f.Table.Suit(p.Card)) || f.IsVoid(s, (int)c.Trump)))).ToArray();
            if (needed <= safeWinners.Select(p => p.Card).Distinct().Count() && safeWinners.Length > 0)
                return safeWinners.OrderBy(Cost).First();
        }
        return baseline;
    }
}
