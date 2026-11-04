using Trickster.Bots;
using Trickster.cloud;

using FiveHundredArena;

namespace FiveHundredLab;

/// <summary>Adapts the existing strategy to detached, information-limited library states.</summary>
public sealed class CompactMediumV1(string name = "compact-medium-v1") : IPlayer
{
    private static readonly IReadOnlyDictionary<PlayingCard, string> Encodings = new Rules().Deck().ToDictionary(c => c, c => c.ToBotCard().ToString());
    public string Name => name;
    private static FiveHundredOptions Options(Observation v) => new()
    {
        variation = FiveHundredVariation.Australian, players = 4, isPartnership = true, deckSize = 43,
        gameOverScore = v.Rules.WinScore, mustBidToWin = v.Rules.MustContractToWin,
        whenNullo = !v.Rules.AllowMisere ? FiveHundredWhenNullo.Off : v.Rules.MisereRequiresSeven ? FiveHundredWhenNullo.After7 : FiveHundredWhenNullo.Anytime,
        openNulloAfter8 = v.Rules.OpenMisereRequiresEight,
        bidAfterPass = v.Rules.PassIsPermanent ? BidAfterPass.Never : BidAfterPass.Always,
        nulloGetsKitty = NulloGetsKitty.Always, nulloPlaysPartner = false,
        S8Overbid250Nullo = true, jokerLowInNullo = false,
        nulloPoints = v.Rules.MiserePoints switch { 250 => FiveHundredNulloPoints.TwoHundredFifty, 230 => FiveHundredNulloPoints.TwoHundredThirty, 210 => FiveHundredNulloPoints.TwoHundredTen, _ => throw new NotSupportedException("Legacy bot supports Misere points 210/230/250 only") },
        openNulloPoints = v.Rules.OpenMiserePoints switch { 500 => FiveHundredOpenNulloPoints.FiveHundred, 520 => FiveHundredOpenNulloPoints.FiveHundredTwenty, 430 => FiveHundredOpenNulloPoints.FourHundredThirty, 330 => FiveHundredOpenNulloPoints.ThreeHundredThirty, _ => throw new NotSupportedException("Legacy bot supports Open Misere points 330/430/500/520 only") },
        _jokerSuit = v.Trick.Count > 0 && v.Trick[0].Action.Card.IsJoker ? v.Trick[0].Action.JokerLeadSuit : Suit.Unknown
    };
    private static FiveHundredBid BotBid(Contract c) => c.Kind switch
    {
        ContractKind.Misere => FiveHundredBid.Misere250Before8SBid,
        ContractKind.OpenMisere => FiveHundredBid.OpenMisereBidByPoints[FiveHundredOpenNulloPoints.FiveHundred],
        _ => new FiveHundredBid(c.Trump, c.Tricks)
    };
    private static int Bid(Contract? c) => c == null ? BidBase.Pass : (int)BotBid(c);
    private static string Cards(IEnumerable<PlayingCard> cards) => string.Concat(cards.Select(c => Encodings.TryGetValue(c, out var text) ? text : c.ToBotCard().ToString()));
    private static PlayerBase[] Players(Observation v, bool bidding = false)
    {
        var players = v.Players.Select(p => new PlayerBase
        {
            Seat = p.Seat, Hand = p.Seat == v.Seat ? Cards(v.Hand) : p.ExposedHand != null ? Cards(p.ExposedHand) : string.Concat(Enumerable.Repeat("0U", p.CardCount)),
            Bid = bidding ? (p.Bids.Count > 0 ? Bid(p.Bids.Last()) : BidBase.NoBid)
                : p.Seat == v.Declarer ? (int)BotBid(v.Contract!)
                : p.Seat == (v.Declarer + 2) % 4 ? v.Contract!.IsMisere ? BidBase.NotPlaying : (int)FiveHundredBid.ContractorPartnerBid : (int)FiveHundredBid.NotContractorBid,
            BidHistory = p.Bids.Select(Bid).ToList(), CardsTaken = Cards(p.Taken), HandScore = p.Tricks,
            GameScore = p.Score, VoidSuits = [], PlayedCards = [],
            Folded = !bidding && v.Contract?.IsMisere == true && p.Seat == (v.Declarer + 2) % 4
        }).ToArray();
        if (v.Contract != null)
        {
            int active = v.Contract!.IsMisere ? 3 : 4;
            var partial = new List<PublicPlay>();
            foreach (var play in v.History)
            {
                partial.Add(play);
                if (partial.Count > 1)
                {
                    var lead = v.Rules.LedSuit(partial, v.Contract);
                    if (v.Rules.EffectiveSuit(play.Action.Card, v.Contract) != lead && !players[play.Seat].VoidSuits.Contains(lead)) players[play.Seat].VoidSuits.Add(lead);
                }
                if (partial.Count == active) partial.Clear();
            }
        }
        return players;
    }
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var players = Players(view, true); var options = Options(view);
        var state = new SuggestBidState<FiveHundredOptions>
        {
            dealerSeat = view.Dealer, hand = new Hand(Cards(view.Hand)), legalBids = legal.Select(c => new BidBase(Bid(c))).ToList(),
            options = options, player = players[view.Seat], players = players, trumpSuit = Suit.Unknown
        };
        int bid = new MediumBot(options, Suit.Unknown).SuggestBid(state).value;
        return legal.Single(c => Bid(c) == bid);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var options = Options(view); var players = Players(view); var trump = view.Contract!.Trump;
        return new MediumBot(options, trump).SuggestDiscard(new SuggestDiscardState<FiveHundredOptions>
        { hand = new Hand(Cards(view.Hand)), player = players[view.Seat], options = options, trumpSuit = trump }).Select(PlayingCard.FromBotCard).ToArray();
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var options = Options(view); var players = Players(view); var contract = view.Contract!;
        int? winner = view.Trick.Count == 0 ? null : view.Rules.Winner(view.Trick, contract);
        var card = new MediumBot(options, contract.Trump).SuggestNextCard(new SuggestCardState<FiveHundredOptions>
        {
            options = options, player = players[view.Seat], players = players, trumpSuit = contract.Trump,
            trick = view.Trick.Select(p => p.Action.Card.ToBotCard()).ToArray(),
            legalCards = legal.Select(p => p.Card).Distinct().Select(c => c.ToBotCard()).ToArray(),
            cardsPlayed = view.History.Take(view.History.Count - view.Trick.Count).Select(p => p.Action.Card.ToBotCard()).ToArray(),
            cardsPlayedInOrder = Cards(view.History.Select(p => p.Action.Card)),
            cardTakingTrick = winner == null ? null : view.Trick.Single(p => p.Seat == winner).Action.Card.ToBotCard(),
            trickTaker = winner == null ? null : players[winner.Value],
            isPartnerTakingTrick = winner == (view.Seat + 2) % 4
        });
        var chosen = PlayingCard.FromBotCard(card);
        // The legacy strategy returns only a card; choose its Joker lead suit from the longest held suit.
        return legal.Where(p => p.Card == chosen).OrderByDescending(p => view.Hand.Count(c => c.Suit == p.JokerLeadSuit)).First();
    }
}

