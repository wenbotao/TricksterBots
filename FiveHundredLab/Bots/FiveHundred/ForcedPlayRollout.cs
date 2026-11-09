using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

// Play-only forward model: skip observation/Medium adaptation for uniquely forced actions.
// The original rollout and authoritative engine remain unchanged.
internal sealed class ForcedPlayRollout
{
    private readonly Observation root;
    private readonly IPlayer continuation;
    private readonly PlayRolloutCounter work;
    private readonly Action<PublicPlay>? onSimulatedPlay;
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
    internal ForcedPlayRollout(Observation view, SampledDeal sample, Func<IPlayer> createContinuation, PlayRolloutCounter work, Action<PublicPlay>? onSimulatedPlay = null)
    {
        this.work = work;
        this.onSimulatedPlay = onSimulatedPlay;
        work.Evaluations++;
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
            // Legal actions are complete actions, including Joker lead declarations.
            // The fixed Medium continuation has no learning state or random stream to advance.
            var legal = root.Rules.LegalPlays(hands[seat], trick, contract);
            PlayAction move;
            if (legal.Count == 1) { move = legal[0]; work.ForcedMoves++; }
            else { move = continuation.Play(View(seat), legal); work.PolicyMoves++; }
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

// Per-root diagnostics; never shared across bot instances or used to choose an action.
internal sealed class PlayRolloutCounter
{
    public long Evaluations, ForcedMoves, PolicyMoves;
    public void Reset() { Evaluations = ForcedMoves = PolicyMoves = 0; }
}
