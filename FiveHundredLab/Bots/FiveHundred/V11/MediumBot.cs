using FiveHundredArena;
using Trickster.cloud;
using Trickster.Bots;
namespace FiveHundredLab.Bots.FiveHundred.V11;

/// <summary>Owns both the migrated original strategy and the configured deterministic policy.</summary>
public sealed class MediumBot : FiveHundredRuleBot
{
    private readonly DefaultAdapter defaultPolicy = new();
    public MediumBot(int seed = 1, Rules? rules = null) : base("medium-rules-v1", seed, rules) { }
    public static MediumBot FromConfiguration(string? path = null, int seed = 1) => new(seed, RuleConfiguration.Load(path));
    // Solo declaration is deliberately conservative; the partner will stop playing.
    public override bool DeclareGrandSlam(Observation view)
    {
        var rules=RequireMatchingRules(view);
        if(rules==null||view.Contract is not {Kind:ContractKind.Normal} contract)return false;
        bool controls=view.Hand.Any(c=>c.IsJoker) && (contract.Trump==Suit.Unknown ? view.Hand.Count(c=>c.Rank==14)>=3 : view.Hand.Count(c=>rules.EffectiveSuit(c,contract)==contract.Trump)>=7);
        return controls && RuleBotEvaluation.HandValue(view.Hand,contract,rules)>=10.2;
    }
    public override Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        return LegalBid(rules == null ? defaultPolicy.Bid(view, legal) : BidConfigured(view, rules, legal), legal);
    }
    public override IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return defaultPolicy.Discard(view, count);
        if (count == 0)
            return Array.Empty<PlayingCard>();
        return DiscardCandidates(view, rules, count, 1)[0];
    }
    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return defaultPolicy.Play(view, legal);
        return legal.Count == 1 ? legal[0] : PlayConfiguredTactics(view, rules, legal);
    }
    // Estimate contract returns under the active scoring rules; Inkle never becomes a contract.
    private Contract? BidConfigured(Observation view, ConfiguredRules rules, IReadOnlyList<Contract?> legal)
    {
        if (rules.PassIsPermanent && view.Declarer == (view.Seat + 2) % 4 && view.Contract is { } current && view.Scores[view.Seat % 2] + rules.Value(current) >= rules.WinScore && view.Players.Where(p => p.Seat % 2 != view.Seat % 2).All(p => view.Auction.Any(a => a.Seat == p.Seat && a.Bid == null)))
            return null;
        Contract? best = null;
        double bestUtility = 0;
        foreach (var bid in legal.Where(b => b != null).Select(b => b!))
        {
            if (bid.Kind == ContractKind.Inkle)
                continue;
            if (bid.IsMisere)
            {
                double risk = view.Hand.Sum(c => RuleBotEvaluation.Danger(view, c) / (1 + view.Hand.Count(x => x.Suit == c.Suit && RuleBotEvaluation.Danger(view, x) < RuleBotEvaluation.Danger(view, c))));
                if (risk > 27)
                    continue;
                double p = Math.Clamp(.85 - risk / 100, .05, .95);
                var made = new int[4];
                made[(view.Seat + 1) % 4] = 10;
                var failed = new int[4];
                failed[view.Seat] = 1;
                failed[(view.Seat + 1) % 4] = 9;
                double misereUtility = p * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, made) + (1 - p) * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, failed);
                if (misereUtility > bestUtility)
                {
                    bestUtility = misereUtility;
                    best = bid;
                }
                continue;
            }
            double estimate = RuleBotEvaluation.HandValue(view.Hand, bid, rules) + 2.1;
            var support = view.Auction.LastOrDefault(a => a.Seat == (view.Seat + 2) % 4 && a.Bid?.Kind == ContractKind.Normal)?.Bid;
            if (support?.Trump == bid.Trump)
                estimate += .8;
            if (estimate < bid.Tricks)
                continue;
            double probability = Math.Clamp(.65 + (estimate - bid.Tricks) * .12, .1, .97);
            var success = new int[4];
            success[view.Seat] = bid.Tricks;
            success[(view.Seat + 1) % 4] = 10 - bid.Tricks;
            var failure = new int[4];
            failure[view.Seat] = bid.Tricks - 1;
            failure[(view.Seat + 1) % 4] = 11 - bid.Tricks;
            double utility = probability * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, success) + (1 - probability) * RuleBotEvaluation.Utility(view, rules, bid, view.Seat, failure);
            if (utility > bestUtility)
            {
                bestUtility = utility;
                best = bid;
            }
        }
        if (best != null)
            return best;
        var inkle = legal.Where(c => c?.Kind == ContractKind.Inkle).Select(c => c!).OrderByDescending(c => view.Hand.Count(x => rules.EffectiveSuit(x, c) == c.Trump)).FirstOrDefault();
        return inkle != null && view.Hand.Count(c => rules.EffectiveSuit(c, inkle) == inkle.Trump) >= 4 ? inkle : null;
    }
    private static PlayingCard[][] DiscardCandidates(Observation view, ConfiguredRules rules, int count, int limit)
    {
        var danger = view.Hand.ToDictionary(card => card, card => RuleBotEvaluation.Danger(view, card));
        double Value(PlayingCard[] discard)
        {
            var kept = view.Hand.Except(discard).ToArray();
            return view.Contract!.IsMisere ? -kept.Sum(c => danger[c] / Math.Max(1, kept.Count(x => x.Suit == c.Suit && danger[x] <= danger[c]))) : RuleBotEvaluation.RetainedValue(kept, view.Contract, rules);
        }
        return HandEvaluator.DiscardSets(view.Hand, count).OrderByDescending(Value).Take(limit).ToArray();
    }

    internal static PlayAction PlayConfiguredTactics(Observation view, ConfiguredRules r, IReadOnlyList<PlayAction> legal)
    {
        var c = view.Contract!;
        var sorted = legal.OrderBy(p => RuleBotEvaluation.Danger(view, p.Card)).ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit).ToArray();
        if (view.Trick.Count == 0)
        {
            if (c.IsMisere)
                return c.Kind != ContractKind.GrandNullo && view.Declarer != view.Seat && view.Declarer % 2 == view.Seat % 2 ? sorted[^1] : sorted[0];
            return sorted.OrderByDescending(p => view.Hand.Count(x => r.EffectiveSuit(x, c) == r.EffectiveSuit(p.Card, c))).ThenByDescending(p => r.Rank(p.Card, c)).First();
        }
        int Winner(PlayAction p) => r.Winner(view.Trick.Append(new PublicPlay(view.Seat, p)).ToArray(), c);
        if (c.IsMisere)
        {
            if (view.Declarer == view.Seat || c.Kind == ContractKind.GrandNullo && view.Declarer % 2 == view.Seat % 2)
            {
                var duck = sorted.Where(p => c.Kind == ContractKind.GrandNullo ? Winner(p) % 2 != view.Seat % 2 : Winner(p) != view.Seat).ToArray();
                return duck.Length > 0 ? duck[^1] : sorted[0];
            }
            if (view.Declarer % 2 == view.Seat % 2)
            {
                var cover = sorted.Where(p => Winner(p) != view.Declarer).ToArray();
                return cover.Length > 0 ? cover[^1] : sorted[0];
            }
            var bust = sorted.Where(p => (c.Kind == ContractKind.GrandNullo ? Winner(p) % 2 == view.Declarer % 2 : Winner(p) == view.Declarer)).ToArray();
            if (bust.Length > 0)
                return bust[0];
            return sorted[^1];
        }
        if (r.Winner(view.Trick, c) % 2 == view.Seat % 2)
            return sorted.FirstOrDefault(p => Winner(p) % 2 == view.Seat % 2) ?? sorted[0];
        return sorted.FirstOrDefault(p => Winner(p) == view.Seat) ?? sorted[0];
    }

    // Default-rule adaptation and the complete original policy live here, not in FiveHundredBot.
    internal sealed class DefaultAdapter(string name = "medium-v1") : IPlayer
    {
        public string Name => name;
        private static FiveHundredOptions Options(Observation v) => new()
        {
            variation = FiveHundredVariation.Australian,
            players = 4,
            isPartnership = true,
            deckSize = 43,
            gameOverScore = v.Rules.WinScore,
            mustBidToWin = v.Rules.MustContractToWin,
            whenNullo = !v.Rules.AllowMisere ? FiveHundredWhenNullo.Off : v.Rules.MisereRequiresSeven ? FiveHundredWhenNullo.After7 : FiveHundredWhenNullo.Anytime,
            openNulloAfter8 = v.Rules.OpenMisereRequiresEight,
            bidAfterPass = v.Rules.PassIsPermanent ? BidAfterPass.Never : BidAfterPass.Always,
            nulloGetsKitty = NulloGetsKitty.Always,
            nulloPlaysPartner = false,
            S8Overbid250Nullo = true,
            jokerLowInNullo = false,
            nulloPoints = v.Rules.MiserePoints switch
            {
                250 => FiveHundredNulloPoints.TwoHundredFifty,
                230 => FiveHundredNulloPoints.TwoHundredThirty,
                210 => FiveHundredNulloPoints.TwoHundredTen,
                _ => throw new NotSupportedException("Legacy bot supports Misere points 210/230/250 only")
            },
            openNulloPoints = v.Rules.OpenMiserePoints switch
            {
                500 => FiveHundredOpenNulloPoints.FiveHundred,
                520 => FiveHundredOpenNulloPoints.FiveHundredTwenty,
                430 => FiveHundredOpenNulloPoints.FourHundredThirty,
                330 => FiveHundredOpenNulloPoints.ThreeHundredThirty,
                _ => throw new NotSupportedException("Legacy bot supports Open Misere points 330/430/500/520 only")
            },
            _jokerSuit = v.Trick.Count > 0 && v.Trick[0].Action.Card.IsJoker ? v.Trick[0].Action.JokerLeadSuit : Suit.Unknown
        };
        private static FiveHundredBid BotBid(Contract c) => c.Kind switch
        {
            ContractKind.Misere => FiveHundredBid.Misere250Before8SBid,
            ContractKind.OpenMisere => FiveHundredBid.OpenMisereBidByPoints[FiveHundredOpenNulloPoints.FiveHundred],
            _ => new FiveHundredBid(c.Trump, c.Tricks)
        };
        private static int Bid(Contract? c) => c == null ? BidBase.Pass : (int)BotBid(c);
        private static string Cards(IEnumerable<PlayingCard> cards) => string.Concat(cards.Select(c => c.ToBotCard().ToString()));
        private static PlayerBase[] Players(Observation v, bool bidding = false)
        {
            var players = v.Players.Select(p => new PlayerBase
            {
                Seat = p.Seat,
                Hand = p.Seat == v.Seat ? Cards(v.Hand) : p.ExposedHand != null ? Cards(p.ExposedHand) : string.Concat(Enumerable.Repeat("0U", p.CardCount)),
                Bid = bidding ? (p.Bids.Count > 0 ? Bid(p.Bids.Last()) : BidBase.NoBid)
                    : p.Seat == v.Declarer ? (int)BotBid(v.Contract!)
                    : p.Seat == (v.Declarer + 2) % 4 ? v.Contract!.IsMisere ? BidBase.NotPlaying : (int)FiveHundredBid.ContractorPartnerBid : (int)FiveHundredBid.NotContractorBid,
                BidHistory = p.Bids.Select(Bid).ToList(),
                CardsTaken = Cards(p.Taken),
                HandScore = p.Tricks,
                GameScore = p.Score,
                VoidSuits = [],
                PlayedCards = [],
                Folded = !bidding && v.Contract?.IsMisere == true && p.Seat == (v.Declarer + 2) % 4
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
        public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
        {
            var players = Players(view, true);
            var options = Options(view);
            var state = new SuggestBidState<FiveHundredOptions>
            {
                dealerSeat = view.Dealer,
                hand = new Hand(Cards(view.Hand)),
                legalBids = legal.Select(c => new BidBase(Bid(c))).ToList(),
                options = options,
                player = players[view.Seat],
                players = players,
                trumpSuit = Suit.Unknown
            };
            int bid = new DefaultStrategy(options, Suit.Unknown).SuggestBid(state).value;
            return legal.Single(c => Bid(c) == bid);
        }
        public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
        {
            var options = Options(view);
            var players = Players(view);
            var trump = view.Contract!.Trump;
            return new DefaultStrategy(options, trump).SuggestDiscard(new SuggestDiscardState<FiveHundredOptions>
            {
                hand = new Hand(Cards(view.Hand)),
                player = players[view.Seat],
                options = options,
                trumpSuit = trump
            }).Select(PlayingCard.FromBotCard).ToArray();
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        {
            var options = Options(view);
            var players = Players(view);
            var contract = view.Contract!;
            int? winner = view.Trick.Count == 0 ? null : view.Rules.Winner(view.Trick, contract);
            var card = new DefaultStrategy(options, contract.Trump).SuggestNextCard(new SuggestCardState<FiveHundredOptions>
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
    internal class DefaultStrategy : BaseBot<FiveHundredOptions>
    {
        public DefaultStrategy(FiveHundredOptions options, Suit trumpSuit) : base(options, trumpSuit)
        {
        }

        private int KittySize => options.deckSize - 40;

        public override BidBase SuggestBid(SuggestBidState<FiveHundredOptions> state)
        {
            var (players, hand, legalBids, player) = (new PlayersCollectionBase(this, state.players), state.hand, state.legalBids, state.player);
            var opponentsBids = players.Opponents(player).SelectMany(p => p.BidHistory.Select(b => new FiveHundredBid(b))).ToList();
            var partnersBids = players.PartnersOf(player).SelectMany(p => p.BidHistory.Select(b => new FiveHundredBid(b))).ToList();
            var playerLastBid = player.BidHistory.Any() ? new FiveHundredBid(player.BidHistory.Last()) : new FiveHundredBid(BidBase.NoBid);
            var partnerPassedFirst = partnersBids.Any() && partnersBids.FirstOrDefault() == BidBase.Pass;
            var defaultPartnerTricks = (partnerPassedFirst || players.Count == 3) ? 1 : 2;
            var estimatedKittyTricks = 1;
            var minimumToBid6NT = 6 - defaultPartnerTricks - estimatedKittyTricks;

            //  calculate the raw number of tricks we can take with a given trump suit
            var tricksBySuit = FiveHundredBid.suitRank.Keys.ToDictionary(s => s, s => CountTricks(hand, s));
            var hasJoker = hand.Any(c => c.suit == Suit.Joker);

            //  in Australian: if we haven't bid yet, partner hasn't bid yet, we have a Joker, and we can bid 6NT, do so (if our hand has at least some strength)
            if (options.variation == FiveHundredVariation.Australian
                && !player.BidHistory.Any()
                && !partnersBids.Any()
                && hasJoker
                && tricksBySuit[Suit.Unknown] >= minimumToBid6NT)
            {
                var sixNTBid = new FiveHundredBid(Suit.Unknown, 6);
                var sixNT = legalBids.FirstOrDefault(b => new FiveHundredBid(b.value) == sixNTBid);
                if (sixNT != null)
                    return sixNT;
            }

            //  then add adjustments based on hand shape, other players, and the kitty
            foreach (var suit in FiveHundredBid.suitRank.Keys)
            {
                var partnerBidSuit = partnersBids.Any(b => b.IsContractor && b.Suit == suit);

                //  deduct one for each number of cards we have fewer than five in trump (unless partner bid suit too)
                var count = hand.Count(c => EffectiveSuit(c, suit) == suit);
                if (suit != Suit.Unknown && count < 5 && !partnerBidSuit)
                    tricksBySuit[suit] -= 5 - count;

                //  deduct one if the highest card in trump is less than an Ace (unless partner bid suit too)
                var highestTrump = hand.Where(c => EffectiveSuit(c, suit) == suit).OrderByDescending(c => RankSort(c, suit)).FirstOrDefault();
                if (highestTrump != null && RankSort(highestTrump, suit) < RankSort(new Card(suit, Rank.Ace), suit) && !partnerBidSuit)
                    tricksBySuit[suit] -= 1;

                //  if opponents bid a suit, stay clear of it
                if (opponentsBids.Any(b => b.IsContractor && b.Suit == suit))
                    tricksBySuit[suit] = 0;

                //  avoid NT without the Joker (unless a partner has bid it)
                else if (suit == Suit.Unknown && !hasJoker && !partnerBidSuit)
                    tricksBySuit[suit] = 0;

                //  if any partner bid a suit we haven't bid yet, add our tricks to theirs (minus the two they expect from us), but don't go past 8 unless we have it by ourself
                else if (partnersBids.Any(b => b.IsContractor && b.Suit == suit) && !(playerLastBid.IsContractor && playerLastBid.Suit == suit))
                    tricksBySuit[suit] = Math.Max(tricksBySuit[suit], Math.Min(8, tricksBySuit[suit] + partnersBids.Last(b => b.IsContractor && b.Suit == suit).Tricks - defaultPartnerTricks));

                //  otherwise assume two tricks from partner plus one/two from the kitty (depending on size) unless already estimating 8+
                //  also progressively reduce how many additional tricks we'll assume as our own trick count increases
                else if (tricksBySuit[suit] < 8)
                    tricksBySuit[suit] += (int)Math.Floor((defaultPartnerTricks + estimatedKittyTricks) * Math.Min(4.0 / tricksBySuit[suit], 1.0));

                //  don't bid above 8 without the joker
                if (tricksBySuit[suit] >= 9 && !hasJoker)
                    tricksBySuit[suit] = 8;
            }

            var matches = legalBids.Where(b =>
            {
                var fhb = new FiveHundredBid(b.value);

                if (!fhb.IsContractor)
                    return false;

                if (fhb.IsLikeNullo)
                    return CanBidNullo(hand, fhb.IsOpen);

                return fhb.Tricks <= tricksBySuit[fhb.Suit];
            }).ToList();

            //  when playing Australian rules, signal with our best suit with our first bid if any partner hasn't passed
            var best = matches.LastOrDefault();
            var bestFHB = best != null ? new FiveHundredBid(best.value) : null;
            var shouldSignal = options.variation == FiveHundredVariation.Australian && players.PartnersOf(player).Any(p => p.Bid != BidBase.Pass) && player.BidHistory.Count == 0;
            var suggestion = shouldSignal && bestFHB != null ? matches.FirstOrDefault(b => new FiveHundredBid(b.value).Suit == bestFHB!.Suit) : best;

            //  only bid as high as necessary to win the game
            var suggestionIsPastGameOver = suggestion != null && BidValue(new FiveHundredBid(suggestion.value)) + player.GameScore >= options.gameOverScore;
            if (suggestion != null && suggestionIsPastGameOver)
            {
                var partners = players.PartnersOf(player);
                var highBid = players.Select(p => new FiveHundredBid(p.Bid)).OrderByDescending(b => b).First();
                var highBidIsPastGameOver = BidValue(highBid) + player.GameScore >= options.gameOverScore;
                var teamHasHighBid = players.Any(p => p.Bid == highBid && (p.Seat == player.Seat || partners.Any(partner => p.Seat == partner.Seat)));
                var canReenterBidding = options.bidAfterPass != BidAfterPass.Never;
                var opponentsHavePassed = players.Opponents(player).All(p => p.Bid == BidBase.Pass);

                //  pass if our team has the high bid, it's past the game over score, and opponents have all passed or we'll get the chance to bid again
                if (teamHasHighBid && highBidIsPastGameOver && (opponentsHavePassed || canReenterBidding))
                    return new BidBase(BidBase.Pass);

                //  otherwise bid, but only as high as needed to win the game
                suggestion = matches.FirstOrDefault(b =>
                {
                    var fhb = new FiveHundredBid(b.value);
                    if (fhb.Suit != bestFHB!.Suit)
                        return false;

                    return BidValue(fhb) + player.GameScore >= options.gameOverScore;
                });
            }

            return suggestion ?? new BidBase(BidBase.Pass);
        }

        public override List<Card> SuggestDiscard(SuggestDiscardState<FiveHundredOptions> state)
        {
            var (player, hand) = (state.player, state.hand);
            var count = KittySize;
            var theBid = new FiveHundredBid(player.Bid);
            List<Card> cards;

            if (theBid.IsLikeNullo)
            {
                //  throw the highest cards we have (trump in this case hits the jokers)
                cards = hand.Where(IsTrump).OrderBy(RankSort).Take(count).ToList();
                if (cards.Count < count)
                    cards.AddRange(hand.Where(c => !IsTrump(c)).OrderByDescending(RankSort).Take(count - cards.Count));
            }
            else if (theBid.Suit == Suit.Unknown)
            {
                //  in no-trump, throw the lowest cards we have
                //  TODO: try to balance this by keeping cards we need to stop a running suit
                cards = hand.OrderBy(RankSort).Take(count).ToList();
            }
            else
            {
                //  in trump, first group by suits to focus on creating void off-suits
                cards = hand.GroupBy(EffectiveSuit).ToDictionary(g => g.Key, g => g.OrderBy(RankSort).ToList())

                    //  save trump to discard last
                    .OrderBy(kvp => kvp.Key == trump)

                    //  try to get rid of off-suits with no cards we can make boss,
                    .ThenBy(kvp => 0 >= HighRankInSuit(kvp.Key) - RankSort(kvp.Value.Last()) - (kvp.Value.Count - 1))

                    //  followed by those that will take the longest to make a card boss
                    .ThenByDescending(kvp => HighRankInSuit(kvp.Key) - RankSort(kvp.Value.Last()))

                    //  then just get rid of the lowest cards in the shortest suits
                    .ThenBy(kvp => kvp.Value.Count)

                    //  now merge the suits into one flat list and take however many cards we need to discard
                    .SelectMany(kvp => kvp.Value).Take(count).ToList();
            }

            return cards;
        }

        public override Card SuggestNextCard(SuggestCardState<FiveHundredOptions> state)
        {
            var (players, trick, legalCards, cardsPlayed, player, isPartnerTakingTrick, cardTakingTrick, trickTaker) = (new PlayersCollectionBase(this, state.players), state.trick, state.legalCards, state.cardsPlayed,
                state.player, state.isPartnerTakingTrick, state.cardTakingTrick, state.trickTaker);

            //  if we're leading in a no-trump contract and we have a joker (but not all jokers), remove the joker(s) from our legal cards so we don't suggest a lead of joker unless we have to
            if (trump == Suit.Unknown && trick.Count == 0 && legalCards.Any(c => c.suit == Suit.Joker) && legalCards.Any(c => c.suit != Suit.Joker))
                legalCards = legalCards.Where(c => c.suit != Suit.Joker).ToList();

            if (IsNulloPlayer(player))
                return TryDumpEm(trick, legalCards, players.Count);

            if (players.Opponents(player).Any(p => new FiveHundredBid(p.Bid).IsLikeNullo))
                return TryBustNullo(player, trick, legalCards, cardsPlayed, players, trickTaker);

            // 3-player only: team up with the other opponent if declarer is in the lead (unless we're the declarer)
            var effectivePlayers = players;
            var isEffectivePartnerTakingTrick = isPartnerTakingTrick;
            if (players.Count == 3 && trickTaker != null)
            {
                var declarer = players.Single(p => p.Bid != FiveHundredBid.NotContractorBid);
                if (player.Seat != declarer.Seat && trickTaker.Seat != declarer.Seat && declarer.GameScore == players.Max(p => p.GameScore))
                {
                    var effectivePartners = players.Where(p => p.Seat != declarer.Seat && p.Seat != player.Seat);
                    effectivePlayers = new EffectivePartnerPlayersCollection(this, players, effectivePartners);
                    isEffectivePartnerTakingTrick = true;
                }
            }

            var bid = new FiveHundredBid(player.Bid);
            return TryTakeEm(player, trick, legalCards, cardsPlayed, effectivePlayers, isEffectivePartnerTakingTrick, cardTakingTrick, !bid.IsContractor && !bid.IsContractorPartner);
        }

        public override List<Card> SuggestPass(SuggestPassState<FiveHundredOptions> state)
        {
            throw new NotImplementedException();
        }

        public override int SuitOrder(Suit s)
        {
            return FiveHundredBid.suitOrder[s];
        }

        //  we're trying not to take the trick
        protected Card TryDumpEm(IReadOnlyList<Card> trick, IReadOnlyList<Card> legalCards, int nPlayers, bool takeWithHigh = false)
        {
            Card? suggestion;

            var firstCardInTrick = trick.FirstOrDefault(IsOfValue);

            if (firstCardInTrick == null)
            {
                //  lead the absolute lowest card we can
                suggestion = legalCards.OrderBy(RankSort).First();
            }
            else
            {
                var trickSuit = EffectiveSuit(firstCardInTrick);

                if (EffectiveSuit(legalCards[0]) == trickSuit)
                {
                    //  we can follow suit: dump the highest card that won't take the trick
                    if (trick.Any(IsTrump) && !IsTrump(firstCardInTrick))
                    {
                        //  if the lead suit was not trump and there is trump in the trick, just dump our highest card
                        suggestion = legalCards.OrderByDescending(RankSort).First();
                    }
                    else
                    {
                        //  otherwise dump the highest card below the card that is currently taking the trick
                        var trickTakerRank = trick.Where(c => EffectiveSuit(c) == trickSuit).Max(RankSort);
                        suggestion = legalCards.Where(c => RankSort(c) < trickTakerRank).OrderByDescending(RankSort).FirstOrDefault();

                        //  else if we can't get below the highest card and we're playing last, just play our highest card
                        if (suggestion == null && (takeWithHigh || trick.Count == nPlayers - 1))
                            suggestion = legalCards.OrderByDescending(RankSort).First();
                    }
                }
                else if (trick.Any(IsTrump))
                {
                    //  we can't follow suit but the trick contains trump: dump the highest trump that won't take the trick or the highest non-trump we have
                    var maxTrumpInTrick = trick.Where(IsTrump).Max(RankSort);
                    suggestion = legalCards.Where(c => IsTrump(c) && RankSort(c) < maxTrumpInTrick).OrderByDescending(RankSort).FirstOrDefault()
                                 ?? legalCards.Where(c => !IsTrump(c)).OrderByDescending(RankSort).FirstOrDefault();
                }
                else
                {
                    //  we can't follow suit and the trick does not contain trump: dump the highest non-trump we have or the highest trump if that's all we have left
                    suggestion = legalCards.Where(c => !IsTrump(c)).OrderByDescending(RankSort).FirstOrDefault() ?? legalCards.OrderByDescending(RankSort).FirstOrDefault();
                }
            }

            //  return either the suggestion, the lowest non-trump we can, or the lowest trump we can
            return suggestion ?? legalCards.Where(c => !IsTrump(c)).OrderBy(RankSort).FirstOrDefault() ?? legalCards.OrderBy(RankSort).First();
        }

        private bool CanBidNullo(IReadOnlyList<Card> hand, bool isOpen)
        {
            //  never bid nullo with the Joker
            if (hand.Any(c => c.suit == Suit.Joker))
                return false;

            var allowedGaps = isOpen ? 0 : 3;

            IReadOnlyList<Card> deck = DeckBuilder.BuildDeck(DeckType);
            var deckBySuit = SuitRank.stdSuits.ToDictionary(s => s, s => deck.Where(c => EffectiveSuit(c) == s).OrderBy(RankSort).ToList());
            var handBySuit = SuitRank.stdSuits.ToDictionary(s => s, s => hand.Where(c => EffectiveSuit(c) == s).OrderBy(RankSort).ToList());

            //  otherwise tolerate up to three gaps per suit below our cards
            foreach (var suit in SuitRank.stdSuits)
            {
                var cards = handBySuit[suit];
                var lowRank = RankSort(deckBySuit[suit].First());

                foreach (var rank in cards.Select(RankSort))
                {
                    var gaps = rank - lowRank - cards.Count(c => lowRank < RankSort(c) && RankSort(c) < rank);

                    if (gaps > allowedGaps)
                        return false;
                }
            }

            return true;
        }

        private int CountTricks(IEnumerable<Card> hand, Suit trumpSuit)
        {
            var deckBySuit = DeckBuilder.BuildDeck(DeckType).GroupBy(c => EffectiveSuit(c, trumpSuit)).ToDictionary(g => g.Key, g => g.OrderBy(c => RankSort(c, trumpSuit)).ToList());
            var handBySuit = SuitRank.stdSuits.ToDictionary(s => s, s => hand.Where(c => EffectiveSuit(c, trumpSuit) == s).OrderBy(c => RankSort(c, trumpSuit)).ToList());

            var tricks = 0;
            var nJokers = hand.Count(c => c.suit == Suit.Joker);

            if (trumpSuit == Suit.Unknown)
            {
                //  in no-trump, count 1 trick for each joker
                tricks += nJokers;
            }
            else
            {
                //  trump suits are only "good" if we have at least 4 trump
                var trumpCards = handBySuit[trumpSuit];
                if (trumpCards.Count < 4)
                    return 0;

                //  with trump, count trump we can use on suits with singletons or voids
                foreach (var suit in SuitRank.stdSuits.Where(s => s != trumpSuit).ToList())
                {
                    var countInSuit = handBySuit[suit].Count;
                    if (countInSuit < 2)
                    {
                        var trumpIn = Math.Min(2 - countInSuit, trumpCards.Count);
                        trumpCards.RemoveRange(0, trumpIn);
                        tricks += trumpIn;
                    }
                }
            }

            //  then calculate the winners for each suit, accounting for gaps
            foreach (var suit in SuitRank.stdSuits)
            {
                var deck = deckBySuit[suit];
                var cards = handBySuit[suit];

                var highRank = RankSort(deck.Last(), trumpSuit);
                var nextHighestRank = highRank;
                var hasStopper = nJokers > 0; //  if we have a Joker, we have a stopper for every suit

                while (cards.Any())
                {
                    //  don't give credit for off-suit cards more than one step below the highest rank in a trump contract
                    //  reasoning: too easy for other players to be void and trump in by that point
                    if (trumpSuit != Suit.Unknown && suit != trumpSuit && highRank - nextHighestRank > 1)
                        break;

                    var targetCard = cards.Last(); //  start with our next highest card
                    var targetRank = RankSort(targetCard, trumpSuit);
                    var gaps = deck.Count(c => targetRank < RankSort(c, trumpSuit) && RankSort(c, trumpSuit) <= nextHighestRank && !cards.Contains(c));
                    var below = cards.Count(c => RankSort(c, trumpSuit) < targetRank);

                    if (gaps > below)
                        break;

                    tricks++;
                    hasStopper = true;
                    nextHighestRank = targetRank - 1;
                    cards.Remove(targetCard);
                    cards.RemoveRange(0, gaps);
                }

                //  if we're looking at no-trump and we don't have a stopper in all suits, bail
                if (trumpSuit == Suit.Unknown && !hasStopper)
                    return 0;
            }

            return tricks;
        }

        private static bool IsNulloPlayer(PlayerBase player)
        {
            if (!player.IsActivelyPlaying)
                return false;

            //  active nullo players are those that either bid nullo (can be multiple if playing solo)
            //  OR partners of nullo bidders who are still in the game (will be face-up dummy hands played by the nullo bidder)
            return player.Bid == BidBase.Dummy || new FiveHundredBid(player.Bid).IsLikeNullo;
        }

        private int BidValue(FiveHundredBid theBid)
        {
            if (!theBid.IsContractor)
                return 0;

            if (theBid.IsLikeNullo)
                return theBid.IsOpen ? options.OpenNulloPoints : options.NulloPoints;

            //  Avondale scoring from https://en.wikipedia.org/wiki/500_(card_game)
            return 20 + FiveHundredBid.suitRank[theBid.Suit] * 20 + (theBid.Tricks - 6) * 100;
        }

        private Card TryBustNullo(PlayerBase player, IReadOnlyList<Card> trick, IReadOnlyList<Card> legalCards, IReadOnlyList<Card> cardsPlayed, PlayersCollectionBase players, PlayerBase trickTaker)
        {
            var nulloPlayers = players.Where(IsNulloPlayer).ToList();

            if (trick.Count == 0)
            {
                //  we're leading: try to pick something intelligent
                var avoidSuits = new List<Suit>();

                foreach (var suit in SuitRank.stdSuits)
                {
                    if (nulloPlayers.Any(p => players.TargetIsVoidInSuit(player, p, suit, cardsPlayed)))
                        avoidSuits.Add(suit);
                }

                var knownCards = cardsPlayed.Concat(legalCards).ToList();
                var preferredLegalCards = legalCards
                    .Where(c => !avoidSuits.Contains(EffectiveSuit(c))) //  avoid leading a suit any nullo player is void in
                    .Where(c => !IsCardHigh(c, knownCards)) //  also avoid leading a card that is known to be high
                    .ToList();

                //  fall back to avoiding cards known to be high, but allowing suits where nullo is void
                //  (gives partner a chance to take the lead)
                if (preferredLegalCards.Count == 0)
                    preferredLegalCards = legalCards.Where(c => !IsCardHigh(c, knownCards)).ToList();

                if (preferredLegalCards.Count > 0)
                    legalCards = preferredLegalCards;

                return TryDumpEm(trick, legalCards, players.Count);
            }

            //  if we're not leading, but a nullo player has yet to play and isn't void in the led suit, play low
            if (nulloPlayers.Any(p => p.Hand.Length == player.Hand.Length && !players.TargetIsVoidInSuit(player, p, trick[0], cardsPlayed)))
                return TryDumpEm(trick, legalCards, players.Count);

            //  if a nullo player is taking the trick, try to get under them (but go high if we can't)
            if (nulloPlayers.Any(p => p.Seat == trickTaker.Seat))
                return TryDumpEm(trick, legalCards, players.Count, takeWithHigh: true);

            //  play our highest card, preferring trump; this improves our ability to duck under nullo players later
            return legalCards.Where(IsTrump).OrderByDescending(RankSort).FirstOrDefault() ?? legalCards.OrderByDescending(RankSort).First();
        }

        private class EffectivePartnerPlayersCollection : PlayersCollectionBase
        {
            private readonly PlayerBase[] effectivePartners;

            public EffectivePartnerPlayersCollection(IBaseBot bot, IEnumerable<PlayerBase> players, IEnumerable<PlayerBase> effectivePartners) : base(bot, players)
            {
                this.effectivePartners = effectivePartners.ToArray();
            }

            protected override bool IsPartnership => true;

            public override PlayerBase[] PartnersOf(PlayerBase player)
            {
                return this.effectivePartners;
            }
        }
    }

    // Fast uses the historical visibility-safe variant of this same migrated policy.
    internal sealed class VisibilityAdapter(string name = "visibility-medium-v5") : IPlayer
    {
        private static readonly IReadOnlyDictionary<PlayingCard, string> Encodings = new Rules().Deck().ToDictionary(c => c, c => c.ToBotCard().ToString());
        private static readonly System.Threading.ThreadLocal<Dictionary<(Rules Rules, Suit Trump, Suit Joker), VisibilityStrategy>> Bots = new(() => new());
        private static VisibilityStrategy GetBot(Observation v, Suit trump)
        {
            var joker = v.Trick.Count > 0 && v.Trick[0].Action.Card.IsJoker ? v.Trick[0].Action.JokerLeadSuit : Suit.Unknown;
            var cache = Bots.Value!;
            var key = (v.Rules, trump, joker);
            if (!cache.TryGetValue(key, out var bot))
            {
                if (cache.Count >= 128)
                    cache.Clear();
                cache[key] = bot = new VisibilityStrategy(Options(v), trump);
            }
            return bot;
        }
        public string Name => name;
        private static FiveHundredOptions Options(Observation v) => new()
        {
            variation = FiveHundredVariation.Australian,
            players = 4,
            isPartnership = true,
            deckSize = 43,
            gameOverScore = v.Rules.WinScore,
            mustBidToWin = v.Rules.MustContractToWin,
            whenNullo = !v.Rules.AllowMisere ? FiveHundredWhenNullo.Off : v.Rules.MisereRequiresSeven ? FiveHundredWhenNullo.After7 : FiveHundredWhenNullo.Anytime,
            openNulloAfter8 = v.Rules.OpenMisereRequiresEight,
            bidAfterPass = v.Rules.PassIsPermanent ? BidAfterPass.Never : BidAfterPass.Always,
            nulloGetsKitty = NulloGetsKitty.Always,
            nulloPlaysPartner = false,
            S8Overbid250Nullo = true,
            jokerLowInNullo = false,
            nulloPoints = v.Rules.MiserePoints switch
            {
                250 => FiveHundredNulloPoints.TwoHundredFifty,
                230 => FiveHundredNulloPoints.TwoHundredThirty,
                210 => FiveHundredNulloPoints.TwoHundredTen,
                _ => throw new NotSupportedException("Legacy bot supports Misere points 210/230/250 only")
            },
            openNulloPoints = v.Rules.OpenMiserePoints switch
            {
                500 => FiveHundredOpenNulloPoints.FiveHundred,
                520 => FiveHundredOpenNulloPoints.FiveHundredTwenty,
                430 => FiveHundredOpenNulloPoints.FourHundredThirty,
                330 => FiveHundredOpenNulloPoints.ThreeHundredThirty,
                _ => throw new NotSupportedException("Legacy bot supports Open Misere points 330/430/500/520 only")
            },
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
                Seat = p.Seat,
                Hand = p.Seat == v.Seat ? Cards(v.Hand) : p.ExposedHand != null ? Cards(p.ExposedHand) : string.Concat(Enumerable.Repeat("0U", p.CardCount)),
                Bid = bidding ? (p.Bids.Count > 0 ? Bid(p.Bids.Last()) : BidBase.NoBid)
                    : p.Seat == v.Declarer ? (int)BotBid(v.Contract!)
                    : p.Seat == (v.Declarer + 2) % 4 ? v.Contract!.IsMisere ? BidBase.NotPlaying : (int)FiveHundredBid.ContractorPartnerBid : (int)FiveHundredBid.NotContractorBid,
                BidHistory = p.Bids.Select(Bid).ToList(),
                CardsTaken = Cards(p.Taken),
                HandScore = p.Tricks,
                GameScore = p.Score,
                VoidSuits = [],
                PlayedCards = [],
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
                        if (v.Rules.EffectiveSuit(play.Action.Card, v.Contract) != lead && !players[play.Seat].VoidSuits.Contains(lead))
                            players[play.Seat].VoidSuits.Add(lead);
                    }
                    if (partial.Count == active)
                        partial.Clear();
                }
            }
            return players;
        }
        public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
        {
            var players = Players(view, true);
            var bot = GetBot(view, Suit.Unknown);
            var options = (FiveHundredOptions)bot.Options;
            var state = new SuggestBidState<FiveHundredOptions>
            {
                dealerSeat = view.Dealer,
                hand = new Hand(Cards(view.Hand)),
                legalBids = legal.Select(c => new BidBase(Bid(c))).ToList(),
                options = options,
                player = players[view.Seat],
                players = players,
                trumpSuit = Suit.Unknown
            };
            int bid = bot.SuggestBid(state).value;
            return legal.Single(c => Bid(c) == bid);
        }
        public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
        {
            var trump = view.Contract!.Trump;
            var bot = GetBot(view, trump);
            var options = (FiveHundredOptions)bot.Options;
            var players = Players(view);
            return bot.SuggestDiscard(new SuggestDiscardState<FiveHundredOptions>
            {
                hand = new Hand(Cards(view.Hand)),
                player = players[view.Seat],
                options = options,
                trumpSuit = trump
            }).Select(PlayingCard.FromBotCard).ToArray();
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        {
            var contract = view.Contract!;
            var bot = GetBot(view, contract.Trump);
            var options = (FiveHundredOptions)bot.Options;
            var players = Players(view);
            int? winner = view.Trick.Count == 0 ? null : view.Rules.Winner(view.Trick, contract);
            var card = bot.SuggestNextCard(new SuggestCardState<FiveHundredOptions>
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
    internal sealed class VisibilityStrategy : DefaultStrategy
    {
        internal VisibilityStrategy(FiveHundredOptions options, Suit trump) : base(options, trump) { }
        public override bool CanSeeHand(PlayersCollectionBase players, PlayerBase player, PlayerBase target)
        {
            if (player.Seat == target.Seat)
                return true;
            string hand = target.Hand;
            if (string.IsNullOrEmpty(hand))
                return false;
            for (int i = 0; i < hand.Length; i += 2)
                if (hand[i] == '0' || hand[i + 1] == 'U' || hand[i + 1] == '?')
                    return false;
            return true;
        }
    }
}
