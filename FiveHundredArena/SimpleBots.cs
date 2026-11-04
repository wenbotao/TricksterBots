using Trickster.cloud;

namespace FiveHundredArena;

/// <summary>Uniform bids (including Pass), uniform discard subsets, uniform cards then Joker suits.</summary>
public sealed class RandomLegalBot : IPlayer
{
    private readonly SeededRandom random;
    public RandomLegalBot() : this(1) { }
    public RandomLegalBot(int seed) => random = new(seed);
    public string Name => "random-legal-v1";
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => legal[random.Next(legal.Count)];
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        if (count < 0 || count > view.Hand.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var cards = view.Hand.ToArray(); random.Shuffle(cards); return cards.Take(count).ToArray();
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var cards = legal.Select(p => p.Card).Distinct().ToArray();
        var chosen = cards[random.Next(cards.Length)];
        var choices = legal.Where(p => p.Card == chosen).ToArray();
        return choices[random.Next(choices.Length)];
    }
}

/// <summary>Local beginner rules; no memory, void inference, bidding signals or search.</summary>
public sealed class WeakBot : IPlayer
{
    public string Name => "weak-v1";
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var best = legal.Where(c => c is { IsMisere: false, Tricks: <= 7 } && c.Trump != Suit.Unknown)
            .Where(c => view.Declarer != (view.Seat + 2) % 4 || view.Contract?.Trump != c!.Trump)
            .Select(c => (Contract: c!, Strength: Estimate(view.Hand, c!, view.Rules)))
            .Where(x => x.Strength >= x.Contract.Tricks).OrderByDescending(x => x.Strength)
            .ThenBy(x => view.Rules.Order(x.Contract)).FirstOrDefault();
        return best.Contract;
    }
    private static double Estimate(IReadOnlyList<PlayingCard> hand, Contract c, Rules r)
    {
        int trumps = hand.Count(x => r.EffectiveSuit(x, c) == c.Trump);
        return 2 + .55 * trumps + hand.Sum(x => x.IsJoker ? .8 : r.Rank(x, c) >= 98 ? .7
            : x.Rank == 14 && r.EffectiveSuit(x, c) != c.Trump ? .65 : x.Rank == 13 ? .15 : 0);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var c = view.Contract!; var r = view.Rules;
        return view.Hand.OrderBy(x => c.IsMisere ? -r.Rank(x, c)
            : x.IsJoker ? 1000 : c.Trump != Suit.Unknown && r.EffectiveSuit(x, c) == c.Trump ? 100 + r.Rank(x, c) : r.Rank(x, c)).Take(count).ToArray();
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var c = view.Contract!; var r = view.Rules;
        var ordered = legal.OrderBy(p => p.Card.IsJoker ? 1000 : c.Trump != Suit.Unknown && r.EffectiveSuit(p.Card, c) == c.Trump ? 100 + r.Rank(p.Card, c) : r.Rank(p.Card, c)).ToArray();
        if (view.Trick.Count == 0) return c.IsMisere ? ordered[0] : ordered[^1];
        int taking = r.Winner(view.Trick, c);
        if (c.IsMisere) return ordered[0];
        if (taking % 2 == view.Seat % 2) return ordered[0];
        return ordered.FirstOrDefault(p => r.Winner(view.Trick.Append(new PublicPlay(view.Seat, p)).ToArray(), c) == view.Seat) ?? ordered[0];
    }
}

