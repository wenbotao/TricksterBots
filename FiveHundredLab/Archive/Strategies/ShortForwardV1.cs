using FiveHundredArena;
namespace FiveHundredLab;

// No policy is given more than its own hand and the public state of a hypothetical deal.
public sealed class ShortForwardV1
{
    private readonly Observation root;
    private readonly List<PlayingCard>[] hands;
    private readonly List<PlayingCard>[] taken;
    private readonly List<PublicPlay> history;
    private readonly List<PublicPlay> trick;
    private readonly int[] tricks;
    private readonly List<PlayingCard>[] discards = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
    private readonly IPlayer[] policies;
    private readonly CardTableV1 table;
    private readonly int horizon, startTricks, declarer;
    private readonly ValueModelV1? model;
    private bool exposed;
    public ShortForwardV1(Observation v, SampledDeal sample, int horizon, ValueModelV1? model, int seed, OpponentPolicyV1 opponent)
    {
        if (horizon < 1 || v.Contract == null || v.Declarer == null) throw new ArgumentException("Post-auction observation and positive horizon required");
        root = v; this.horizon = horizon; this.model = model; declarer = v.Declarer.Value;
        hands = sample.Hands.Select(h => h.ToList()).ToArray(); taken = v.Players.Select(p => p.Taken.ToList()).ToArray();
        history = v.History.ToList(); trick = v.Trick.ToList(); tricks = v.Players.Select(p => p.Tricks).ToArray(); startTricks = tricks.Sum();
        table = CardTableV1.Get(v.Rules, v.Contract);
        policies = Enumerable.Range(0, 4).Select(s => s % 2 == v.Seat % 2 ? (IPlayer)new TacticianV1() : ContinuationPoolV1.Create(opponent, unchecked(seed + s * 101))).ToArray();
        discards[v.Seat].AddRange(v.OwnDiscards);
        if (v.Seat != declarer && v.Hand.Count <= 10) discards[declarer].AddRange(sample.Dead);
        exposed = v.Contract.Kind == ContractKind.OpenMisere && history.Count - trick.Count >= 3;
    }
    private Observation View(int seat)
    {
        var players = Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, hands[s].Count, root.Scores[s % 2], root.Players[s].Bids,
            tricks[s], taken[s].ToArray(), exposed && s == declarer ? hands[s].ToArray() : null)).ToArray();
        return new(seat, root.Dealer, root.Round, hands[seat].ToArray(), players, root.Auction, root.Contract, declarer,
            trick.ToArray(), history.ToArray(), root.Scores, root.Rules, discards[seat].ToArray());
    }
    public RolloutValueV1 Discard(IReadOnlyList<PlayingCard> choice)
    {
        if (choice.Distinct().Count() != choice.Count || choice.Any(c => !hands[root.Seat].Contains(c))) throw new InvalidDataException("Invalid simulated discard");
        foreach (var c in choice) hands[root.Seat].Remove(c);
        discards[root.Seat].AddRange(choice); return Finish(declarer);
    }
    public RolloutValueV1 Play(PlayAction choice) { Add(root.Seat, choice); return Finish(Next(root.Seat)); }
    private int Next(int seat) { do seat = (seat + 1) % 4; while (root.Contract!.IsMisere && seat == (declarer + 2) % 4); return seat; }
    private void Add(int seat, PlayAction p)
    {
        if (!root.Rules.LegalPlays(hands[seat], trick, root.Contract!).Contains(p) || !hands[seat].Remove(p.Card)) throw new InvalidDataException("Illegal simulated play");
        var play = new PublicPlay(seat, p); trick.Add(play); history.Add(play);
    }
    private RolloutValueV1 Finish(int seat)
    {
        int active = root.Contract!.IsMisere ? 3 : 4;
        while (tricks.Sum() < 10)
        {
            if (trick.Count == active)
            {
                seat = table.Winner(trick, root.Contract); tricks[seat]++; taken[seat].AddRange(trick.Select(p => p.Action.Card)); trick.Clear();
                if (root.Contract.Kind == ContractKind.OpenMisere) exposed = true;
                int completed = tricks.Sum();
                if (completed < 10 && completed - startTricks >= horizon && 10 - completed > 3)
                {
                    var leaf = LeafValueV1.Estimate(View(seat), model);
                    return leaf with { Utility = leaf.Utility * (seat % 2 == root.Seat % 2 ? 1 : -1) };
                }
                continue;
            }
            var v = View(seat); var legal = root.Rules.LegalPlays(hands[seat], trick, root.Contract);
            Add(seat, policies[seat].Play(v, legal)); seat = Next(seat);
        }
        return LeafValueV1.Score(root, tricks);
    }
}
