using Trickster.cloud;

namespace FiveHundredArena;

public sealed class Engine(Rules rules)
{
    public GameRecord Run(IReadOnlyList<IPlayer> bots, int seed, int maxRounds = 200, int initialDealer = 3)
    {
        rules.Validate();
        if (bots.Count != 4 || maxRounds < 1 || initialDealer is < 0 or > 3) throw new ArgumentException("Four seats, positive round limit and dealer 0..3 required");
        var deck = rules.Deck();
        if (deck.Count != 43 || deck.Distinct().Count() != 43) throw new ArgumentException("This engine requires a unique 43-card deck (four hands of ten plus kitty of three)");
        var result = new GameRecord { Seed = seed, Rules = rules, Players = bots.Select(b => b.Name).ToArray(), MaxRounds = maxRounds, InitialDealer = initialDealer };
        var scores = new int[2];
        try
        {
            for (int round = 0; round < maxRounds; round++)
            {
                int dealer = (initialDealer + round) % 4;
                var shuffled = deck.ToList();
                new SeededRandom(unchecked(seed + round * 1000003)).Shuffle(shuffled);
                var hands = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
                // Round-robin ten cards per seat, beginning left of dealer.
                for (int i = 0; i < 40; i++) hands[(dealer + 1 + i) % 4].Add(shuffled[i]);
                var kitty = shuffled.Skip(40).ToArray();
                var record = new RoundRecord { Number = round, Dealer = dealer, Hands = hands.Select(h => h.ToArray()).ToArray(), Kitty = kitty };
                result.Rounds.Add(record);
                var auction = new List<AuctionAction>();
                var history = new List<PublicPlay>();
                var trick = new List<PublicPlay>();
                var taken = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
                var tricks = new int[4];
                Contract? high = null;
                int declarer = -1;
                bool exposed = false;
                PlayingCard[] discard = [];
                Observation View(int seat)
                {
                    var players = Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, hands[s].Count, scores[s % 2],
                        auction.Where(a => a.Seat == s).Select(a => a.Bid).ToArray(), tricks[s], taken[s].ToArray(),
                        exposed && s == declarer ? hands[s].ToArray() : null)).ToArray();
                    // Even a malicious in-process player modifying its lists cannot change engine state.
                    return new(seat, dealer, round, hands[seat].ToArray(), players, auction.ToArray(), high,
                        declarer < 0 ? null : declarer, trick.ToArray(), history.ToArray(), scores.ToArray(), rules with { }, seat == declarer ? discard.ToArray() : []);
                }
                var passed = new bool[4];
                int seat = (dealer + 1) % 4, consecutivePasses = 0;
                while (true)
                {
                    if (rules.PassIsPermanent && passed[seat]) { seat = (seat + 1) % 4; continue; }
                    var legal = rules.LegalBids(high, auction);
                    Contract? bid;
                    try { bid = bots[seat].Bid(View(seat), legal.ToArray()); }
                    catch (Exception ex) { throw new IllegalActionException($"Round {round}, seat {seat}, bid failed: {ex.Message}", ex); }
                    if (!legal.Contains(bid)) throw new IllegalActionException($"Round {round}, seat {seat}: illegal bid {bid}");
                    auction.Add(new(seat, bid)); record.Decisions.Add(new(seat, "bid", Bid: bid));
                    if (bid == null) { passed[seat] = true; consecutivePasses++; }
                    else { high = bid; declarer = seat; consecutivePasses = 0; }
                    if (high == null && consecutivePasses == 4) break;
                    if (high != null && (rules.PassIsPermanent ? Enumerable.Range(0, 4).All(s => s == declarer || passed[s]) : consecutivePasses == 3)) break;
                    seat = (seat + 1) % 4;
                }
                if (high == null)
                {
                    record.Redeal = true; record.ScoreDelta = [0, 0]; record.Scores = scores.ToArray(); record.Tricks = tricks;
                    continue;
                }
                record.Contract = high; record.Declarer = declarer;
                hands[declarer].AddRange(kitty);
                try { discard = bots[declarer].Discard(View(declarer), 3).ToArray(); }
                catch (Exception ex) { throw new IllegalActionException($"Round {round}, seat {declarer}, discard failed: {ex.Message}", ex); }
                if (discard.Length != 3 || discard.Distinct().Count() != 3 || discard.Any(c => !hands[declarer].Contains(c)))
                    throw new IllegalActionException($"Round {round}, seat {declarer}: must discard three distinct cards from hand");
                foreach (var card in discard) hands[declarer].Remove(card);
                record.Decisions.Add(new(declarer, "discard", Discards: discard));
                var active = Enumerable.Range(0, 4).Where(s => !high.IsMisere || s != (declarer + 2) % 4).ToArray();
                int leader = declarer;
                for (int t = 0; t < 10; t++)
                {
                    trick.Clear();
                    for (int offset = 0; offset < 4; offset++)
                    {
                        seat = (leader + offset) % 4;
                        if (!active.Contains(seat)) continue;
                        var legal = rules.LegalPlays(hands[seat], trick, high);
                        PlayAction play;
                        try { play = bots[seat].Play(View(seat), legal.ToArray()); }
                        catch (Exception ex) { throw new IllegalActionException($"Round {round}, trick {t}, seat {seat}, play failed: {ex.Message}", ex); }
                        if (!legal.Contains(play)) throw new IllegalActionException($"Round {round}, trick {t}, seat {seat}: illegal play {play}");
                        hands[seat].Remove(play.Card);
                        var publicPlay = new PublicPlay(seat, play);
                        trick.Add(publicPlay); history.Add(publicPlay);
                        record.Decisions.Add(new(seat, "play", Play: play));
                    }
                    leader = rules.Winner(trick, high); tricks[leader]++;
                    taken[leader].AddRange(trick.Select(p => p.Action.Card));
                    record.TrickWinners.Add(leader);
                    if (high.Kind == ContractKind.OpenMisere) exposed = true;
                }
                var (made, delta) = rules.Score(high, declarer, tricks);
                for (int i = 0; i < 2; i++) scores[i] = checked(scores[i] + delta[i]);
                record.Made = made; record.Tricks = tricks; record.ScoreDelta = delta; record.Scores = scores.ToArray();
                result.Winner = rules.WinningTeam(scores, declarer % 2, made);
                if (result.Winner.HasValue) return result;
            }
        }
        catch (Exception ex) { ex.Data["record"] = result; throw; }
        result.Truncated = true;
        return result;
    }
}

public sealed class RandomPlayer(string name, int seed) : IPlayer
{
    private readonly SeededRandom random = new(seed);
    public string Name => name;
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => random.Next(3) != 0 ? null : legal[random.Next(legal.Count)];
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    { var cards = view.Hand.ToList(); random.Shuffle(cards); return cards.Take(count).ToArray(); }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal) => legal[random.Next(legal.Count)];
}

public sealed class ReplayPlayer(string name, IEnumerable<Decision> decisions) : IPlayer
{
    private readonly Queue<Decision> queue = new(decisions);
    public string Name => name;
    private Decision Next(string phase)
    { if (!queue.TryDequeue(out var d) || d.Phase != phase) throw new InvalidDataException("Replay decision sequence mismatch"); return d; }
    public bool Complete => queue.Count == 0;
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => Next("bid").Bid;
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count) => Next("discard").Discards ?? throw new InvalidDataException("Missing discards");
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal) => Next("play").Play ?? throw new InvalidDataException("Missing play");
}
