using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred.V11;

// One private model per candidate/world. No sampled hand is exposed to the root player.
internal sealed class VariantRollout
{
    private readonly Observation root;
    private readonly ConfiguredRules rules;
    private readonly VariantFlowData? data;
    private readonly MediumBot medium;
    private readonly List<PlayingCard>[] hands, taken, discarded;
    private readonly List<PublicPlay> history, trick;
    private readonly List<AuctionAction> auction;
    private readonly int[] tricks;
    private List<PlayingCard> kitty;
    private Contract? contract;
    private int declarer;
    private bool exposed;
    private int[] activeSeats=[];
    private PublicPlay[]? historySnapshot;
    private PublicPlay[] HistorySnapshot()=>data==null?history.ToArray():historySnapshot??=history.ToArray();
    private readonly Action<PublicPlay>? trace;
    private IReadOnlyList<AuctionAction>? auctionSnapshot;
    private IReadOnlyList<Contract?>[]? bids;
    internal VariantRollout(Observation root, ConfiguredRules source, SampledDeal sample, VariantFlowData? data = null, Action<PublicPlay>? trace = null)
    {
        this.trace=trace;this.root = root;rules = source.Detached();this.data = data;medium = new(1, rules);
        hands = sample.Hands.Select(h => h.ToList()).ToArray();kitty = sample.Dead.ToList();
        taken = root.Players.Select(p => p.Taken.ToList()).ToArray();tricks = root.Players.Select(p => p.Tricks).ToArray();
        discarded = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();discarded[root.Seat].AddRange(root.OwnDiscards);
        history = root.History.ToList();trick = root.Trick.ToList();auction = root.Auction.ToList();
        contract = root.Contract;declarer = root.Declarer ?? root.Dealer;if(contract!=null)activeSeats=rules.ActiveSeats(contract,declarer);exposed = root.Players[declarer].ExposedHand != null;
    }
    private Observation View(int seat)
    {
        // Flow reuses fixed auction-derived data and a single detached history snapshot per policy call.
        var h = HistorySnapshot();rules.History = h;rules.CurrentSeat = seat;rules.DeclarerSeat = declarer;
        auctionSnapshot ??= auction.ToArray();bids ??= Enumerable.Range(0, 4).Select(s => (IReadOnlyList<Contract?>)auction.Where(a => a.Seat == s).Select(a => a.Bid).ToArray()).ToArray();
        var players = Enumerable.Range(0, 4).Select(s => new PublicPlayer(s, hands[s].Count, root.Scores[s % 2], bids[s], tricks[s], taken[s].ToArray(), exposed && s == declarer ? hands[s].ToArray() : null)).ToArray();
        return new(seat, root.Dealer, root.Round, hands[seat].ToArray(), players, auctionSnapshot, contract, contract?.Kind == ContractKind.NoContractNT ? null : declarer,
            trick.ToArray(), h, root.Scores, rules, discarded[seat].ToArray());
    }
    private void ApplyBid(int seat, Contract? bid)
    { auction.Add(new(seat, bid));auctionSnapshot = null;bids = null;if (bid != null && bid.Kind != ContractKind.Inkle) { contract = bid;declarer = seat; } }
    internal double Bid(Contract? choice)
    {
        ApplyBid(root.Seat, choice);
        var passed = new bool[4];int consecutive = 0;
        foreach (var a in auction) { if (a.Bid == null) { passed[a.Seat] = true;consecutive++; } else if (a.Bid.Kind != ContractKind.Inkle) consecutive = 0; }
        var options = rules.Section("auction");
        bool Done() => ConfiguredRules.Text(options, "mode") == "single_round" ? auction.Count >= 4 : contract == null ? consecutive >= 4 : rules.PassIsPermanent ? Enumerable.Range(0, 4).All(s => s == declarer || passed[s]) : consecutive >= 3;
        int seat = (root.Seat + 1) % 4;
        while (!Done())
        {
            if (!rules.PassIsPermanent || !passed[seat])
            {
                rules.CurrentSeat = seat;var legal = rules.LegalBids(contract, auction);
                int position = (seat - root.Dealer + 3) % 4;
                int min = options.GetProperty("normalMinimumByPosition")[position].GetInt32();
                legal.RemoveAll(b => b?.Kind == ContractKind.Normal && b.Tricks < min);
                var inkle = options.GetProperty("inkle");
                if (contract == null && ConfiguredRules.Flag(inkle, "enabled") && inkle.GetProperty("allowedPositions").EnumerateArray().Any(p => p.GetInt32() == position))
                    legal.AddRange(inkle.GetProperty("strains").EnumerateArray().Select(s => new Contract(6, ConfiguredRules.SuitOf(s.GetString()!), ContractKind.Inkle)));
                if (contract == null && seat == root.Dealer && auction.Count == 3 && auction.All(a => a.Bid == null) && ConfiguredRules.Text(options.GetProperty("allPass"), "action") == "force_dealer") legal.Remove(null);
                var bid = medium.Bid(View(seat), legal);ApplyBid(seat, bid);
                if (bid == null) { passed[seat] = true;consecutive++; } else if (bid.Kind != ContractKind.Inkle) consecutive = 0;
            }
            seat = (seat + 1) % 4;
        }
        if (contract == null)
        {
            if (ConfiguredRules.Text(options.GetProperty("allPass"), "action") != "play_no_contract_nt") return 0;
            contract = new(0, Suit.Unknown, ContractKind.NoContractNT);declarer = root.Dealer;
            if (ConfiguredRules.Text(options.GetProperty("allPass"), "noContractKitty") == "dealer_takes") Exchange(declarer);
        }
        else
        {
            string mode = ConfiguredRules.Text(rules.ContractOptions(contract), "kitty");
            if(contract.Kind==ContractKind.Normal && rules.CanGrandSlam)
            {
                hands[declarer].AddRange(kitty);
                bool solo=medium.DeclareGrandSlam(View(declarer));
                if(solo)
                {
                    contract=new(10,contract.Trump,ContractKind.GrandSlam);int partner=(declarer+2)%4;
                    var gift=medium.TransferCard(View(partner),hands[partner]);hands[partner].Remove(gift);hands[declarer].Add(gift);
                }
                var cards=medium.Discard(View(declarer),kitty.Count+(solo?1:0)).ToArray();
                foreach(var card in cards)hands[declarer].Remove(card);discarded[declarer].AddRange(cards);kitty=cards.ToList();
            }
            else if (mode != "untouched") Exchange(declarer);
            if (mode == "sequential_team_exchange") { discarded[declarer].Clear();Exchange((declarer + 2) % 4); }
        }
        return StartPlay();
    }
    private void Exchange(int seat)
    {
        hands[seat].AddRange(kitty);var cards = medium.Discard(View(seat), kitty.Count).ToArray();
        foreach (var c in cards) if (!hands[seat].Remove(c)) throw new InvalidDataException("Rollout discard missing");
        discarded[seat].AddRange(cards);kitty = cards.ToList();
    }
    internal static double Discard(Observation root, ConfiguredRules rules, SampledDeal sample, PlayingCard[] choice, VariantFlowData? data = null)
    {
        var model = new VariantRollout(root, rules, sample, data);
        foreach (var c in choice) if (!model.hands[root.Seat].Remove(c)) throw new InvalidDataException("Rollout discard missing");
        model.discarded[root.Seat].AddRange(choice);
        if (root.Contract!.Kind == ContractKind.GrandNullo && root.Seat == model.declarer)
        { model.discarded[root.Seat].Clear();model.kitty = choice.ToList();model.Exchange((model.declarer + 2) % 4); }
        return model.StartPlay();
    }
    internal static double Play(Observation root, ConfiguredRules rules, SampledDeal sample, PlayAction choice, VariantFlowData? data = null, Action<PublicPlay>? trace = null)
    {
        var model = new VariantRollout(root, rules, sample, data,trace);data?.Apply(model.rules, root.Contract!);
        model.Add(root.Seat, choice);return model.Finish(model.Next(root.Seat));
    }
    private double StartPlay()
    {
        var options = rules.ContractOptions(contract!);activeSeats=rules.ActiveSeats(contract!,declarer);
        // The fixed Medium continuation declines optional nomination; Grand Slam is handled before discard.
        // Existing public declarations are retained; hidden opponents are never revealed.
        exposed = ConfiguredRules.Text(options.GetProperty("exposure"), "timing") == "before_first_lead";
        data?.Apply(rules, contract!);
        string lead = contract!.Kind == ContractKind.NoContractNT ? ConfiguredRules.Text(rules.Section("auction").GetProperty("allPass"), "noContractLead") : ConfiguredRules.Text(options, "firstLead");
        int seat = lead == "left_of_dealer" ? (root.Dealer + 1) % 4 : lead == "dealer" ? root.Dealer : lead == "left_of_declarer" ? (declarer + 1) % 4 : declarer;
        while (!activeSeats.Contains(seat)) seat = (seat + 1) % 4;
        return Finish(seat);
    }
    private int Next(int seat)
    { do { seat = (seat + 1) % 4; } while (!activeSeats.Contains(seat));return seat; }
    private void Add(int seat, PlayAction move)
    {
        if (!hands[seat].Remove(move.Card)) throw new InvalidDataException("Rollout card missing");
        var play = new PublicPlay(seat, move);trick.Add(play);history.Add(play);historySnapshot=null;trace?.Invoke(play);
    }
    private double Finish(int seat)
    {
        int active = activeSeats.Length;
        while (tricks.Sum() < 10)
        {
            if (trick.Count == active)
            {
                seat = rules.Winner(trick, contract!);tricks[seat]++;taken[seat].AddRange(trick.Select(p => p.Action.Card));trick.Clear();
                if (ConfiguredRules.Text(rules.ContractOptions(contract!).GetProperty("exposure"), "timing") == "after_first_trick") exposed = true;
                continue;
            }
            rules.CurrentSeat = seat;rules.DeclarerSeat = declarer;rules.History = HistorySnapshot();
            var legal = rules.LegalPlays(hands[seat], trick, contract!);
            // Complete actions include Joker lead declarations; no random/learning state is skipped.
            var move = legal.Count == 1 ? legal[0] : MediumBot.PlayConfiguredTactics(View(seat), rules, legal);
            Add(seat, move);seat = Next(seat);
        }
        return RuleBotEvaluation.Utility(root, rules, contract!, declarer, tricks);
    }
}
internal sealed class VariantFlowData(ConfiguredRules source)
{
    private readonly Dictionary<Contract, IReadOnlyDictionary<PlayingCard, (Suit Suit, int Rank)>> frames = new();
    internal void Apply(ConfiguredRules rules, Contract contract)
    {
        if (!frames.TryGetValue(contract, out var frame))
        {
            var uncached = source.Detached();uncached.PreparedCards = null;
            frame = new System.Collections.ObjectModel.ReadOnlyDictionary<PlayingCard, (Suit, int)>(uncached.Deck().ToDictionary(card => card, card => (uncached.EffectiveSuit(card, contract), uncached.Rank(card, contract))));
            frames.Add(contract, frame);
        }
        rules.PreparedContract = contract;rules.PreparedCards = frame;
    }
}
