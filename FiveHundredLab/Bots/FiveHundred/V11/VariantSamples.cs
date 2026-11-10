using Trickster.cloud;
using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred.V11;

internal static class VariantSamples
{
    // Match unknown cards to legal public-information slots; never read the real hidden deal.
    internal static IReadOnlyList<SampledDeal> Sample(Observation view, ConfiguredRules r, int count, SeededRandom random)
    {
        var knownHands = view.Players.Select(p => p.Seat == view.Seat ? view.Hand.ToArray() : p.ExposedHand?.ToArray()).ToArray();
        var known = view.History.Select(p => p.Action.Card).Concat(view.OwnDiscards).Concat(knownHands.Where(h => h != null).SelectMany(h => h!)).ToArray();
        if (known.Distinct().Count() != known.Length)
            throw new InvalidDataException("Duplicate observed cards");
        var unknown = r.Deck().Except(known).ToArray();
        var slots = new List<int>();
        for (int s = 0; s < 4; s++)
            if (knownHands[s] == null)
                slots.AddRange(Enumerable.Repeat(s, view.Players[s].CardCount));
        int deadCount = unknown.Length - slots.Count;
        if (deadCount < 0 || deadCount > ConfiguredRules.Number(r.Section("deal"), "kittySize") + (view.Contract?.Kind == ContractKind.GrandSlam ? 1 : 0))
            throw new InvalidDataException("Invalid unknown counts");
        slots.AddRange(Enumerable.Repeat(-1, deadCount));
        var voids = Enumerable.Range(0, 4).Select(_ => new HashSet<Suit>()).ToArray();
        if (view.Contract != null && ConfiguredRules.Flag(r.Section("play"), "mustFollowEffectiveSuit"))
        {
            int active = ConfiguredRules.Text(r.ContractOptions(view.Contract), "activeSeats") == "all_four" ? 4 : 3;
            for (int i = 0; i < view.History.Count; i += active)
            {
                var lead = r.LedSuit([view.History[i]], view.Contract);
                foreach (var p in view.History.Skip(i + 1).Take(Math.Min(active - 1, view.History.Count - i - 1)))
                    if (!p.Action.Card.IsJoker && r.EffectiveSuit(p.Action.Card, view.Contract) != lead)
                        voids[p.Seat].Add(lead);
            }
        }
        // Effective suit depends on the public rule snapshot, not on a sampled allocation.
        var effective = view.Contract == null ? null : r.Deck().ToDictionary(card => card, card => r.EffectiveSuit(card, view.Contract));
        var result = new List<SampledDeal>();
        for (int sample = 0; sample < count; sample++)
        {
            var cards = unknown.ToArray();
            random.Shuffle(cards);
            var targets = slots.ToArray();
            random.Shuffle(targets);
            var assigned = Enumerable.Repeat(-1, targets.Length).ToArray();
            bool AssignCard(int card, bool[] seen)
            {
                for (int j = 0; j < targets.Length; j++)
                {
                    if (seen[j] || r.Declarations.ContainsKey(cards[card]) && targets[j] != r.DeclarationOwners.GetValueOrDefault(cards[card], view.Declarer ?? -1) || targets[j] >= 0 && view.Contract != null && voids[targets[j]].Contains(effective![cards[card]]))
                        continue;
                    seen[j] = true;
                    if (assigned[j] < 0 || AssignCard(assigned[j], seen))
                    {
                        assigned[j] = card;
                        return true;
                    }
                }
                return false;
            }
            for (int i = 0; i < cards.Length; i++)
                if (!AssignCard(i, new bool[targets.Length]))
                    throw new InvalidDataException("No sample satisfies public voids");
            var hands = knownHands.Select(h => h?.ToList() ?? new List<PlayingCard>()).ToArray();
            var dead = new List<PlayingCard>();
            for (int j = 0; j < targets.Length; j++)
                if (targets[j] < 0)
                    dead.Add(cards[assigned[j]]);
                else
                    hands[targets[j]].Add(cards[assigned[j]]);
            result.Add(new(hands.Select(h => h.ToArray()).ToArray(), dead.ToArray(), 1));
        }
        return result;
    }
}
