using System.Collections.Concurrent;
using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab;

public sealed class CardTableV1
{
    private static readonly ConcurrentDictionary<(Rules, Contract), CardTableV1> Cache = new();
    public readonly PlayingCard[] Deck;
    public readonly int[] Effective = new int[120];
    public readonly int[] Rank = new int[120];
    public readonly int[] Index = Enumerable.Repeat(-1, 120).ToArray();
    public readonly ulong DeckMask;
    private CardTableV1(Rules r, Contract c)
    {
        Deck = r.Deck().ToArray();
        for (int i = 0; i < Deck.Length; i++) { int code = Code(Deck[i]); Effective[code] = (int)r.EffectiveSuit(Deck[i], c); Rank[code] = r.Rank(Deck[i], c); Index[code] = i; DeckMask |= 1UL << i; }
    }
    public static CardTableV1 Get(Rules r, Contract c) => Cache.GetOrAdd((r, c), key => new(key.Item1, key.Item2));
    public static int Code(PlayingCard c) => (int)c.Suit * 20 + c.Rank;
    public ulong Bit(PlayingCard c) { int code = Code(c); if (code < 0 || code >= Index.Length || Index[code] < 0) throw new InvalidDataException("Unknown card in observation"); return 1UL << Index[code]; }
    public int Suit(PlayingCard c) => Effective[Code(c)];
    public int Strength(PlayingCard c) => Rank[Code(c)];
    public int Winner(IReadOnlyList<PublicPlay> trick, Contract c)
    {
        int lead = trick[0].Action.Card.IsJoker && c.Trump == Trickster.cloud.Suit.Unknown ? (int)trick[0].Action.JokerLeadSuit : Suit(trick[0].Action.Card);
        int best = -1, seat = -1;
        foreach (var p in trick)
        {
            int strength = p.Action.Card.IsJoker ? 1000 : (c.Trump != Trickster.cloud.Suit.Unknown && Suit(p.Action.Card) == (int)c.Trump ? 500 : Suit(p.Action.Card) == lead ? 100 : 0) + Strength(p.Action.Card);
            if (strength > best) { best = strength; seat = p.Seat; }
        }
        return seat;
    }
}
// Retains only public history. Prefix verification prevents reuse across divergent hypothetical paths.
public sealed class PublicTrackerV1
{
    private PublicPlay[] prefix = [];
    private Contract? contract;
    private int round = -1;
    private readonly int[] voids = new int[4];
    public IReadOnlyList<int> Update(Observation v)
    {
        bool extend = round == v.Round && contract == v.Contract && prefix.Length <= v.History.Count;
        if (extend) for (int i = 0; i < prefix.Length; i++) if (prefix[i] != v.History[i]) { extend = false; break; }
        int start = extend ? prefix.Length : 0;
        if (!extend) Array.Clear(voids);
        if (v.Contract != null)
        {
            var table = CardTableV1.Get(v.Rules, v.Contract); int active = v.Contract.IsMisere ? 3 : 4;
            for (int i = start; i < v.History.Count; i++)
            {
                int first = i / active * active;
                if (i == first) continue;
                var lead = v.History[first].Action;
                int suit = lead.Card.IsJoker && v.Contract.Trump == Suit.Unknown ? (int)lead.JokerLeadSuit : table.Suit(lead.Card);
                var p = v.History[i]; if (table.Suit(p.Action.Card) != suit) voids[p.Seat] |= 1 << suit;
            }
        }
        prefix = v.History.ToArray(); contract = v.Contract; round = v.Round; return voids.ToArray();
    }
}
public sealed class CardFrameV1
{
    public readonly Observation View;
    public readonly CardTableV1 Table;
    public readonly ulong Known;
    public readonly ulong HandMask;
    public readonly ulong Unseen;
    public readonly int[] Length = new int[6];
    public readonly IReadOnlyList<int> Voids;
    public CardFrameV1(Observation v, PublicTrackerV1? tracker = null)
    {
        View = v; Table = CardTableV1.Get(v.Rules, v.Contract ?? new(6, Suit.Unknown));
        foreach (var c in v.Hand) { HandMask |= Table.Bit(c); Length[Table.Suit(c)]++; }
        foreach (var p in v.History) Known |= Table.Bit(p.Action.Card);
        foreach (var c in v.OwnDiscards) Known |= Table.Bit(c);
        Unseen = Table.DeckMask & ~(Known | HandMask);
        Voids = (tracker ?? new PublicTrackerV1()).Update(v);
    }
    public bool IsVoid(int seat, int suit) => (Voids[seat] & (1 << suit)) != 0;
    public bool Boss(PlayingCard card)
    {
        if (card.IsJoker) return true;
        foreach (var c in Table.Deck) if ((Unseen & Table.Bit(c)) != 0 && Table.Suit(c) == Table.Suit(card) && Table.Strength(c) > Table.Strength(card)) return false;
        return true;
    }
    public IReadOnlyList<PlayAction> Representatives(IReadOnlyList<PlayAction> legal, PlayAction preferred)
    {
        if (View.Contract!.IsMisere) return legal;
        var groups = new List<List<PlayAction>>();
        foreach (var move in legal.OrderBy(p => Table.Suit(p.Card)).ThenBy(p => Table.Strength(p.Card)))
        {
            var last = groups.LastOrDefault(); bool merge = last != null && !move.Card.IsJoker && !last[^1].Card.IsJoker && Table.Suit(last[^1].Card) == Table.Suit(move.Card);
            if (merge)
            {
                int lo = Table.Strength(last![^1].Card), hi = Table.Strength(move.Card);
                merge = !Table.Deck.Any(c => (Unseen & Table.Bit(c)) != 0 && Table.Suit(c) == Table.Suit(move.Card) && Table.Strength(c) > lo && Table.Strength(c) < hi);
            }
            if (merge) last!.Add(move); else groups.Add([move]);
        }
        return groups.Select(g => g.Contains(preferred) ? preferred : g[0]).ToArray();
    }
}
