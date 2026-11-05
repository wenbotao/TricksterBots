using Trickster.cloud;

namespace FiveHundredArena;

// Override rule operations for variants; JSON settings cover common four-player house rules.
[System.Text.Json.Serialization.JsonPolymorphic]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ConfiguredRules), "resolved-v1")]
public record Rules
{
    public string Id { get; init; } = "australian-4-v1";
    public bool AllowMisere { get; init; } = true;
    public bool AllowOpenMisere { get; init; } = true;
    public bool MisereRequiresSeven { get; init; } = true;
    public bool OpenMisereRequiresEight { get; init; } = true;
    public int MiserePoints { get; init; } = 250;
    public int OpenMiserePoints { get; init; } = 500;
    public int WinScore { get; init; } = 500;
    public int LossScore { get; init; } = -500;
    public int DefenderTrickPoints { get; init; } = 10;
    public int SlamMinimum { get; init; } = 250;
    public bool MustContractToWin { get; init; } = true;
    public bool PassIsPermanent { get; init; } = true;
    public virtual void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || WinScore <= 0 || LossScore >= 0 || MiserePoints <= 0 || OpenMiserePoints <= 0 || DefenderTrickPoints < 0 || SlamMinimum < 0)
            throw new ArgumentException("Invalid rule profile");
    }
    public static IReadOnlyList<Suit> Suits { get; } = Array.AsReadOnly(new[] { Suit.Spades, Suit.Clubs, Suit.Diamonds, Suit.Hearts });
    public virtual List<PlayingCard> Deck() => Suits.SelectMany(s => Enumerable.Range(s is Suit.Diamonds or Suit.Hearts ? 4 : 5, s is Suit.Diamonds or Suit.Hearts ? 11 : 10).Select(r => new PlayingCard(s, r)))
        .Append(new PlayingCard(Suit.Joker, 16)).ToList();
    public virtual int Value(Contract c) => c.Kind switch
    {
        ContractKind.Misere => MiserePoints,
        ContractKind.OpenMisere => OpenMiserePoints,
        _ => (c.Tricks - 6) * 100 + 40 + Array.IndexOf(new[] { Suit.Spades, Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Unknown }, c.Trump) * 20
    };
    // Australian auction order is independent of points: Misere < 8S, Open Misere < 10NT.
    public virtual int Order(Contract c) => c.Kind switch
    {
        ContractKind.Misere => 19,
        ContractKind.OpenMisere => 47,
        _ => (c.Tricks - 6) * 10 + Array.IndexOf(new[] { Suit.Spades, Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Unknown }, c.Trump) * 2
    };
    public virtual List<Contract?> LegalBids(Contract? high, IReadOnlyList<AuctionAction> auction)
    {
        var bids = (from n in Enumerable.Range(6, 5) from s in Suits.Append(Suit.Unknown) select new Contract(n, s)).ToList();
        if (AllowMisere && (!MisereRequiresSeven || auction.Any(a => a.Bid is { IsMisere: false, Tricks: >= 7 }))) bids.Add(new(0, Suit.Unknown, ContractKind.Misere));
        if (AllowOpenMisere && (!OpenMisereRequiresEight || auction.Any(a => a.Bid is { IsMisere: false, Tricks: >= 8 }))) bids.Add(new(0, Suit.Unknown, ContractKind.OpenMisere));
        return new Contract?[] { null }.Concat(bids.Where(b => high == null || Order(b) > Order(high)).OrderBy(Order)).ToList();
    }
    public virtual Suit EffectiveSuit(PlayingCard c, Contract contract)
    {
        if (contract.Trump == Suit.Unknown || contract.IsMisere) return c.Suit;
        if (c.IsJoker || (c.Rank == 11 && SameColor(c.Suit, contract.Trump))) return contract.Trump;
        return c.Suit;
    }
    private static bool SameColor(Suit a, Suit b) => (a is Suit.Hearts or Suit.Diamonds) == (b is Suit.Hearts or Suit.Diamonds);
    public virtual int Rank(PlayingCard c, Contract contract)
    {
        if (c.IsJoker) return 100;
        if (contract.Trump != Suit.Unknown && !contract.IsMisere && c.Rank == 11)
        {
            if (c.Suit == contract.Trump) return 99;
            if (SameColor(c.Suit, contract.Trump)) return 98;
        }
        return c.Rank;
    }
    public virtual List<PlayAction> LegalPlays(IReadOnlyList<PlayingCard> hand, IReadOnlyList<PublicPlay> trick, Contract contract)
    {
        IEnumerable<PlayingCard> cards = hand;
        if (trick.Count > 0)
        {
            var lead = LedSuit(trick, contract);
            var follow = hand.Where(c => EffectiveSuit(c, contract) == lead).ToList();
            if (follow.Count > 0) cards = follow;
        }
        return cards.SelectMany(c => c.IsJoker && contract.Trump == Suit.Unknown && trick.Count == 0
            ? Suits.Select(s => new PlayAction(c, s)) : new[] { new PlayAction(c) }).ToList();
    }
    public virtual Suit LedSuit(IReadOnlyList<PublicPlay> trick, Contract c) => trick[0].Action.Card.IsJoker && c.Trump == Suit.Unknown
        ? trick[0].Action.JokerLeadSuit : EffectiveSuit(trick[0].Action.Card, c);
    public virtual int Winner(IReadOnlyList<PublicPlay> trick, Contract c)
    {
        Suit lead = LedSuit(trick, c);
        int Strength(PublicPlay p)
        {
            if (p.Action.Card.IsJoker) return 1000;
            Suit suit = EffectiveSuit(p.Action.Card, c);
            return (c.Trump != Suit.Unknown && suit == c.Trump ? 500 : suit == lead ? 100 : 0) + Rank(p.Action.Card, c);
        }
        return trick.MaxBy(Strength)!.Seat;
    }
    public virtual (bool Made, int[] Delta) Score(Contract c, int declarer, int[] tricks)
    {
        int team = declarer % 2, won = tricks[declarer] + (c.IsMisere ? 0 : tricks[(declarer + 2) % 4]);
        bool made = c.IsMisere ? won == 0 : won >= c.Tricks;
        int points = Value(c);
        var delta = new int[2];
        delta[team] = made ? (!c.IsMisere && won == 10 ? Math.Max(points, SlamMinimum) : points) : -points;
        if (!c.IsMisere) delta[1 - team] = DefenderTrickPoints * (10 - won);
        return (made, delta);
    }
    public virtual int? WinningTeam(int[] scores, int contractingTeam, bool made)
    {
        if (scores[contractingTeam] <= LossScore) return 1 - contractingTeam;
        if (scores[1 - contractingTeam] <= LossScore) return contractingTeam;
        if (made && scores[contractingTeam] >= WinScore) return contractingTeam;
        if (!MustContractToWin && scores[1 - contractingTeam] >= WinScore) return 1 - contractingTeam;
        return null;
    }
}
