using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

// This is a strategy-side forward model. The authoritative engine still validates real moves.
internal sealed class DefaultRollout
{
    private readonly Observation root;
    private readonly IPlayer continuation;
    private readonly Func<IPlayer> createContinuation;
    private readonly List<PlayingCard>[] hands;
    private readonly List<PlayingCard>[] taken;
    private readonly List<AuctionAction> auction;
    private readonly List<PublicPlay> history;
    private readonly List<PublicPlay> trick;
    private readonly int[] tricks;
    private readonly List<PlayingCard>[] knownDiscards = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
    private readonly PlayingCard[] dead;
    private Contract? contract;
    private int declarer;
    private bool exposed;
    internal DefaultRollout(Observation view, SampledDeal sample, Func<IPlayer> createContinuation)
    {
        this.createContinuation = createContinuation;
        continuation = createContinuation();
        root = view;
        hands = sample.Hands.Select(h => h.ToList()).ToArray();
        dead = sample.Dead.ToArray();
        taken = view.Players.Select(p => p.Taken.ToList()).ToArray();
        auction = view.Auction.ToList();
        history = view.History.ToList();
        trick = view.Trick.ToList();
        tricks = view.Players.Select(p => p.Tricks).ToArray();
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
        var bidder = createContinuation();
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
        history.Add(p);
    }
    private double Finish(int seat)
    {
        int active = contract!.IsMisere ? 3 : 4;
        while (tricks.Sum() < 10)
        {
            if (trick.Count == active)
            {
                seat = root.Rules.Winner(trick, contract);
                tricks[seat]++;
                taken[seat].AddRange(trick.Select(p => p.Action.Card));
                trick.Clear();
                if (contract.Kind == ContractKind.OpenMisere)
                    exposed = true;
                continue;
            }
            var view = View(seat);
            var legal = root.Rules.LegalPlays(hands[seat], trick, contract);
            AddPlay(seat, continuation.Play(view, legal));
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
