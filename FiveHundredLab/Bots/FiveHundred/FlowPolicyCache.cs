using FiveHundredArena;
using Trickster.Bots;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>One root decision owns the cache. Rules are fixed by that lifetime;
/// trump and Joker lead suit complete the strategy key. No mutable cache is shared across bots.</summary>
internal sealed class FlowPolicyCache : IPlayer
{
    private readonly Observation root;
    private readonly Dictionary<PlayingCard, string> encodings;
    private readonly Dictionary<(Suit Trump, Suit Joker), MediumBot.DefaultStrategy> strategies = new();
    private readonly string[] unknownHands = Enumerable.Range(0, 14)
        .Select(n => string.Concat(Enumerable.Repeat("0U", n))).ToArray();
    private readonly List<Suit>[] prefixVoids;
    internal int EncodedCards => encodings.Count;
    internal int StrategyInstances => strategies.Count;
    internal long AdapterCalls { get; private set; }
    internal long DirectPlayCalls { get; private set; }
    public string Name => "flow-cached-medium";
    internal FlowPolicyCache(Observation root)
    {
        if (root.Rules is ConfiguredRules) throw new ArgumentException("Default Medium cache requires legacy rules");
        this.root = root;
        encodings = root.Rules.Deck().ToDictionary(c => c, c => c.ToBotCard().ToString());
        prefixVoids = SummarizeVoids(root.History, root.Contract);
    }
    private MediumBot.DefaultStrategy Strategy(Suit trump, IReadOnlyList<PublicPlay> trick)
    {
        var joker = trick.Count > 0 && trick[0].Action.Card.IsJoker ? trick[0].Action.JokerLeadSuit : Suit.Unknown;
        var key = (trump, joker);
        if (!strategies.TryGetValue(key, out var strategy))
            strategies[key] = strategy = new MediumBot.DefaultStrategy(CreateOptions(root.Rules, joker), trump);
        return strategy;
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

    private static int BidCode(Contract? contract) => contract == null ? BidBase.Pass : (int)BotBid(contract);
    private static FiveHundredBid BotBid(Contract c) => c.Kind switch
    {
        ContractKind.Misere => FiveHundredBid.Misere250Before8SBid,
        ContractKind.OpenMisere => FiveHundredBid.OpenMisereBidByPoints[FiveHundredOpenNulloPoints.FiveHundred],
        _ => new FiveHundredBid(c.Trump, c.Tricks)
    };
    private string Encode(PlayingCard card) => encodings.TryGetValue(card, out var text) ? text : card.ToBotCard().ToString();
    private string Cards(IEnumerable<PlayingCard> cards) => string.Concat(cards.Select(Encode));
    private string Unknown(int count) => count < unknownHands.Length ? unknownHands[count] : string.Concat(Enumerable.Repeat("0U", count));
    private static int Role(int seat, Contract contract, int declarer) => seat == declarer ? (int)BotBid(contract)
        : seat == (declarer + 2) % 4 ? contract.IsMisere ? BidBase.NotPlaying : (int)FiveHundredBid.ContractorPartnerBid
        : (int)FiveHundredBid.NotContractorBid;
    private List<Suit>[] SummarizeVoids(IReadOnlyList<PublicPlay> history, Contract? contract)
    {
        var result = Enumerable.Range(0, 4).Select(_ => new List<Suit>()).ToArray();
        if (contract == null) return result;
        int active = contract.IsMisere ? 3 : 4;
        var partial = new List<PublicPlay>(active);
        foreach (var play in history)
        {
            partial.Add(play);
            if (partial.Count > 1)
            {
                var led = root.Rules.LedSuit(partial, contract);
                if (root.Rules.EffectiveSuit(play.Action.Card, contract) != led && !result[play.Seat].Contains(led))
                    result[play.Seat].Add(led);
            }
            if (partial.Count == active) partial.Clear();
        }
        return result;
    }
    internal List<Suit>[] ForkVoids(Contract? contract)
    {
        if (contract != root.Contract) throw new ArgumentException("Root contract differs");
        return prefixVoids.Select(v => new List<Suit>(v)).ToArray();
    }
    private PlayerBase[] Players(Observation v, bool bidding)
    {
        var voids = v.Contract == root.Contract && ReferenceEquals(v.History, root.History)
            ? prefixVoids : SummarizeVoids(v.History, v.Contract);
        return v.Players.Select(p => new PlayerBase
        {
            Seat = p.Seat,
            Hand = p.Seat == v.Seat ? Cards(v.Hand) : p.ExposedHand != null ? Cards(p.ExposedHand) : Unknown(p.CardCount),
            Bid = bidding ? p.Bids.Count == 0 ? BidBase.NoBid : BidCode(p.Bids[^1]) : Role(p.Seat, v.Contract!, v.Declarer!.Value),
            BidHistory = p.Bids.Select(BidCode).ToList(), CardsTaken = Cards(p.Taken), HandScore = p.Tricks,
            GameScore = p.Score, VoidSuits = new List<Suit>(voids[p.Seat]), PlayedCards = [],
            Folded = !bidding && v.Contract!.IsMisere && p.Seat == (v.Declarer + 2) % 4
        }).ToArray();
    }
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        AdapterCalls++;
        var bot = Strategy(Suit.Unknown, view.Trick);
        var players = Players(view, true);
        int bid = bot.SuggestBid(new SuggestBidState<FiveHundredOptions>
        {
            dealerSeat = view.Dealer, hand = new Hand(Cards(view.Hand)),
            legalBids = legal.Select(c => new BidBase(BidCode(c))).ToList(),
            options = (FiveHundredOptions)bot.Options, player = players[view.Seat], players = players, trumpSuit = Suit.Unknown
        }).value;
        return legal.Single(c => BidCode(c) == bid);
    }
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        AdapterCalls++;
        var bot = Strategy(view.Contract!.Trump, view.Trick);
        var players = Players(view, false);
        return bot.SuggestDiscard(new SuggestDiscardState<FiveHundredOptions>
        {
            hand = new Hand(Cards(view.Hand)), player = players[view.Seat],
            options = (FiveHundredOptions)bot.Options, trumpSuit = view.Contract.Trump
        }).Select(PlayingCard.FromBotCard).ToArray();
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        AdapterCalls++;
        return Choose(view.Seat, view.Hand, Players(view, false), view.Contract!,
            view.Trick, view.History.Take(view.History.Count - view.Trick.Count).Select(p => p.Action.Card.ToBotCard()).ToArray(), legal);
    }
    // Auction is fixed once play starts. Encode it once per branch, not once per simulated card.
    internal static int[][] EncodeBids(IReadOnlyList<AuctionAction> auction) => Enumerable.Range(0, 4)
        .Select(s => auction.Where(a => a.Seat == s).Select(a => BidCode(a.Bid)).ToArray()).ToArray();
    internal PlayAction PlayDirect(int seat, List<PlayingCard>[] hands, List<PlayingCard>[] taken,
        int[][] bids, int[] tricks, Contract contract, int declarer, bool exposed,
        IReadOnlyList<PublicPlay> trick, IReadOnlyList<Card> completedCards, List<Suit>[] voids, IReadOnlyList<PlayAction> legal)
    {
        DirectPlayCalls++;
        var players = new PlayerBase[4];
        for (int s = 0; s < 4; s++)
            players[s] = new PlayerBase
            {
                Seat = s, Hand = s == seat || exposed && s == declarer ? Cards(hands[s]) : Unknown(hands[s].Count),
                Bid = Role(s, contract, declarer), BidHistory = new List<int>(bids[s]),
                CardsTaken = Cards(taken[s]), HandScore = tricks[s], GameScore = root.Scores[s % 2],
                VoidSuits = new List<Suit>(voids[s]), PlayedCards = [], Folded = contract.IsMisere && s == (declarer + 2) % 4
            };
        return Choose(seat, hands[seat], players, contract, trick, completedCards, legal);
    }
    private PlayAction Choose(int seat, IReadOnlyList<PlayingCard> hand, PlayerBase[] players, Contract contract,
        IReadOnlyList<PublicPlay> trick, IReadOnlyList<Card> completedCards, IReadOnlyList<PlayAction> legal)
    {
        var bot = Strategy(contract.Trump, trick);
        int? winner = trick.Count == 0 ? null : root.Rules.Winner(trick, contract);
        var card = bot.SuggestNextCard(new SuggestCardState<FiveHundredOptions>
        {
            options = (FiveHundredOptions)bot.Options, player = players[seat], players = players,
            trumpSuit = contract.Trump, trick = trick.Select(p => p.Action.Card.ToBotCard()).ToArray(),
            legalCards = legal.Select(p => p.Card).Distinct().Select(c => c.ToBotCard()).ToArray(),
            cardsPlayed = completedCards,
            // DefaultStrategy and its BaseBot play helpers consume completed cards and voids.
            // They do not read PlayedCards or cardsPlayedInOrder; omit those serialization-only models.
            cardsPlayedInOrder = "", cardTakingTrick = winner == null ? null : trick.Single(p => p.Seat == winner).Action.Card.ToBotCard(),
            trickTaker = winner == null ? null : players[winner.Value], isPartnerTakingTrick = winner == (seat + 2) % 4
        });
        var chosen = PlayingCard.FromBotCard(card);
        return legal.Where(p => p.Card == chosen).OrderByDescending(p => hand.Count(c => c.Suit == p.JokerLeadSuit)).First();
    }
}
