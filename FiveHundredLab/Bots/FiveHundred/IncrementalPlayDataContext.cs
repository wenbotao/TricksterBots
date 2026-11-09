using FiveHundredArena;
using Trickster.cloud;
using Trickster.Bots;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Root-play preparation shared by hypothetical worlds. Mutable hands,
/// player lists, histories and strategy instances remain independent.</summary>
internal sealed class IncrementalPlayDataContext
{
    private readonly Dictionary<PlayingCard, string> encodings;
    private readonly IncrementalPlayHistory prefix;
    internal int SeedHistoryCount => prefix.Count;
    internal long HistoryForks { get; private set; }
    internal long HistoryAppends { get; private set; }
    internal IncrementalPlayHistory ForkHistory() { HistoryForks++;return prefix.Fork(); }
    internal void RecordHistoryAppend() => HistoryAppends++;
    private readonly int[][] bidHistory;
    private readonly int[] roles;
    private readonly string[] unknownHands;
    private readonly Dictionary<Suit, FiveHundredOptions> optionsByJoker = new();
    internal readonly IReadOnlyList<AuctionAction> Auction;
    internal readonly IReadOnlyList<Contract?>[] Bids;
    internal readonly IReadOnlyList<int> Scores;
    internal readonly Rules Rules;
    internal int EncodedCards => encodings.Count;
    internal int OptionsCreated => optionsByJoker.Count;
    internal long AdapterCalls { get; private set; }
    internal IncrementalPlayDataContext(Observation root)
    {
        // Only the default path uses this context. Configured continuations are unchanged.
        Rules = root.Rules with { };
        Auction = Array.AsReadOnly(root.Auction.ToArray());
        Scores = Array.AsReadOnly(root.Scores.ToArray());
        Bids = Enumerable.Range(0, 4).Select(seat => (IReadOnlyList<Contract?>)Array.AsReadOnly(root.Auction.Where(a => a.Seat == seat).Select(a => a.Bid).ToArray())).ToArray();
        bidHistory = Bids.Select(bids => bids.Select(Bid).ToArray()).ToArray();
        roles = root.Players.Select(p => p.Seat == root.Declarer ? (int)BotBid(root.Contract!)
            : p.Seat == (root.Declarer + 2) % 4 ? root.Contract!.IsMisere ? BidBase.NotPlaying : (int)FiveHundredBid.ContractorPartnerBid
            : (int)FiveHundredBid.NotContractorBid).ToArray();
        encodings = Rules.Deck().ToDictionary(c => c, c => c.ToBotCard().ToString());
        unknownHands = Enumerable.Range(0, 11).Select(n => string.Concat(Enumerable.Repeat("0U", n))).ToArray();
        prefix = new(Rules, root.Contract!, root.History, Encode);
    }
    private string UnknownHand(int count) => count < unknownHands.Length && count >= 0
        ? unknownHands[count] : string.Concat(Enumerable.Repeat("0U", count));
    private FiveHundredOptions Options(Observation view)
    {
        // Separate options for every Joker lead suit; never mutate a previously cached key.
        var joker = view.Trick.Count > 0 && view.Trick[0].Action.Card.IsJoker ? view.Trick[0].Action.JokerLeadSuit : Suit.Unknown;
        if (!optionsByJoker.TryGetValue(joker, out var options)) optionsByJoker[joker] = options = CreateOptions(Rules, joker);
        return options;
    }
        private static FiveHundredOptions CreateOptions(Rules rules, Suit jokerSuit) => new()
        {
            variation = FiveHundredVariation.Australian,
            players = 4,
            isPartnership = true,
            deckSize = 43,
            gameOverScore = rules.WinScore,
            mustBidToWin = rules.MustContractToWin,
            whenNullo = !rules.AllowMisere ? FiveHundredWhenNullo.Off : rules.MisereRequiresSeven ? FiveHundredWhenNullo.After7 : FiveHundredWhenNullo.Anytime,
            openNulloAfter8 = rules.OpenMisereRequiresEight,
            bidAfterPass = rules.PassIsPermanent ? BidAfterPass.Never : BidAfterPass.Always,
            nulloGetsKitty = NulloGetsKitty.Always,
            nulloPlaysPartner = false,
            S8Overbid250Nullo = true,
            jokerLowInNullo = false,
            nulloPoints = rules.MiserePoints switch
            {
                250 => FiveHundredNulloPoints.TwoHundredFifty,
                230 => FiveHundredNulloPoints.TwoHundredThirty,
                210 => FiveHundredNulloPoints.TwoHundredTen,
                _ => throw new NotSupportedException("Legacy bot supports Misere points 210/230/250 only")
            },
            openNulloPoints = rules.OpenMiserePoints switch
            {
                500 => FiveHundredOpenNulloPoints.FiveHundred,
                520 => FiveHundredOpenNulloPoints.FiveHundredTwenty,
                430 => FiveHundredOpenNulloPoints.FourHundredThirty,
                330 => FiveHundredOpenNulloPoints.ThreeHundredThirty,
                _ => throw new NotSupportedException("Legacy bot supports Open Misere points 330/430/500/520 only")
            },
            _jokerSuit = jokerSuit
        };
        private static FiveHundredBid BotBid(Contract c) => c.Kind switch
        {
            ContractKind.Misere => FiveHundredBid.Misere250Before8SBid,
            ContractKind.OpenMisere => FiveHundredBid.OpenMisereBidByPoints[FiveHundredOpenNulloPoints.FiveHundred],
            _ => new FiveHundredBid(c.Trump, c.Tricks)
        };
        private static int Bid(Contract? c) => c == null ? BidBase.Pass : (int)BotBid(c);
        private string Encode(PlayingCard card) => encodings.TryGetValue(card, out var text) ? text : card.ToBotCard().ToString();
        private string Cards(IEnumerable<PlayingCard> cards) => string.Concat(cards.Select(Encode));
        private PlayerBase[] Players(Observation v, IncrementalPlayHistory history)
        {
            var players = v.Players.Select(p => new PlayerBase
            {
                Seat = p.Seat,
                Hand = p.Seat == v.Seat ? Cards(v.Hand) : p.ExposedHand != null ? Cards(p.ExposedHand) : UnknownHand(p.CardCount),
                Bid = roles[p.Seat],
                // Fresh mutable lists preserve isolation from the cached integer arrays.
                BidHistory = new List<int>(bidHistory[p.Seat]),
                CardsTaken = Cards(p.Taken),
                HandScore = p.Tricks,
                GameScore = p.Score,
                VoidSuits = [],
                PlayedCards = [],
                Folded = v.Contract?.IsMisere == true && p.Seat == (v.Declarer + 2) % 4
            }).ToArray();
            history.Populate(players);
            return players;
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal, IncrementalPlayHistory history)
        {
            AdapterCalls++;
            var options = Options(view);
            var players = Players(view, history);
            var contract = view.Contract!;
            int? winner = view.Trick.Count == 0 ? null : view.Rules.Winner(view.Trick, contract);
            var card = new MediumBot.DefaultStrategy(options, contract.Trump).SuggestNextCard(new SuggestCardState<FiveHundredOptions>
            {
                options = options,
                player = players[view.Seat],
                players = players,
                trumpSuit = contract.Trump,
                trick = view.Trick.Select(p => p.Action.Card.ToBotCard()).ToArray(),
                legalCards = legal.Select(p => p.Card).Distinct().Select(c => c.ToBotCard()).ToArray(),
                cardsPlayed = history.CompletedCards(),
                cardsPlayedInOrder = history.OrderedCards,
                cardTakingTrick = winner == null ? null : view.Trick.Single(p => p.Seat == winner).Action.Card.ToBotCard(),
                trickTaker = winner == null ? null : players[winner.Value],
                isPartnerTakingTrick = winner == (view.Seat + 2) % 4
            });
            var chosen = PlayingCard.FromBotCard(card);
            // The legacy strategy returns only a card; choose its Joker lead suit from the longest held suit.
            return legal.Where(p => p.Card == chosen).OrderByDescending(p => view.Hand.Count(c => c.Suit == p.JokerLeadSuit)).First();
        }
}
