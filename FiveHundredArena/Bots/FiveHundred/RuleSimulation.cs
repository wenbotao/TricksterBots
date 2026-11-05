using Trickster.cloud;
namespace FiveHundredArena;

internal static class RuleSamples
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
        if (deadCount < 0 || deadCount > ConfiguredRules.Number(r.Section("deal"), "kittySize"))
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
                    if (seen[j] || r.Declarations.ContainsKey(cards[card]) && targets[j] != view.Declarer || targets[j] >= 0 && view.Contract != null && voids[targets[j]].Contains(effective![cards[card]]))
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
internal static class RuleTactics
{
    internal static PlayAction Play(Observation view, ConfiguredRules r, IReadOnlyList<PlayAction> legal, bool weak = false)
    {
        var c = view.Contract!;
        var sorted = legal.OrderBy(p => RuleAwarePlayer.Danger(view, p.Card)).ThenBy(p => p.Card.Suit).ThenBy(p => p.JokerLeadSuit).ToArray();
        if (view.Trick.Count == 0)
        {
            if (c.IsMisere)
                return view.Declarer != view.Seat && view.Declarer % 2 == view.Seat % 2 ? sorted[^1] : sorted[0];
            return weak ? sorted[^1] : sorted.OrderByDescending(p => view.Hand.Count(x => r.EffectiveSuit(x, c) == r.EffectiveSuit(p.Card, c))).ThenByDescending(p => r.Rank(p.Card, c)).First();
        }
        int Winner(PlayAction p) => r.Winner(view.Trick.Append(new PublicPlay(view.Seat, p)).ToArray(), c);
        if (c.IsMisere)
        {
            if (view.Declarer == view.Seat)
            {
                var duck = sorted.Where(p => Winner(p) != view.Seat).ToArray();
                return duck.Length > 0 ? duck[^1] : sorted[0];
            }
            if (view.Declarer % 2 == view.Seat % 2)
            {
                var cover = sorted.Where(p => Winner(p) != view.Declarer).ToArray();
                return cover.Length > 0 ? cover[^1] : sorted[0];
            }
            var bust = sorted.Where(p => Winner(p) == view.Declarer).ToArray();
            if (bust.Length > 0)
                return bust[0];
            return weak ? sorted[0] : sorted[^1];
        }
        if (r.Winner(view.Trick, c) % 2 == view.Seat % 2)
            return sorted.FirstOrDefault(p => Winner(p) % 2 == view.Seat % 2) ?? sorted[0];
        return sorted.FirstOrDefault(p => Winner(p) == view.Seat) ?? sorted[0];
    }
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
                var replace = selected.Where(p => !p.IsJoker).OrderBy(p => RuleAwarePlayer.Danger(view, p)).FirstOrDefault();
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
                .Where(d => Bare(d) < Bare(before) && !d.Any(p => p.IsJoker && high) && RuleAwarePlayer.RetainedValue(view.Hand.Except(d).ToArray(), c, r) >= RuleAwarePlayer.RetainedValue(view.Hand.Except(before).ToArray(), c, r) - .15)
                .OrderByDescending(d => RuleAwarePlayer.RetainedValue(view.Hand.Except(d).ToArray(), c, r)).FirstOrDefault();
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
        bool Prove(List<PublicPlay> prefix, int target)
        {
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
            foreach (var move in legal.OrderBy(p => RuleAwarePlayer.Danger(view, p.Card)))
                if (Prove(view.Trick.Append(new PublicPlay(view.Seat, move)).ToList(), (view.Seat + 2) % 4))
                    return move;
        return PartnerLead(view, r, legal);
    }
}
internal sealed class RuleRollout
{
    private readonly Observation root;
    private readonly ConfiguredRules rules;
    private readonly List<PlayingCard>[] hands;
    private readonly List<PublicPlay> history, trick;
    private readonly int[] tricks;
    private readonly List<PlayingCard>[] discards = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
    private readonly Contract contract;
    private readonly int declarer, active, inactive;
    private bool exposed;
    private RuleRollout(Observation view, ConfiguredRules r, SampledDeal world)
    {
        root = view;
        rules = r with
        {
            Declarations = new(r.Declarations),
            History = view.History.ToArray()
        };
        hands = world.Hands.Select(h => h.ToList()).ToArray();
        history = view.History.ToList();
        trick = view.Trick.ToList();
        tricks = view.Players.Select(p => p.Tricks).ToArray();
        contract = view.Contract!;
        declarer = view.Declarer!.Value;
        active = ConfiguredRules.Text(r.ContractOptions(contract), "activeSeats") == "all_four" ? 4 : 3;
        inactive = active == 3 ? (declarer + 2) % 4 : -1;
        exposed = view.Players[declarer].ExposedHand != null;
        discards[view.Seat].AddRange(view.OwnDiscards);
        if (declarer != view.Seat && ConfiguredRules.Text(r.ContractOptions(contract), "kitty") == "take_and_discard")
            discards[declarer].AddRange(world.Dead);
    }
    internal static double Play(Observation view, ConfiguredRules r, SampledDeal world, PlayAction move)
    {
        var model = new RuleRollout(view, r, world);
        model.Add(view.Seat, move);
        return model.Finish(model.Next(view.Seat));
    }
    internal static double Discard(Observation view, ConfiguredRules r, SampledDeal world, PlayingCard[] move)
    {
        var model = new RuleRollout(view, r, world);
        foreach (var c in move)
        {
            model.hands[view.Seat].Remove(c);
            model.discards[view.Seat].Add(c);
        }
        var options = r.ContractOptions(view.Contract!);
        string first = ConfiguredRules.Text(options, "firstLead");
        int seat = first == "left_of_declarer" ? (model.declarer + 1) % 4 : first == "left_of_dealer" ? (view.Dealer + 1) % 4 : model.declarer;
        if (seat == model.inactive)
            seat = model.Next(seat);
        model.exposed = ConfiguredRules.Text(options.GetProperty("exposure"), "timing") == "before_first_lead";
        return model.Finish(seat);
    }
    private int Next(int seat)
    {
        do
        {
            seat = (seat + 1) % 4;
        } while (seat == inactive);
        return seat;
    }
    private void Add(int seat, PlayAction move)
    {
        if (!hands[seat].Remove(move.Card))
            throw new InvalidOperationException("Simulation card absent");
        var p = new PublicPlay(seat, move);
        trick.Add(p);
        history.Add(p);
    }
    private double Finish(int seat)
    {
        while (tricks.Sum() < 10)
        {
            if (trick.Count == active)
            {
                seat = rules.Winner(trick, contract);
                tricks[seat]++;
                trick.Clear();
                if (ConfiguredRules.Text(rules.ContractOptions(contract).GetProperty("exposure"), "timing") == "after_first_trick")
                    exposed = true;
                continue;
            }
            rules.History = history.ToArray();
            rules.CurrentSeat = seat;
            rules.DeclarerSeat = declarer;
            var players = root.Players.Select(p => p with { CardCount = hands[p.Seat].Count, Tricks = tricks[p.Seat], ExposedHand = exposed && p.Seat == declarer ? hands[p.Seat].ToArray() : null }).ToArray();
            var view = root with
            {
                Seat = seat,
                Hand = hands[seat].ToArray(),
                Players = players,
                History = history.ToArray(),
                Trick = trick.ToArray(),
                Rules = rules,
                OwnDiscards = discards[seat].ToArray()
            };
            var legal = rules.LegalPlays(view.Hand, view.Trick, contract);
            Add(seat, RuleTactics.Play(view, rules, legal));
            seat = Next(seat);
        }
        return Utility(root, rules, contract, declarer, tricks);
    }
    // Match the engine: apply defensive caps before checking terminal scores.
    internal static double Utility(Observation view, ConfiguredRules r, Contract contract, int declarer, int[] tricks)
    {
        var (made, delta) = r.Score(contract, declarer, tricks);
        int defending = 1 - declarer % 2;
        var cap = r.Section("scoring").GetProperty("defensiveScoreCap");
        if (cap.ValueKind != System.Text.Json.JsonValueKind.Null)
            delta[defending] = Math.Min(delta[defending], Math.Max(0, cap.GetInt32() - view.Scores[defending]));
        var scores = view.Scores.Select((x, i) => x + delta[i]).ToArray();
        var winner = r.WinningTeam(scores, declarer % 2, made);
        int team = view.Seat % 2;
        return winner.HasValue ? (winner == team ? 1 : -1) * 4.0 * r.WinScore : delta[team] - delta[1 - team];
    }
}
