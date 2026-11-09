using System.Text;
using FiveHundredArena;
using Trickster.Bots;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Public-history value summary. A fork owns its lists and current trick;
/// legacy mutable models are freshly materialized for every policy call.</summary>
internal sealed class IncrementalPlayHistory
{
    internal readonly record struct Entry(PlayingCard Card, PlayingCard TakingCard);
    private readonly Rules rules;
    private readonly Contract contract;
    private readonly Func<PlayingCard, string> encode;
    private readonly int active;
    private readonly List<Entry>[] played;
    private readonly List<Suit>[] voids;
    private readonly List<PublicPlay> partial;
    private readonly List<PlayingCard> cards;
    private readonly StringBuilder ordered;
    private string? orderedText;
    internal int Count => cards.Count;
    internal IncrementalPlayHistory(Rules rules, Contract contract, IReadOnlyList<PublicPlay> prefix, Func<PlayingCard, string> encode)
    {
        this.rules = rules;this.contract = contract;this.encode = encode;
        active = contract.IsMisere ? 3 : 4;
        played = Enumerable.Range(0, 4).Select(_ => new List<Entry>()).ToArray();
        voids = Enumerable.Range(0, 4).Select(_ => new List<Suit>()).ToArray();
        partial = new(active);cards = new(40);ordered = new(80);
        foreach (var play in prefix) Append(play);
    }
    private IncrementalPlayHistory(IncrementalPlayHistory source)
    {
        rules = source.rules;contract = source.contract;encode = source.encode;active = source.active;
        played = source.played.Select(list => new List<Entry>(list)).ToArray();
        voids = source.voids.Select(list => new List<Suit>(list)).ToArray();
        partial = new(source.partial);cards = new(source.cards);
        orderedText = source.OrderedCards;ordered = new(orderedText, 80);
    }
    internal IncrementalPlayHistory Fork() => new(this);
    internal void Append(PublicPlay play)
    {
        partial.Add(play);
        int winner = rules.Winner(partial, contract);
        // Freeze this prefix's winner. Never replace earlier entries with the final trick winner.
        var taking = partial.Single(p => p.Seat == winner).Action.Card;
        played[play.Seat].Add(new(play.Action.Card, taking));
        if (partial.Count > 1)
        {
            var led = rules.LedSuit(partial, contract);
            if (rules.EffectiveSuit(play.Action.Card, contract) != led && !voids[play.Seat].Contains(led))
                voids[play.Seat].Add(led); // Keep first-observed order, as the original adapter does.
        }
        cards.Add(play.Action.Card);ordered.Append(encode(play.Action.Card));orderedText = null;
        if (partial.Count == active) partial.Clear();
    }
    internal void Populate(PlayerBase[] players)
    {
        foreach (var player in players)
        {
            player.VoidSuits = new List<Suit>(voids[player.Seat]);
            player.PlayedCards = played[player.Seat].Select(e => new PlayedCard(e.Card.ToBotCard(), e.TakingCard.ToBotCard())).ToList();
        }
    }
    internal Card[] CompletedCards() => cards.Take(cards.Count - partial.Count).Select(c => c.ToBotCard()).ToArray();
    internal string OrderedCards => orderedText ??= ordered.ToString();
}
