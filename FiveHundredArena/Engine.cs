using Trickster.cloud;

namespace FiveHundredArena;

public sealed class Engine
{
    private readonly Rules template;
    public Engine(Rules rules)
    {
        template = rules;
    }
    public Engine() : this(RuleConfiguration.Load()) { }
    public static Engine FromConfiguration(string? path = null) => new(RuleConfiguration.Load(path));
    public GameRecord Run(IReadOnlyList<IPlayer> bots, int seed, int maxRounds = 200, int? initialDealer = null)
    {
        if (template is ConfiguredRules { IsV11: true } current)
            return new EngineV11(current).Run(bots, seed, maxRounds, initialDealer);
        var rules = template is ConfiguredRules config ? config with
        {
            History = [],
            Declarations = new()
        } : template;
        rules.Validate();
        if (bots.Count != 4 || maxRounds < 1 || initialDealer is < 0 or > 3)
            throw new ArgumentException("Four seats, positive round limit and dealer 0..3 required");
        var deck = rules.Deck();
        var configured = rules as ConfiguredRules;
        int kittyCount = configured == null ? 3 : ConfiguredRules.Number(configured.Section("deal"), "kittySize");
        if (deck.Count != 40 + kittyCount || deck.Distinct().Count() != deck.Count)
            throw new ArgumentException("This engine requires a unique 43-card deck (four hands of ten plus kitty of three)");
        int dealerStart = initialDealer ?? (configured == null ? 3 : ConfiguredRules.Text(configured.Section("deal"), "firstDealer") == "random" ? new SeededRandom(seed).Next(4) : ConfiguredRules.Number(configured.Section("deal"), "fixedDealer"));
        var result = new GameRecord { Seed = seed, Rules = rules, Players = bots.Select(b => b.Name).ToArray(), MaxRounds = maxRounds, InitialDealer = dealerStart };
        var scores = new int[2];
        try
        {
            int nextDealer = dealerStart;
            for (int round = 0; round < maxRounds; round++)
            {
                if (configured != null)
                {
                    configured.Declarations = new();
                    configured.History = [];
                }
                int dealer = nextDealer;
                nextDealer = (dealer + 1) % 4;
                var shuffled = deck.ToList();
                new SeededRandom(unchecked(seed + round * 1000003)).Shuffle(shuffled);
                var hands = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
                // Round-robin ten cards per seat, beginning left of dealer.
                PlayingCard[] kitty;
                if (configured != null && ConfiguredRules.Text(configured.Section("deal"), "method") == "packets")
                {
                    int index = 0;
                    var middle = new List<PlayingCard>();
                    foreach (var packet in configured.Section("deal").GetProperty("packets").EnumerateArray())
                    {
                        for (int off = 0; off < 4; off++)
                            for (int n = 0; n < ConfiguredRules.Number(packet, "cardsEach"); n++)
                                hands[(dealer + 1 + off) % 4].Add(shuffled[index++]);
                        for (int n = 0; n < ConfiguredRules.Number(packet, "kittyCards"); n++)
                            middle.Add(shuffled[index++]);
                    }
                    kitty = middle.ToArray();
                }
                else
                {
                    for (int i = 0; i < 40; i++)
                        hands[(dealer + 1 + i) % 4].Add(shuffled[i]);
                    kitty = shuffled.Skip(40).ToArray();
                }
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
                        declarer < 0 ? null : declarer, trick.ToArray(), history.ToArray(), scores.ToArray(), rules with
                        {
                        }, seat == declarer ? discard.ToArray() : []);
                }
                // Auction state follows the configured pass/reentry and single-round rules.
                var passed = new bool[4];
                int seat = (dealer + 1) % 4, consecutivePasses = 0;
                while (true)
                {
                    if (rules.PassIsPermanent && passed[seat])
                    {
                        seat = (seat + 1) % 4;
                        continue;
                    }
                    var legal = rules.LegalBids(high, auction);
                    if (configured != null)
                    {
                        var options = configured.Section("auction");
                        int position = (seat - dealer + 3) % 4;
                        int min = options.GetProperty("normalMinimumByPosition")[position].GetInt32();
                        legal = legal.Where(b => b == null || b.Kind != ContractKind.Normal || b.Tricks >= min).ToList();
                        var inkle = options.GetProperty("inkle");
                        if (high == null && ConfiguredRules.Flag(inkle, "enabled") && inkle.GetProperty("allowedPositions").EnumerateArray().Any(x => x.GetInt32() == position))
                            legal.AddRange(inkle.GetProperty("strains").EnumerateArray().Select(x => new Contract(6, ConfiguredRules.SuitOf(x.GetString()!), ContractKind.Inkle)));
                    }
                    Contract? bid;
                    try
                    {
                        bid = bots[seat].Bid(View(seat), legal.ToArray());
                    }
                    catch (Exception ex) { throw new IllegalActionException($"Round {round}, seat {seat}, bid failed: {ex.Message}", ex); }
                    if (!legal.Contains(bid))
                        throw new IllegalActionException($"Round {round}, seat {seat}: illegal bid {bid}");
                    auction.Add(new(seat, bid));
                    record.Decisions.Add(new(seat, "bid", Bid: bid));
                    if (bid == null)
                    {
                        passed[seat] = true;
                        consecutivePasses++;
                    }
                    else if (bid.Kind != ContractKind.Inkle)
                    {
                        high = bid;
                        declarer = seat;
                        consecutivePasses = 0;
                    }
                    if (configured != null && ConfiguredRules.Text(configured.Section("auction"), "mode") == "single_round" && auction.Count == 4)
                        break;
                    if (high == null && consecutivePasses == 4)
                        break;
                    if (high != null && (rules.PassIsPermanent ? Enumerable.Range(0, 4).All(s => s == declarer || passed[s]) : consecutivePasses == 3))
                        break;
                    seat = (seat + 1) % 4;
                }
                if (high == null)
                {
                    if (configured != null && ConfiguredRules.Text(configured.Section("auction").GetProperty("allPass"), "dealerOnRedeal") == "same")
                        nextDealer = dealer;
                    record.Redeal = true;
                    record.ScoreDelta = [0, 0];
                    record.Scores = scores.ToArray();
                    record.Tricks = tricks;
                    continue;
                }
                record.Contract = high;
                record.Declarer = declarer;
                bool takeKitty = configured == null || ConfiguredRules.Text(configured.ContractOptions(high), "kitty") == "take_and_discard";
                if (takeKitty)
                    hands[declarer].AddRange(kitty);
                try
                {
                    discard = bots[declarer].Discard(View(declarer), takeKitty ? kittyCount : 0).ToArray();
                }
                catch (Exception ex) { throw new IllegalActionException($"Round {round}, seat {declarer}, discard failed: {ex.Message}", ex); }
                if (discard.Length != (takeKitty ? kittyCount : 0) || discard.Distinct().Count() != (takeKitty ? kittyCount : 0) || discard.Any(c => !hands[declarer].Contains(c)))
                    throw new IllegalActionException($"Round {round}, seat {declarer}: must discard three distinct cards from hand");
                foreach (var card in discard)
                    hands[declarer].Remove(card);
                record.Decisions.Add(new(declarer, "discard", Discards: discard));
                if (configured != null)
                {
                    configured.Declarations = new();
                    if (ConfiguredRules.Text(configured.ContractOptions(high).GetProperty("joker"), "predeclare") == "declarer_optional" && (high.IsMisere || high.Trump == Trickster.cloud.Suit.Unknown) && bots[declarer] is IJokerDeclarationPlayer naming)
                        foreach (var declaration in naming.DeclareJokers(View(declarer)))
                        {
                            if (!declaration.Card.IsJoker || !hands[declarer].Contains(declaration.Card) || !Rules.Suits.Contains(declaration.JokerLeadSuit) || !configured.Declarations.TryAdd(declaration.Card, declaration.JokerLeadSuit))
                                throw new IllegalActionException("Invalid Joker declaration");
                            record.Decisions.Add(new(declarer, "declare", Play: declaration));
                        }
                }
                // Misere participation is a contract rule, not an assumption about seat count.
                var active = Enumerable.Range(0, 4).Where(s => (configured == null ? !high.IsMisere : ConfiguredRules.Text(configured.ContractOptions(high), "activeSeats") == "all_four") || s != (declarer + 2) % 4).ToArray();
                int leader = declarer;
                if (configured != null)
                {
                    var o = configured.ContractOptions(high);
                    leader = ConfiguredRules.Text(o, "firstLead") switch
                    {
                        "left_of_declarer" => (declarer + 1) % 4,
                        "left_of_dealer" => (dealer + 1) % 4,
                        _ => declarer
                    };
                    while (!active.Contains(leader))
                        leader = (leader + 1) % 4;
                    exposed = ConfiguredRules.Text(o.GetProperty("exposure"), "timing") == "before_first_lead";
                }
                for (int t = 0; t < 10; t++)
                {
                    trick.Clear();
                    for (int offset = 0; offset < 4; offset++)
                    {
                        seat = (leader + offset) % 4;
                        if (!active.Contains(seat))
                            continue;
                        if (configured != null)
                        {
                            configured.History = history.ToArray();
                            configured.CurrentSeat = seat;
                            configured.DeclarerSeat = declarer;
                        }
                        var legal = rules.LegalPlays(hands[seat], trick, high);
                        PlayAction play;
                        try
                        {
                            play = bots[seat].Play(View(seat), legal.ToArray());
                        }
                        catch (Exception ex) { throw new IllegalActionException($"Round {round}, trick {t}, seat {seat}, play failed: {ex.Message}", ex); }
                        if (!legal.Contains(play))
                            throw new IllegalActionException($"Round {round}, trick {t}, seat {seat}: illegal play {play}");
                        hands[seat].Remove(play.Card);
                        var publicPlay = new PublicPlay(seat, play);
                        trick.Add(publicPlay);
                        history.Add(publicPlay);
                        record.Decisions.Add(new(seat, "play", Play: play));
                    }
                    leader = rules.Winner(trick, high);
                    tricks[leader]++;
                    taken[leader].AddRange(trick.Select(p => p.Action.Card));
                    record.TrickWinners.Add(leader);
                    if (configured == null ? high.Kind == ContractKind.OpenMisere : ConfiguredRules.Text(configured.ContractOptions(high).GetProperty("exposure"), "timing") == "after_first_trick")
                        exposed = true;
                }
                // Score the complete deal, cap only new defensive income, then check game end.
                var (made, delta) = rules.Score(high, declarer, tricks);
                if (configured != null)
                {
                    var cap = configured.Section("scoring").GetProperty("defensiveScoreCap");
                    if (cap.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        int def = 1 - declarer % 2;
                        delta[def] = Math.Min(delta[def], Math.Max(0, cap.GetInt32() - scores[def]));
                    }
                }
                for (int i = 0; i < 2; i++)
                    scores[i] = checked(scores[i] + delta[i]);
                record.Made = made;
                record.Tricks = tricks;
                record.ScoreDelta = delta;
                record.Scores = scores.ToArray();
                result.Winner = rules.WinningTeam(scores, declarer % 2, made);
                if (result.Winner.HasValue)
                    return result;
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
    {
        var cards = view.Hand.ToList();
        random.Shuffle(cards);
        return cards.Take(count).ToArray();
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal) => legal[random.Next(legal.Count)];
}

public sealed class ReplayPlayer(string name, IEnumerable<Decision> decisions) : IPlayer, IJokerDeclarationPlayer, IRuleVariationPlayer
{
    private readonly Queue<Decision> queue = new(decisions);
    public string Name => name;
    private Decision Next(string phase)
    {
        if (!queue.TryDequeue(out var d) || d.Phase != phase)
            throw new InvalidDataException("Replay decision sequence mismatch");
        return d;
    }
    public bool Complete => queue.Count == 0;
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view)
    {
        var result = new List<PlayAction>();
        while (queue.TryPeek(out var d) && d.Phase == "declare")
            result.Add(queue.Dequeue().Play!);
        return result;
    }
    public bool RequestRedeal(Observation view) => Next("redeal-request").Accepted ?? false;
    public bool DeclareGrandSlam(Observation view) => Next("grand-slam").Accepted ?? false;
    public PlayingCard TransferCard(Observation view, IReadOnlyList<PlayingCard> legal) => Next("transfer").Play!.Card;
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => Next("bid").Bid;
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count) => Next("discard").Discards ?? throw new InvalidDataException("Missing discards");
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal) => Next("play").Play ?? throw new InvalidDataException("Missing play");
}
