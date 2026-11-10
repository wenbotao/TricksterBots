using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

// Decision-local forward model. Auction/scoring match DefaultRollout; play uses direct Medium inputs.
internal sealed class FlowCachedRollout
{
    private readonly Observation root;
    private readonly FlowPolicyCache continuation;
    private readonly PlayRolloutCounter work;
    private readonly Action<PublicPlay>? onSimulatedPlay;
    private readonly List<Suit>[] voids;
    private readonly List<PlayingCard>[] hands;
    private readonly List<PlayingCard>[] taken;
    private readonly List<AuctionAction> auction;
    private readonly List<PublicPlay> history;
    private readonly List<PublicPlay> trick;
    private readonly int[] tricks;
    private readonly List<Card> completedCards;
    private IReadOnlyList<Card>? completedSnapshot;
    private readonly List<PlayingCard>[] knownDiscards = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
    private readonly PlayingCard[] dead;
    private Contract? contract;
    private int declarer;
    private bool exposed;
    internal FlowCachedRollout(Observation view, SampledDeal sample, FlowPolicyCache continuation, PlayRolloutCounter work, Action<PublicPlay>? onSimulatedPlay = null)
    {
        this.continuation = continuation;
        this.work = work;
        this.onSimulatedPlay = onSimulatedPlay;
        work.Evaluations++;
        voids = continuation.ForkVoids(view.Contract);
        root = view;
        hands = sample.Hands.Select(h => h.ToList()).ToArray();
        dead = sample.Dead.ToArray();
        taken = view.Players.Select(p => p.Taken.ToList()).ToArray();
        auction = view.Auction.ToList();
        history = view.History.ToList();
        trick = view.Trick.ToList();
        tricks = view.Players.Select(p => p.Tricks).ToArray();
        completedCards = view.History.Take(view.History.Count - view.Trick.Count).Select(p => p.Action.Card.ToBotCard()).ToList();
        contract = view.Contract;
        declarer = view.Declarer ?? -1;
        exposed = contract?.Kind == ContractKind.OpenMisere && history.Count - trick.Count >= 3;
        knownDiscards[view.Seat].AddRange(view.OwnDiscards);
    }
    private Observation View(int seat)
    {
        var players = Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, hands[s].Count, root.Scores[s % 2],
            auction.Where(a => a.Seat == s).Select(a => a.Bid).ToArray(), tricks[s], taken[s].ToArray(), exposed && s == declarer ? hands[s].ToArray() : null)).ToArray();
        return new(seat, root.Dealer, root.Round, hands[seat].ToArray(), players, auction.ToArray(), contract, declarer < 0 ? null : declarer,
            trick.ToArray(), history.ToArray(), root.Scores.ToArray(), root.Rules with
            {
            }, knownDiscards[seat].ToArray());
    }
    internal double Bid(Contract? choice)
    {
        var passed = new bool[4];
        int consecutive = 0;
        foreach (var a in auction)
        {
            if (a.Bid == null)
            {
                passed[a.Seat] = true;
                consecutive++;
            }
            else
                consecutive = 0;
        }
        void Apply(int seat, Contract? bid)
        {
            auction.Add(new(seat, bid));
            if (bid == null)
            {
                passed[seat] = true;
                consecutive++;
            }
            else
            {
                contract = bid;
                declarer = seat;
                consecutive = 0;
            }
        }
        bool Done() => contract == null ? consecutive >= 4 : root.Rules.PassIsPermanent
            ? Enumerable.Range(0, 4).All(s => s == declarer || passed[s]) : consecutive >= 3;
        Apply(root.Seat, choice);
        int seat = (root.Seat + 1) % 4;
        var bidder = continuation;
        while (!Done())
        {
            if (!root.Rules.PassIsPermanent || !passed[seat])
            {
                var view = View(seat);
                var legal = root.Rules.LegalBids(contract, auction);
                Apply(seat, bidder.Bid(view, legal));
            }
            seat = (seat + 1) % 4;
        }
        if (contract == null)
            return 0; // passed out
        hands[declarer].AddRange(dead);
        DiscardFor(declarer, continuation.Discard(View(declarer), dead.Length));
        return Finish(declarer);
    }
    private void DiscardFor(int seat, IReadOnlyList<PlayingCard> cards)
    {
        foreach (var card in cards)
            hands[seat].Remove(card);
        knownDiscards[seat].AddRange(cards);
    }
    internal double Discard(IReadOnlyList<PlayingCard> choice)
    {
        DiscardFor(root.Seat, choice);
        return Finish(declarer);
    }
    internal double Play(PlayAction choice)
    {
        // In a defender's sampled world, Dead represents the declarer's private discards.
        if (root.Seat != declarer)
            knownDiscards[declarer].AddRange(dead);
        AddPlay(root.Seat, choice);
        return Finish(NextActive(root.Seat));
    }
    private int NextActive(int seat)
    {
        do
            seat = (seat + 1) % 4;
        while (contract!.IsMisere && seat == (declarer + 2) % 4);
        return seat;
    }
    private void AddPlay(int seat, PlayAction play)
    {
        if (!hands[seat].Remove(play.Card))
            throw new InvalidOperationException("Simulation card absent from acting hand");
        var p = new PublicPlay(seat, play);
        trick.Add(p);
        if (trick.Count > 1)
        {
            var led = root.Rules.LedSuit(trick, contract!);
            if (root.Rules.EffectiveSuit(play.Card, contract!) != led && !voids[seat].Contains(led))
                voids[seat].Add(led);
        }
        history.Add(p);
    }
    // All policy calls within a trick see the same completed cards. Publish one detached,
    // read-only snapshot per trick; never expose the branch's growing mutable list.
    private IReadOnlyList<Card> CompletedCards() => completedSnapshot ??= Array.AsReadOnly(completedCards.ToArray());
    private double Finish(int seat)
    {
        int active = contract!.IsMisere ? 3 : 4;
        var encodedBids = FlowPolicyCache.EncodeBids(auction);
        while (tricks.Sum() < 10)
        {
            if (trick.Count == active)
            {
                seat = root.Rules.Winner(trick, contract);
                tricks[seat]++;
                taken[seat].AddRange(trick.Select(p => p.Action.Card));
                foreach (var play in trick) completedCards.Add(play.Action.Card.ToBotCard());
                completedSnapshot = null;
                trick.Clear();
                if (contract.Kind == ContractKind.OpenMisere)
                    exposed = true;
                continue;
            }
            var legal = root.Rules.LegalPlays(hands[seat], trick, contract);
            PlayAction move;
            // Medium has no random stream or learning state. A sole complete action needs no adapter.
            if (legal.Count == 1) { move = legal[0]; work.ForcedMoves++; }
            else
            {
                move = continuation.PlayDirect(seat, hands, taken, encodedBids, tricks, contract, declarer,
                    exposed, trick, CompletedCards(), voids, legal);
                work.PolicyMoves++;
            }
            onSimulatedPlay?.Invoke(new(seat, move));
            AddPlay(seat, move);
            seat = NextActive(seat);
        }
        var (made, delta) = root.Rules.Score(contract, declarer, tricks);
        var scores = new[] { root.Scores[0] + delta[0], root.Scores[1] + delta[1] };
        int? winner = root.Rules.WinningTeam(scores, declarer % 2, made);
        int team = root.Seat % 2;
        // Terminal results dominate one-deal points; nonterminal choices maximize actual team point differential.
        return winner.HasValue ? (winner == team ? 1 : -1) * (4.0 * root.Rules.WinScore)
            : delta[team] - delta[1 - team];
    }
}
