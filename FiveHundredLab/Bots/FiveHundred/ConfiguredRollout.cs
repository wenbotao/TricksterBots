using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

// Each rollout has detached hands and its own rule context; root strategies never see sampled opponents.
internal sealed class ConfiguredRollout
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
    private ConfiguredRollout(Observation view, ConfiguredRules r, SampledDeal world)
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
        var model = new ConfiguredRollout(view, r, world);
        model.Add(view.Seat, move);
        return model.Finish(model.Next(view.Seat));
    }
    internal static double Discard(Observation view, ConfiguredRules r, SampledDeal world, PlayingCard[] move)
    {
        var model = new ConfiguredRollout(view, r, world);
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
            Add(seat, MediumBot.PlayConfiguredTactics(view, rules, legal));
            seat = Next(seat);
        }
        return RuleBotEvaluation.Utility(root, rules, contract, declarer, tricks);
    }
    // Match the engine: apply defensive caps before checking terminal scores.

}
