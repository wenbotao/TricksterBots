using Trickster.cloud;

namespace FiveHundredArena;

public readonly record struct PlayingCard(Suit Suit, int Rank)
{
    public bool IsJoker => Suit == Suit.Joker;
    public Card ToBotCard() => new(Suit, (Rank)Rank);
    public static PlayingCard FromBotCard(Card c) => new(c.suit, (int)c.rank);
    public override string ToString() => ToBotCard().StdNotation;
}

public enum ContractKind { Normal, Misere, OpenMisere, Inkle, MisereNoPickup, GrandNullo, GrandSlam, NoContractNT }
public sealed record Contract(int Tricks, Suit Trump, ContractKind Kind = ContractKind.Normal)
{
    public bool IsMisere => Kind is ContractKind.Misere or ContractKind.OpenMisere or ContractKind.MisereNoPickup or ContractKind.GrandNullo;
    public override string ToString() => IsMisere ? Kind.ToString() : $"{Tricks}{Trump}";
}
public sealed record AuctionAction(int Seat, Contract? Bid); // null = pass
public sealed record PlayAction(PlayingCard Card, Suit JokerLeadSuit = Suit.Unknown);
public sealed record PublicPlay(int Seat, PlayAction Action);
public sealed record PublicPlayer(int Seat, int CardCount, int Score, IReadOnlyList<Contract?> Bids,
    int Tricks, IReadOnlyList<PlayingCard> Taken, IReadOnlyList<PlayingCard>? ExposedHand);

// Fresh, detached observations: no engine state, deck, kitty or opponent hands are passed to a bot.
public sealed record Observation(int Seat, int Dealer, int Round, IReadOnlyList<PlayingCard> Hand,
    IReadOnlyList<PublicPlayer> Players, IReadOnlyList<AuctionAction> Auction,
    Contract? Contract, int? Declarer, IReadOnlyList<PublicPlay> Trick,
    IReadOnlyList<PublicPlay> History, IReadOnlyList<int> Scores, Rules Rules, IReadOnlyList<PlayingCard> OwnDiscards, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PrivateCardEvent>? PrivateEvents = null);

public interface IPlayer
{
    string Name { get; }
    Contract? Bid(Observation view, IReadOnlyList<Contract?> legal);
    IReadOnlyList<PlayingCard> Discard(Observation view, int count);
    PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal);
}
public sealed class IllegalActionException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record Decision(int Seat, string Phase, Contract? Bid = null,
    PlayingCard[]? Discards = null, PlayAction? Play = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? Accepted = null);
public sealed class RoundRecord
{
    public int Number { get; set; }
    public int Dealer { get; set; }
    public PlayingCard[][] Hands { get; set; } = [];
    public PlayingCard[] Kitty { get; set; } = [];
    public List<Decision> Decisions { get; set; } = [];
    public List<int> TrickWinners { get; set; } = [];
    public Contract? Contract { get; set; }
    public int? Declarer { get; set; }
    public bool Redeal { get; set; }
    public bool Made { get; set; }
    public int[] Tricks { get; set; } = [];
    public int[] ScoreDelta { get; set; } = [];
    public int[] Scores { get; set; } = [];
}
public sealed class GameRecord
{
    public int SchemaVersion { get; set; } = 1;
    public string EngineVersion { get; set; } = "1";
    public int Seed { get; set; }
    public string[] Players { get; set; } = [];
    public Rules Rules { get; set; } = new();
    public int MaxRounds { get; set; }
    public int InitialDealer { get; set; }
    public List<RoundRecord> Rounds { get; set; } = [];
    public int? Winner { get; set; } // team 0 = seats 0,2; team 1 = seats 1,3
    public bool Truncated { get; set; }
}

// Stable across .NET versions, unlike relying on System.Random's implementation.
public sealed class SeededRandom(int seed)
{
    private uint state = unchecked((uint)seed) ^ 0x9e3779b9U;
    public uint NextUInt() { state += 0x6d2b79f5U; var x = state; x = (x ^ (x >> 15)) * (x | 1); x ^= x + ((x ^ (x >> 7)) * (x | 61)); return x ^ (x >> 14); }
    public int Next(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        uint bound = (uint)count, limit = unchecked(0U - bound) % bound, x;
        do x = NextUInt(); while (x < limit);
        return (int)(x % bound);
    }
    public void Shuffle<T>(IList<T> items) { for (int i = items.Count - 1; i > 0; i--) { int j = Next(i + 1); (items[i], items[j]) = (items[j], items[i]); } }
}

public interface IJokerDeclarationPlayer
{
    IReadOnlyList<PlayAction> DeclareJokers(Observation view);
}

// These memories are visible only to the participant who observed the exchange.
// They are historical observations, not a claim about where those cards are now.
public sealed record PrivateCardEvent(string Phase, IReadOnlyList<PlayingCard> Cards);
public interface IRuleVariationPlayer
{
    bool RequestRedeal(Observation view);
    bool DeclareGrandSlam(Observation view);
    PlayingCard TransferCard(Observation view, IReadOnlyList<PlayingCard> legal);
}
