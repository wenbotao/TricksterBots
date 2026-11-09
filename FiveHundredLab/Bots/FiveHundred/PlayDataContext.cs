using FiveHundredArena;
using Trickster.cloud;
using Trickster.Bots;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Root-play preparation shared by hypothetical worlds. Mutable hands,
/// player lists, histories and strategy instances remain independent.</summary>
internal sealed class PlayDataContext
{
    private readonly Dictionary<PlayingCard, string> encodings;
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
    internal PlayDataContext(Observation root)
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
        private string Cards(IEnumerable<PlayingCard> cards) => string.Concat(cards.Select(c => encodings.TryGetValue(c, out var text) ? text : c.ToBotCard().ToString()));
        private PlayerBase[] Players(Observation v)
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
            if (v.Contract != null)
            {
                int active = v.Contract!.IsMisere ? 3 : 4;
                var partial = new List<PublicPlay>();
                foreach (var play in v.History)
                {
                    partial.Add(play);
                    int winner = v.Rules.Winner(partial, v.Contract);
                    players[play.Seat].PlayedCards.Add(new PlayedCard(play.Action.Card.ToBotCard(), partial.Single(p => p.Seat == winner).Action.Card.ToBotCard()));
                    if (partial.Count > 1)
                    {
                        var lead = v.Rules.LedSuit(partial, v.Contract);
                        if (v.Rules.EffectiveSuit(play.Action.Card, v.Contract) != lead && !players[play.Seat].VoidSuits.Contains(lead))
                            players[play.Seat].VoidSuits.Add(lead);
                    }
                    if (partial.Count == active)
                        partial.Clear();
                }
            }
            return players;
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        {
            AdapterCalls++;
            var options = Options(view);
            var players = Players(view);
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
