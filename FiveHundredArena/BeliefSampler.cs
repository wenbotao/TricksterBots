using Trickster.cloud;

namespace FiveHundredArena;

/// <summary>A hypothetical current deal, not the engine's actual hidden state. Dead holds kitty or discards.</summary>
public sealed record SampledDeal(PlayingCard[][] Hands, PlayingCard[] Dead, double Weight);

/// <summary>Randomized constrained matching with soft auction likelihoods; not an exact posterior sampler.</summary>
public static class BeliefSampler
{
    public static IReadOnlyList<SampledDeal> Sample(Observation v, int count, SeededRandom random)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var fixedHands = Enumerable.Range(0, 4).Select(s => s == v.Seat ? v.Hand.ToArray() : v.Players[s].ExposedHand?.ToArray()).ToArray();
        var known = v.History.Select(p => p.Action.Card).Concat(v.OwnDiscards).Concat(fixedHands.Where(h => h != null).SelectMany(h => h!)).ToArray();
        var deck = v.Rules.Deck();
        if (known.Distinct().Count() != known.Length || known.Any(c => !deck.Contains(c))) throw new InvalidDataException("Inconsistent observed cards");
        var unknown = deck.Except(known).ToArray();
        var voids = Voids(v);
        var slots = new List<int>();
        for (int s = 0; s < 4; s++)
        {
            if (fixedHands[s] != null)
            {
                if (fixedHands[s]!.Length != v.Players[s].CardCount) throw new InvalidDataException("Exposed hand count mismatch");
                if (v.Contract != null && fixedHands[s]!.Any(c => voids[s].Contains(v.Rules.EffectiveSuit(c, v.Contract)))) throw new InvalidDataException("Observed hand contradicts a public void");
            }
            else slots.AddRange(Enumerable.Repeat(s, v.Players[s].CardCount));
        }
        int deadCount = unknown.Length - slots.Count;
        if (deadCount is < 0 or > 3) throw new InvalidDataException("Inconsistent unknown hand/kitty counts");
        slots.AddRange(Enumerable.Repeat(-1, deadCount));
        var result = new List<SampledDeal>();
        for (int sample = 0; sample < count; sample++)
        {
            var cards = unknown.ToArray(); random.Shuffle(cards);
            var targets = slots.ToArray(); random.Shuffle(targets);
            var assigned = Enumerable.Repeat(-1, targets.Length).ToArray();
            bool Allowed(int card, int target) => targets[target] < 0 || v.Contract == null || !voids[targets[target]].Contains(v.Rules.EffectiveSuit(cards[card], v.Contract));
            bool Place(int card, bool[] seen)
            {
                for (int target = 0; target < targets.Length; target++)
                {
                    if (seen[target] || !Allowed(card, target)) continue;
                    seen[target] = true;
                    if (assigned[target] < 0 || Place(assigned[target], seen)) { assigned[target] = card; return true; }
                }
                return false;
            }
            for (int i = 0; i < cards.Length; i++)
                if (!Place(i, new bool[targets.Length])) throw new InvalidDataException("No hidden deal satisfies public card counts and voids");
            var hands = fixedHands.Select(h => h?.ToList() ?? []).ToArray();
            var dead = new List<PlayingCard>();
            for (int i = 0; i < assigned.Length; i++)
                if (targets[i] < 0) dead.Add(cards[assigned[i]]); else hands[targets[i]].Add(cards[assigned[i]]);
            result.Add(new(hands.Select(h => h.ToArray()).ToArray(), dead.ToArray(), AuctionWeight(v, hands)));
        }
        return result;
    }
    public static IReadOnlyList<HashSet<Suit>> Voids(Observation v)
    {
        var result = Enumerable.Range(0, 4).Select(_ => new HashSet<Suit>()).ToArray();
        if (v.Contract == null) return result;
        int active = v.Contract.IsMisere ? 3 : 4;
        for (int first = 0; first < v.History.Count; first += active)
        {
            var trick = v.History.Skip(first).Take(active).ToArray();
            Suit led = v.Rules.LedSuit(trick, v.Contract);
            foreach (var p in trick.Skip(1))
                if (v.Rules.EffectiveSuit(p.Action.Card, v.Contract) != led) result[p.Seat].Add(led);
        }
        return result;
    }
    private static double AuctionWeight(Observation v, List<PlayingCard>[] hands)
    {
        double logWeight = 0;
        foreach (var bids in v.Auction.Where(a => a.Seat != v.Seat && a.Bid != null).GroupBy(a => a.Seat))
        {
            var bid = bids.Last().Bid!;
            var initialApproximation = hands[bids.Key].Concat(v.History.Where(p => p.Seat == bids.Key).Select(p => p.Action.Card)).ToArray();
            if (bid.IsMisere) logWeight += Math.Clamp(.08 * (HandEvaluator.HandValue(initialApproximation, bid, v.Rules) + 6), -.7, .7);
            else logWeight += Math.Clamp(.18 * (HandEvaluator.HandValue(initialApproximation, bid, v.Rules) - (bid.Tricks - 2.5)), -.7, .7);
        }
        return Math.Exp(Math.Clamp(logWeight, -2, 2));
    }
}
