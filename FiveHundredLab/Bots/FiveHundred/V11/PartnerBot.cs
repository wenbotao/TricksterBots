using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred.V11;

/// <summary>Fast search with explicit partner safeguards and a declared low-discard request convention.</summary>
public sealed class PartnerBot : FiveHundredRuleBot
{
    private readonly FastBot fast;
    private readonly DefaultPolicy defaultPolicy;
    public PartnerBot(int seed = 1, Rules? rules = null) : base("partner-rules-v1", seed, rules)
    {
        fast = new(seed, rules);
        defaultPolicy = new(seed, rules);
    }
    public static PartnerBot FromConfiguration(string? path = null, int seed = 1) => new(seed, RuleConfiguration.Load(path));
    // Solo declaration is deliberately conservative; the partner will stop playing.
    public override bool DeclareGrandSlam(Observation view)
    {
        var rules=RequireMatchingRules(view);
        if(rules==null||view.Contract is not {Kind:ContractKind.Normal} contract)return false;
        bool controls=view.Hand.Any(c=>c.IsJoker) && (contract.Trump==Suit.Unknown ? view.Hand.Count(c=>c.Rank==14)>=3 : view.Hand.Count(c=>rules.EffectiveSuit(c,contract)==contract.Trump)>=7);
        return controls && RuleBotEvaluation.HandValue(view.Hand,contract,rules)>=10.5;
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
        var selected = fast.Discard(view, count).ToArray();
        LastRollouts = fast.LastRollouts;
        return ConfiguredPartnerPolicy.RepairDiscards(view, rules, selected);
    }
    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules == null)
            return defaultPolicy.Play(view, legal);
        if (legal.Count == 1)
            return legal[0];
        // Do not lead a provably winning card in our own Misere when an alternative exists.
        if (view.Contract!.IsMisere && view.Declarer == view.Seat && view.Trick.Count == 0)
        {
            var safe = legal.Where(p => !ConfiguredPartnerPolicy.GuaranteedLead(view, rules, p)).ToArray();
            if (safe.Length > 0)
                legal = safe;
        }
        if (ConfiguredPartnerPolicy.CertainAction(view, rules, legal) is { } certain)
            return certain;
        var selected = fast.Play(view, legal);
        LastRollouts = fast.LastRollouts;
        return selected;
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

    // Configured safeguards reason only from legal actions and public information.
    private static class ConfiguredPartnerPolicy
    {

        internal static PlayingCard[] RepairDiscards(Observation view, ConfiguredRules r, PlayingCard[] selected)
        {
            var c = view.Contract!;
            bool high = ConfiguredRules.Text(r.ContractOptions(c).GetProperty("joker"), "unassignedRank") == "highest";
            if (selected.Length == 0)
                return selected;
            foreach (var joker in view.Hand.Where(p => p.IsJoker && high))
            {
                if (c.IsMisere && !selected.Contains(joker))
                {
                    var replace = selected.Where(p => !p.IsJoker).OrderBy(p => RuleBotEvaluation.Danger(view, p)).FirstOrDefault();
                    if (selected.Contains(replace))
                        selected = selected.Select(p => p == replace ? joker : p).ToArray();
                }
                if (!c.IsMisere && selected.Contains(joker))
                {
                    var replace = view.Hand.Except(selected).Where(p => !p.IsJoker && p.Rank <= 10 && r.EffectiveSuit(p, c) != c.Trump).OrderBy(p => p.Rank).FirstOrDefault();
                    if (view.Hand.Contains(replace))
                        selected = selected.Select(p => p == joker ? replace : p).ToArray();
                }
            }
            if (c.IsMisere || c.Trump != Suit.Unknown)
                return selected;
            int Bare(PlayingCard[] d) => Rules.Suits.Count(s => { var kept = view.Hand.Except(d).Where(p => p.Suit == s).ToArray(); return view.Hand.Count(p => p.Suit == s) > 1 && kept.Length == 1 && kept[0].Rank == 13; });
            for (int pass = 0; pass < selected.Length && Bare(selected) > 0; pass++)
            {
                var before = selected;
                var repair = before.SelectMany((_, i) => view.Hand.Except(before).Where(p => !p.IsJoker && p.Rank <= 10).Select(card => before.Select((p, j) => j == i ? card : p).ToArray()))
                    .Where(d => Bare(d) < Bare(before) && !d.Any(p => p.IsJoker && high) && RuleBotEvaluation.RetainedValue(view.Hand.Except(d).ToArray(), c, r) >= RuleBotEvaluation.RetainedValue(view.Hand.Except(before).ToArray(), c, r) - .15)
                    .OrderByDescending(d => RuleBotEvaluation.RetainedValue(view.Hand.Except(d).ToArray(), c, r)).FirstOrDefault();
                if (repair == null)
                    break;
                selected = repair;
            }
            return selected;
        }
        internal static bool GuaranteedLead(Observation view, ConfiguredRules r, PlayAction lead)
        {
            var unknown = r.Deck().Except(view.Hand).Except(view.History.Select(p => p.Action.Card)).Except(view.OwnDiscards).Except(view.Players.SelectMany(p => p.ExposedHand ?? [])).ToArray();
            int inactive = ConfiguredRules.Text(r.ContractOptions(view.Contract!), "activeSeats") == "all_four" ? -1 : (view.Declarer!.Value + 2) % 4;
            return view.Players.Where(p => p.Seat != view.Seat && p.Seat != inactive).All(p => (p.ExposedHand ?? unknown).All(card => r.Winner([new(view.Seat, lead), new(p.Seat, new(card))], view.Contract!) == view.Seat));
        }
        private static PlayAction? PartnerLead(Observation view, ConfiguredRules r, IReadOnlyList<PlayAction> legal)
        {
            var c = view.Contract!;
            if (c.IsMisere || view.Trick.Count != 0 || !ConfiguredRules.Flag(r.Section("play"), "mustFollowEffectiveSuit"))
                return null;
            if (legal.Any(p => !p.Card.IsJoker && GuaranteedLead(view, r, p)))
                return null;
            int partner = (view.Seat + 2) % 4;
            var voids = Enumerable.Range(0, 4).Select(_ => new HashSet<Suit>()).ToArray();
            Suit request = Suit.Unknown;
            for (int i = 0; i < view.History.Count; i += 4)
            {
                var trick = view.History.Skip(i).Take(4).ToArray();
                if (trick.Length != 4)
                    break;
                var led = r.LedSuit(trick, c);
                foreach (var p in trick.Skip(1))
                {
                    var suit = r.EffectiveSuit(p.Action.Card, c);
                    if (p.Action.Card.IsJoker || suit == led)
                        continue;
                    voids[p.Seat].Add(led);
                    if (p.Seat == partner && p.Action.Card.Rank <= 7 && suit != c.Trump)
                        request = suit;
                }
            }
            bool Risk(Suit suit) => c.Trump != Suit.Unknown && view.Players.Where(p => p.Seat % 2 != view.Seat % 2).Any(p => voids[p.Seat].Contains(suit) && !voids[p.Seat].Contains(c.Trump));
            if (c.Trump != Suit.Unknown && !voids[partner].Contains(c.Trump) && view.Hand.Count(x => r.EffectiveSuit(x, c) == c.Trump) < 3 && view.Auction.Any(a => a.Seat == partner && a.Bid?.Kind == ContractKind.Normal && a.Bid.Trump == c.Trump))
            {
                var offer = legal.Where(p => !p.Card.IsJoker && r.EffectiveSuit(p.Card, c) != c.Trump && voids[partner].Contains(r.EffectiveSuit(p.Card, c)) && !Risk(r.EffectiveSuit(p.Card, c))).OrderBy(p => p.Card.Rank).FirstOrDefault();
                if (offer != null)
                    return offer;
            }
            var held = legal.Where(p => !p.Card.IsJoker && r.EffectiveSuit(p.Card, c) == request).ToArray();
            return request != Suit.Unknown && !voids[partner].Contains(request) && !Risk(request) && held.Length >= 3 && held.Any(p => p.Card.Rank >= 12) ? held.OrderBy(p => p.Card.Rank).First() : null;
        }
        internal static PlayAction? CertainAction(Observation view, ConfiguredRules r, IReadOnlyList<PlayAction> legal)
        {
            var c = view.Contract!;
            int active = ConfiguredRules.Text(r.ContractOptions(c), "activeSeats") == "all_four" ? 4 : 3;
            var inactive = active == 3 ? (view.Declarer!.Value + 2) % 4 : -1;
            var unknown = r.Deck().Except(view.Hand).Except(view.History.Select(p => p.Action.Card)).Except(view.OwnDiscards).Except(view.Players.SelectMany(p => p.ExposedHand ?? [])).ToArray();
            int Next(int s)
            {
                do
                {
                    s = (s + 1) % 4;
                } while (s == inactive);
                return s;
            }
            int proofBudget=8192;
            bool Prove(List<PublicPlay> prefix, int target)
            {
                if(--proofBudget<0)return false;
                if (prefix.Count == active)
                    return r.Winner(prefix, c) == target;
                int seat = Next(prefix[^1].Seat);
                var hand = view.Players[seat].ExposedHand;
                var proof = r with
                {
                    History = view.History.Take(view.History.Count - view.Trick.Count).Concat(prefix).ToArray(),
                    CurrentSeat = seat,
                    Declarations = new(r.Declarations)
                };
                var replies = hand != null ? proof.LegalPlays(hand, prefix, c) : unknown.Where(x => !prefix.Any(p => p.Action.Card == x)).Select(x => new PlayAction(x)).ToList();
                if (replies.Count == 0)
                    return false;
                foreach (var move in replies)
                {
                    prefix.Add(new(seat, move));
                    bool won = Prove(prefix, target);
                    prefix.RemoveAt(prefix.Count - 1);
                    if (!won)
                        return false;
                }
                return true;
            }
            if (c.IsMisere && view.Declarer % 2 != view.Seat % 2 && view.Players[view.Declarer!.Value].Tricks == 0 && view.Players[view.Declarer.Value].ExposedHand != null)
                foreach (var move in legal)
                    if (Prove(view.Trick.Append(new PublicPlay(view.Seat, move)).ToList(), view.Declarer.Value))
                        return move;
            if (!c.IsMisere && view.Trick.Count > 0 && r.Winner(view.Trick, c) == (view.Seat + 2) % 4)
                foreach (var move in legal.OrderBy(p => RuleBotEvaluation.Danger(view, p.Card)))
                    if (Prove(view.Trick.Append(new PublicPlay(view.Seat, move)).ToList(), (view.Seat + 2) % 4))
                        return move;
            return PartnerLead(view, r, legal);
        }
    }

    // Historical default path keeps canonical tie order and the original partner convention.
    private sealed class DefaultPolicy(int seed, Rules? rules) : IPlayer
    {
        private readonly FastBot fast = new(seed, rules);
        public string Name => "partner-fast-v9";
        public string LastReason { get; private set; } = "none";
        public FastBot.DefaultSearchReport? LastSearch
        {
            get; private set;
        }

        // Canonical inputs remove accidental tie-breaking by the caller's hand layout.
        private static Observation Canonical(Observation v) => v with { Hand = v.Hand.OrderBy(c => c.Suit).ThenBy(c => c.Rank).ToArray() };
        public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
        {
            var result = fast.Bid(Canonical(view), legal);
            LastReason = "fast-bid";
            LastSearch = fast.DefaultSearch;
            return result;
        }
        public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
        {
            var v = Canonical(view);
            var selected = fast.Discard(v, count).ToArray();
            LastSearch = fast.DefaultSearch;
            LastReason = "fast-discard";
            var repaired = DefaultPartnerPolicy.RepairDiscards(v, selected);
            if (!selected.ToHashSet().SetEquals(repaired))
                LastReason = "preserve-discard-structure";
            return repaired;
        }
        public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        {
            var v = Canonical(view);
            var choices = legal.OrderBy(p => p.Card.Suit).ThenBy(p => p.Card.Rank).ThenBy(p => p.JokerLeadSuit).ToArray();
            if (choices.Length == 0)
                throw new ArgumentException("No legal play");
            if (choices.Length == 1)
                return Direct(choices[0], "forced-legal");
            var immediate = DefaultPartnerPolicy.ImmediateMiserePlay(v, choices);
            if (immediate != null)
                return Direct(immediate, "public-misere-certainty");
            if (v.Contract!.IsMisere && v.Declarer == v.Seat && v.Trick.Count == 0)
            {
                var safe = choices.Where(p => !DefaultPartnerPolicy.CertainLeadWin(v, p)).ToArray();
                if (safe.Length > 0 && safe.Length < choices.Length)
                    choices = safe;
            }
            if (!v.Contract.IsMisere)
            {
                var protect = DefaultPartnerPolicy.ProtectPartnerWinner(v, choices);
                if (protect != null)
                    return Direct(protect, "protect-partner-winner");
                var lead = DefaultPartnerPolicy.PartnerLead(v, choices);
                if (lead != null)
                    return Direct(lead.Value.Play, lead.Value.Reason);
            }
            var result = fast.Play(v, choices);
            LastSearch = fast.DefaultSearch;
            LastReason = "fast-play";
            return result;
        }
        private PlayAction Direct(PlayAction play, string reason)
        {
            LastReason = reason;
            LastSearch = new(1, 0, 0, true, "play");
            return play;
        }
    }
    private static class DefaultPartnerPolicy
    {
        private static int Partner(Observation v) => (v.Seat + 2) % 4;
        private static PlayingCard[] Unknown(Observation v) => v.Rules.Deck()
            .Except(v.Hand).Except(v.OwnDiscards).Except(v.History.Select(p => p.Action.Card))
            .Except(v.Trick.Select(p => p.Action.Card)).Except(v.Players.SelectMany(p => p.ExposedHand ?? [])).ToArray();
        private static IEnumerable<PlayingCard> Possible(Observation v, int seat, PlayingCard[] unknown) => v.Players[seat].ExposedHand ?? unknown;
        private static PlayAction Cheapest(Observation v, IEnumerable<PlayAction> actions) => actions
            .OrderBy(p => p.Card.IsJoker ? 1000 : v.Rules.EffectiveSuit(p.Card, v.Contract!) == v.Contract!.Trump ? 100 + v.Rules.Rank(p.Card, v.Contract) : v.Rules.Rank(p.Card, v.Contract!))
            .ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit).First();

        internal static bool CertainLeadWin(Observation v, PlayAction lead)
        {
            var prefix = new[] { new PublicPlay(v.Seat, lead) };
            var unknown = Unknown(v);
            return v.Players.Where(p => p.Seat != v.Seat && (!v.Contract!.IsMisere || p.Seat != (v.Declarer + 2) % 4))
                .All(p => Possible(v, p.Seat, unknown).All(c => v.Rules.Winner([prefix[0], new(p.Seat, new(c))], v.Contract!) == v.Seat));
        }

        internal static PlayAction? ImmediateMiserePlay(Observation v, IReadOnlyList<PlayAction> legal)
        {
            if (v.Contract?.IsMisere != true || v.Declarer == null || v.Players[v.Declarer.Value].Tricks > 0)
                return null;
            int declarer = v.Declarer.Value;
            if (v.Trick.Count == 2)
            {
                var relevant = legal.Where(p =>
                {
                    int winner = v.Rules.Winner(v.Trick.Append(new PublicPlay(v.Seat, p)).ToArray(), v.Contract);
                    return declarer == v.Seat ? winner != declarer : winner == declarer;
                }).ToArray();
                if (relevant.Length > 0)
                    return declarer == v.Seat
                    ? relevant.OrderByDescending(p => v.Rules.Rank(p.Card, v.Contract)).ThenBy(p => p.Card.Suit).First()
                    : Cheapest(v, relevant);
            }
            if (declarer == v.Seat || v.Players[declarer].ExposedHand == null)
                return null;
            var unknown = Unknown(v);
            foreach (var move in legal.OrderBy(p => v.Rules.Rank(p.Card, v.Contract)).ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit))
            {
                var prefix = v.Trick.Append(new PublicPlay(v.Seat, move)).ToList();
                if (AllRepliesBust(prefix))
                    return move;
            }
            return null;

            bool AllRepliesBust(List<PublicPlay> prefix)
            {
                if (prefix.Count == 3)
                    return v.Rules.Winner(prefix, v.Contract!) == declarer;
                int next = (prefix[^1].Seat + 1) % 4;
                if (next == (declarer + 2) % 4)
                    next = (next + 1) % 4;
                var exposed = v.Players[next].ExposedHand;
                // Unknown hands are deliberately over-approximated: even off-suit replies are allowed.
                // A proof that survives this superset cannot depend on a guessed hidden allocation.
                var replies = exposed != null ? v.Rules.LegalPlays(exposed, prefix, v.Contract!)
                    : unknown.Where(c => !prefix.Any(p => p.Action.Card == c)).Select(c => new PlayAction(c)).ToList();
                if (replies.Count == 0)
                    return false;
                foreach (var reply in replies)
                {
                    prefix.Add(new(next, reply));
                    bool bust = AllRepliesBust(prefix);
                    prefix.RemoveAt(prefix.Count - 1);
                    if (!bust)
                        return false;
                }
                return true;
            }
        }

        internal static PlayAction? ProtectPartnerWinner(Observation v, IReadOnlyList<PlayAction> legal)
        {
            if (v.Trick.Count == 0 || v.Contract!.IsMisere || v.Rules.Winner(v.Trick, v.Contract) != Partner(v))
                return null;
            var led = v.Rules.LedSuit(v.Trick, v.Contract);
            var followers = legal.Where(p => v.Rules.EffectiveSuit(p.Card, v.Contract) == led && !p.Card.IsJoker
                && v.Rules.Winner(v.Trick.Append(new PublicPlay(v.Seat, p)).ToArray(), v.Contract) == Partner(v)).ToArray();
            if (followers.Length < 2)
                return null;
            // Do not impose preservation when public repeated leads suggest a long suit to unblock.
            if (v.Hand.Count(c => v.Rules.EffectiveSuit(c, v.Contract) == led) <= 2 &&
                v.History.Where((p, i) => i % 4 == 0 && p.Seat == Partner(v) && v.Rules.LedSuit([p], v.Contract) == led).Count() >= 2)
                return null;
            var low = Cheapest(v, followers);
            var after = v.Trick.Append(new PublicPlay(v.Seat, low)).ToArray();
            var unknown = Unknown(v);
            for (int i = v.Trick.Count + 1; i < 4; i++)
            {
                int next = (v.Trick[0].Seat + i) % 4;
                if (Possible(v, next, unknown).Any(c => v.Rules.Winner(after.Append(new PublicPlay(next, new(c))).ToArray(), v.Contract) != Partner(v)))
                    return null;
            }
            return low;
        }

        internal static (PlayAction Play, string Reason)? PartnerLead(Observation v, IReadOnlyList<PlayAction> legal)
        {
            if (v.Trick.Count != 0 || v.Contract!.IsMisere || v.Players[Partner(v)].CardCount == 0)
                return null;
            // Cashing a publicly certain winner or established run takes precedence over a convention.
            if (legal.Any(p => !p.Card.IsJoker && CertainLeadWin(v, p)))
                return null;
            var frame = new CardFrameV1(v);
            var trump = v.Contract.Trump;
            bool OpponentCanRuff(Suit suit) => trump != Suit.Unknown && v.Players.Where(p => p.Seat % 2 != v.Seat % 2)
                .Any(p => frame.IsVoid(p.Seat, (int)suit) && !frame.IsVoid(p.Seat, (int)trump));
            if (trump != Suit.Unknown && !frame.IsVoid(Partner(v), (int)trump)
                && v.Hand.Count(c => v.Rules.EffectiveSuit(c, v.Contract) == trump) < 3
                && v.Auction.Any(a => a.Seat == Partner(v) && a.Bid is { IsMisere: false } b && b.Trump == trump))
            {
                var ruffs = legal.Where(p => !p.Card.IsJoker && v.Rules.EffectiveSuit(p.Card, v.Contract) != trump
                    && frame.IsVoid(Partner(v), (int)v.Rules.EffectiveSuit(p.Card, v.Contract))
                    && !OpponentCanRuff(v.Rules.EffectiveSuit(p.Card, v.Contract))).ToArray();
                if (ruffs.Length > 0)
                    return (Cheapest(v, ruffs), "offer-partner-ruff");
            }
            // Declared convention: a low non-trump discard requests that suit. Only respond with
            // a useful own holding; a singleton dump alone is not evidence that partner has strength.
            Suit request = Suit.Unknown;
            for (int i = 0; i < v.History.Count; i += 4)
            {
                var trick = v.History.Skip(i).Take(4).ToArray();
                if (trick.Length != 4)
                    break;
                var discard = trick.FirstOrDefault(p => p.Seat == Partner(v));
                if (discard == null)
                    continue;
                var suit = v.Rules.EffectiveSuit(discard.Action.Card, v.Contract);
                if (!discard.Action.Card.IsJoker && discard.Action.Card.Rank <= 7 && suit != trump && suit != v.Rules.LedSuit(trick, v.Contract))
                    request = suit;
            }
            if (request == Suit.Unknown || frame.IsVoid(Partner(v), (int)request) || OpponentCanRuff(request))
                return null;
            var held = legal.Where(p => !p.Card.IsJoker && v.Rules.EffectiveSuit(p.Card, v.Contract) == request).ToArray();
            if (held.Length >= 3 && held.Max(p => v.Rules.Rank(p.Card, v.Contract)) >= 12)
                return (Cheapest(v, held), "return-partner-request");
            return null;
        }

        internal static PlayingCard[] RepairDiscards(Observation v, PlayingCard[] original)
        {
            if (original.Length == 0)
                return original;
            var c = v.Contract!;
            var selected = original.ToArray();
            bool JokerCorrect(IEnumerable<PlayingCard> discard) => !v.Hand.Any(p => p.IsJoker) || (c.IsMisere ? discard.Any(p => p.IsJoker) : !discard.Any(p => p.IsJoker));
            int BareKings(IEnumerable<PlayingCard> discard)
            {
                var keep = v.Hand.Except(discard).ToArray();
                return Rules.Suits.Count(s => v.Hand.Count(p => p.Suit == s) >= 2 && keep.Count(p => p.Suit == s) == 1 && keep.Any(p => p.Suit == s && p.Rank == 13));
            }
            double Value(IEnumerable<PlayingCard> d) => HandEvaluator.RetainedValue(v.Hand.Except(d).ToArray(), c, v.Rules);
            // At most three one-card swaps. Repair local structure without another round of rollouts.
            for (int pass = 0; pass < original.Length; pass++)
            {
                bool fixJoker = !JokerCorrect(selected), fixKing = !c.IsMisere && c.Trump == Suit.Unknown && BareKings(selected) > 0;
                if (!fixJoker && !fixKing)
                    break;
                var swaps = selected.SelectMany((_, i) => v.Hand.Except(selected).Select(card => selected.Select((p, j) => i == j ? card : p).ToArray()))
                    .Where(d => JokerCorrect(d) && (!fixKing || BareKings(d) < BareKings(selected)))
                    .Where(d => fixJoker || d.Except(selected).All(card => !card.IsJoker && card.Rank <= 10))
                    .Where(d => fixJoker || Value(d) >= Value(selected) - .15)
                    .OrderByDescending(Value).ThenBy(d => string.Join(",", d.OrderBy(p => p.Suit).ThenBy(p => p.Rank)), StringComparer.Ordinal).ToArray();
                if (swaps.Length == 0)
                    break;
                selected = swaps[0];
            }
            return selected.OrderBy(p => p.Suit).ThenBy(p => p.Rank).ToArray();
        }
    }
}
