using FiveHundredArena;
using Trickster.cloud;

namespace FiveHundredLab;

// No hidden deal access, learned model, scenario IDs or test predicates. All evidence comes from Observation.
internal static class PartnerPolicyV9
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
        var prefix = new[] { new PublicPlay(v.Seat, lead) }; var unknown = Unknown(v);
        return v.Players.Where(p => p.Seat != v.Seat && (!v.Contract!.IsMisere || p.Seat != (v.Declarer + 2) % 4))
            .All(p => Possible(v, p.Seat, unknown).All(c => v.Rules.Winner([prefix[0], new(p.Seat, new(c))], v.Contract!) == v.Seat));
    }

    internal static PlayAction? ImmediateMiserePlay(Observation v, IReadOnlyList<PlayAction> legal)
    {
        if (v.Contract?.IsMisere != true || v.Declarer == null || v.Players[v.Declarer.Value].Tricks > 0) return null;
        int declarer = v.Declarer.Value;
        if (v.Trick.Count == 2)
        {
            var relevant = legal.Where(p =>
            {
                int winner = v.Rules.Winner(v.Trick.Append(new PublicPlay(v.Seat, p)).ToArray(), v.Contract);
                return declarer == v.Seat ? winner != declarer : winner == declarer;
            }).ToArray();
            if (relevant.Length > 0) return declarer == v.Seat
                ? relevant.OrderByDescending(p => v.Rules.Rank(p.Card, v.Contract)).ThenBy(p => p.Card.Suit).First()
                : Cheapest(v, relevant);
        }
        if (declarer == v.Seat || v.Players[declarer].ExposedHand == null) return null;
        var unknown = Unknown(v);
        foreach (var move in legal.OrderBy(p => v.Rules.Rank(p.Card, v.Contract)).ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit))
        {
            var prefix = v.Trick.Append(new PublicPlay(v.Seat, move)).ToList();
            if (AllRepliesBust(prefix)) return move;
        }
        return null;

        bool AllRepliesBust(List<PublicPlay> prefix)
        {
            if (prefix.Count == 3) return v.Rules.Winner(prefix, v.Contract!) == declarer;
            int next = (prefix[^1].Seat + 1) % 4;
            if (next == (declarer + 2) % 4) next = (next + 1) % 4;
            var exposed = v.Players[next].ExposedHand;
            // Unknown hands are deliberately over-approximated: even off-suit replies are allowed.
            // A proof that survives this superset cannot depend on a guessed hidden allocation.
            var replies = exposed != null ? v.Rules.LegalPlays(exposed, prefix, v.Contract!)
                : unknown.Where(c => !prefix.Any(p => p.Action.Card == c)).Select(c => new PlayAction(c)).ToList();
            if (replies.Count == 0) return false;
            foreach (var reply in replies)
            {
                prefix.Add(new(next, reply)); bool bust = AllRepliesBust(prefix); prefix.RemoveAt(prefix.Count - 1);
                if (!bust) return false;
            }
            return true;
        }
    }

    internal static PlayAction? ProtectPartnerWinner(Observation v, IReadOnlyList<PlayAction> legal)
    {
        if (v.Trick.Count == 0 || v.Contract!.IsMisere || v.Rules.Winner(v.Trick, v.Contract) != Partner(v)) return null;
        var led = v.Rules.LedSuit(v.Trick, v.Contract);
        var followers = legal.Where(p => v.Rules.EffectiveSuit(p.Card, v.Contract) == led && !p.Card.IsJoker
            && v.Rules.Winner(v.Trick.Append(new PublicPlay(v.Seat, p)).ToArray(), v.Contract) == Partner(v)).ToArray();
        if (followers.Length < 2) return null;
        // Do not impose preservation when public repeated leads suggest a long suit to unblock.
        if (v.Hand.Count(c => v.Rules.EffectiveSuit(c, v.Contract) == led) <= 2 &&
            v.History.Where((p, i) => i % 4 == 0 && p.Seat == Partner(v) && v.Rules.LedSuit([p], v.Contract) == led).Count() >= 2) return null;
        var low = Cheapest(v, followers); var after = v.Trick.Append(new PublicPlay(v.Seat, low)).ToArray();
        var unknown = Unknown(v);
        for (int i = v.Trick.Count + 1; i < 4; i++)
        {
            int next = (v.Trick[0].Seat + i) % 4;
            if (Possible(v, next, unknown).Any(c => v.Rules.Winner(after.Append(new PublicPlay(next, new(c))).ToArray(), v.Contract) != Partner(v))) return null;
        }
        return low;
    }

    internal static (PlayAction Play, string Reason)? PartnerLead(Observation v, IReadOnlyList<PlayAction> legal)
    {
        if (v.Trick.Count != 0 || v.Contract!.IsMisere || v.Players[Partner(v)].CardCount == 0) return null;
        // Cashing a publicly certain winner or established run takes precedence over a convention.
        if (legal.Any(p => !p.Card.IsJoker && CertainLeadWin(v, p))) return null;
        var frame = new CardFrameV1(v); var trump = v.Contract.Trump;
        bool OpponentCanRuff(Suit suit) => trump != Suit.Unknown && v.Players.Where(p => p.Seat % 2 != v.Seat % 2)
            .Any(p => frame.IsVoid(p.Seat, (int)suit) && !frame.IsVoid(p.Seat, (int)trump));
        if (trump != Suit.Unknown && !frame.IsVoid(Partner(v), (int)trump)
            && v.Hand.Count(c => v.Rules.EffectiveSuit(c, v.Contract) == trump) < 3
            && v.Auction.Any(a => a.Seat == Partner(v) && a.Bid is { IsMisere: false } b && b.Trump == trump))
        {
            var ruffs = legal.Where(p => !p.Card.IsJoker && v.Rules.EffectiveSuit(p.Card, v.Contract) != trump
                && frame.IsVoid(Partner(v), (int)v.Rules.EffectiveSuit(p.Card, v.Contract))
                && !OpponentCanRuff(v.Rules.EffectiveSuit(p.Card, v.Contract))).ToArray();
            if (ruffs.Length > 0) return (Cheapest(v, ruffs), "offer-partner-ruff");
        }
        // Declared convention: a low non-trump discard requests that suit. Only respond with
        // a useful own holding; a singleton dump alone is not evidence that partner has strength.
        Suit request = Suit.Unknown;
        for (int i = 0; i < v.History.Count; i += 4)
        {
            var trick = v.History.Skip(i).Take(4).ToArray();
            if (trick.Length != 4) break;
            var discard = trick.FirstOrDefault(p => p.Seat == Partner(v));
            if (discard == null) continue;
            var suit = v.Rules.EffectiveSuit(discard.Action.Card, v.Contract);
            if (!discard.Action.Card.IsJoker && discard.Action.Card.Rank <= 7 && suit != trump && suit != v.Rules.LedSuit(trick, v.Contract)) request = suit;
        }
        if (request == Suit.Unknown || frame.IsVoid(Partner(v), (int)request) || OpponentCanRuff(request)) return null;
        var held = legal.Where(p => !p.Card.IsJoker && v.Rules.EffectiveSuit(p.Card, v.Contract) == request).ToArray();
        if (held.Length >= 3 && held.Max(p => v.Rules.Rank(p.Card, v.Contract)) >= 12) return (Cheapest(v, held), "return-partner-request");
        return null;
    }

    internal static PlayingCard[] RepairDiscards(Observation v, PlayingCard[] original)
    {
        if (original.Length == 0) return original;
        var c = v.Contract!; var selected = original.ToArray();
        bool JokerCorrect(IEnumerable<PlayingCard> discard) => !v.Hand.Any(p => p.IsJoker) || (c.IsMisere ? discard.Any(p => p.IsJoker) : !discard.Any(p => p.IsJoker));
        int BareKings(IEnumerable<PlayingCard> discard)
        {
            var keep = v.Hand.Except(discard).ToArray();
            return Rules.Suits.Count(s => v.Hand.Count(p => p.Suit == s) >= 2 && keep.Count(p => p.Suit == s) == 1 && keep.Any(p => p.Suit == s && p.Rank == 13));
        }
        double Value(IEnumerable<PlayingCard> d) => LegacyFeaturesV1.RetainedValue(v.Hand.Except(d).ToArray(), c, v.Rules);
        // At most three one-card swaps. Repair local structure without another round of rollouts.
        for (int pass = 0; pass < original.Length; pass++)
        {
            bool fixJoker = !JokerCorrect(selected), fixKing = !c.IsMisere && c.Trump == Suit.Unknown && BareKings(selected) > 0;
            if (!fixJoker && !fixKing) break;
            var swaps = selected.SelectMany((_, i) => v.Hand.Except(selected).Select(card => selected.Select((p, j) => i == j ? card : p).ToArray()))
                .Where(d => JokerCorrect(d) && (!fixKing || BareKings(d) < BareKings(selected)))
                .Where(d => fixJoker || d.Except(selected).All(card => !card.IsJoker && card.Rank <= 10))
                .Where(d => fixJoker || Value(d) >= Value(selected) - .15)
                .OrderByDescending(Value).ThenBy(d => string.Join(",", d.OrderBy(p => p.Suit).ThenBy(p => p.Rank)), StringComparer.Ordinal).ToArray();
            if (swaps.Length == 0) break;
            selected = swaps[0];
        }
        return selected.OrderBy(p => p.Suit).ThenBy(p => p.Rank).ToArray();
    }
}
