using System.Text.Json;
using Trickster.cloud;
namespace FiveHundredArena;

// A detached resolved configuration travels with saved games; no preset lookup during replay.
public sealed record ConfiguredRules : Rules
{
    public required string Document
    {
        get; init;
    }
    private sealed record ParsedDocument(JsonElement Root);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, ParsedDocument> Documents = new();
    internal JsonElement Root => Documents.GetValue(Document, text => new(JsonSerializer.Deserialize<JsonElement>(text))).Root;
    internal JsonElement Section(string name) => Root.GetProperty(name);
    internal JsonElement ContractOptions(Contract c) => Section("contracts").GetProperty(c.Kind switch { ContractKind.Misere => "misere", ContractKind.OpenMisere => "open_misere", _ => "normal" });
    internal static string Text(JsonElement e, string key) => e.GetProperty(key).GetString()!;
    internal static bool Flag(JsonElement e, string key) => e.GetProperty(key).GetBoolean();
    internal static int Number(JsonElement e, string key) => e.GetProperty(key).GetInt32();
    internal static Suit SuitOf(string s) => s switch { "S" => Suit.Spades, "C" => Suit.Clubs, "D" => Suit.Diamonds, "H" => Suit.Hearts, _ => Suit.Unknown };
    internal static string Symbol(Suit s) => s switch { Suit.Spades => "S", Suit.Clubs => "C", Suit.Diamonds => "D", Suit.Hearts => "H", _ => "NT" };
    internal static string Key(Contract c) => c.Kind switch { ContractKind.Misere => "misere", ContractKind.OpenMisere => "open_misere", _ => c.Tricks + Symbol(c.Trump) };
    internal static Contract Parse(string s) => s switch { "misere" => new(0, Suit.Unknown, ContractKind.Misere), "open_misere" => new(0, Suit.Unknown, ContractKind.OpenMisere), _ => new(int.Parse(s[..(s.Length - (s.EndsWith("NT") ? 2 : 1))]), SuitOf(s.EndsWith("NT") ? "NT" : s[^1..])) };
    public override List<PlayingCard> Deck() => Section("deck").GetProperty("cards").EnumerateArray().Select(x => { var s = x.GetString()!; return s.StartsWith('X') ? new PlayingCard(Suit.Joker, 16 + int.Parse(s[1..]) - 1) : PlayingCard.FromBotCard(new Card(s.Replace("10", "T"))); }).ToList();
    public override void Validate()
    {
        var r = Root;
        ConfigurationStructure.Validate(r);
        if (Number(r, "schemaVersion") != 1 || Number(Section("table"), "players") != 4)
            throw new ArgumentException("Unsupported configuration version or player count");
        var d = Section("deal");
        if (Number(d, "handSize") != 10 || Deck().Count != 40 + Number(d, "kittySize") || Deck().Distinct().Count() != Deck().Count)
            throw new ArgumentException("C01: invalid deck/deal conservation");
        var jokers = Section("deck").GetProperty("jokerOrderHighToLow").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        if (!jokers.SetEquals(Deck().Where(c => c.IsJoker).Select(c => "X" + (c.Rank - 15))))
            throw new ArgumentException("C02: Joker identities/order mismatch");
        var a = Section("auction");
        var expected = new HashSet<string>();
        foreach (var n in Enumerable.Range(6, 5))
            foreach (var s in a.GetProperty("strains").EnumerateArray().Select(x => x.GetString()!))
                if (n >= a.GetProperty("normalMinimumByPosition").EnumerateArray().Min(x => x.GetInt32()) && n <= Number(a, "normalMaximum"))
                    expected.Add(n + s);
        foreach (var k in new[] { "misere", "open_misere" })
            if (Flag(Section("contracts").GetProperty(k), "enabled"))
                expected.Add(k);
        var order = a.GetProperty("orderLowToHigh").EnumerateArray().Select(x => x.GetString()!).ToArray();
        if (order.Distinct().Count() != order.Length || !expected.SetEquals(order))
            throw new ArgumentException("C04: contract order must cover enabled contracts exactly");

        if (!Flag(Section("contracts").GetProperty("normal"), "enabled"))
            throw new ArgumentException("Normal contracts must be enabled");
        if (a.GetProperty("normalMinimumByPosition").EnumerateArray().Any(x => x.GetInt32() > Number(a, "normalMaximum")))
            throw new ArgumentException("C08: unreachable minimum level");
        if (Text(a, "mode") == "multi_round" && Text(a, "finish") != (Text(a, "passReentry") == "never" ? "all_other_seats_withdrawn" : "three_consecutive_passes"))
            throw new ArgumentException("C07: incompatible auction ending");
        if (Flag(a, "selfRaiseReopens"))
            throw new ArgumentException("Cannot reopen without self raise");
        if (Text(d, "method") == "round_robin" && (d.GetProperty("packets").GetArrayLength() != 0 || Text(d, "kittyPosition") != "after_hands") || Text(d, "method") == "packets" && Text(d, "kittyPosition") != "interleaved_packets")
            throw new ArgumentException("C03: packet position mismatch");
        if ((Text(d, "rotation") == "every_deal") != (Text(a.GetProperty("allPass"), "dealerOnRedeal") == "advance"))
            throw new ArgumentException("C21: redeal rotation conflict");
        if (Flag(a.GetProperty("inkle"), "enabled") && (Text(a, "mode") != "single_round" || a.GetProperty("inkle").GetProperty("allowedPositions").GetArrayLength() == 0))
            throw new ArgumentException("C09: Inkle needs single-round positions");
        foreach (var k in new[] { "normal", "misere", "open_misere" })
        {
            var c = Section("contracts").GetProperty(k);
            var eligibility = c.GetProperty("eligibility").GetProperty("minimumPriorNormalLevel");
            if (Flag(c, "enabled") && eligibility.ValueKind != JsonValueKind.Null && (eligibility.GetInt32() > Number(a, "normalMaximum") || Text(a, "mode") == "single_round" && Text(c.GetProperty("eligibility"), "scope") == "self"))
                throw new ArgumentException("C14: unreachable eligibility");
            if (Text(c.GetProperty("exposure"), "timing") == "never" && Flag(c.GetProperty("exposure"), "includeDiscards"))
                throw new ArgumentException("C15: no exposure event for discards");
            if (Flag(c, "enabled") && !Deck().Any(x => x.IsJoker) && (Text(c.GetProperty("joker"), "predeclare") != "none" || Flag(c.GetProperty("joker"), "forceWhenVoid")))
                throw new ArgumentException("C13: Joker behavior without Jokers");
            if (!Flag(c.GetProperty("joker"), "leadDeclarationRequired"))
                throw new NotSupportedException("Unnamed Joker lead without suit is not implemented");
            if (k != "normal" && Text(c, "target") != "declarer_zero" && Flag(c, "enabled"))
                throw new NotSupportedException("Team zero target not implemented");
            if (k == "normal" && (Text(c, "target") != "team_at_least" || Text(c, "activeSeats") != "all_four" || eligibility.ValueKind != JsonValueKind.Null))
                throw new ArgumentException("C12: invalid normal contract");
        }
        // Reject unsupported branches rather than accepting a rule and silently playing another game.
        foreach (var k in new[] { "double_nullo", "grand_nullo" })
            if (Flag(Section("contracts").GetProperty(k), "enabled"))
                throw new NotSupportedException("Team-zero contracts are not implemented");
        if (Text(a, "selfRaise") != "forbidden" || Text(a, "passReentry") == "on_contract_change" || Text(a.GetProperty("allPass"), "action") != "redeal" || Text(d.GetProperty("redealRequest"), "mode") != "none")
            throw new NotSupportedException("Self-raise, reopen, forced/no-contract hands and redeal requests are not implemented");
        if (Text(a, "mode") == "single_round" && Text(a, "finish") != "four_opportunities")
            throw new ArgumentException("C05: single round requires four opportunities");
        foreach (var k in new[] { "normal", "misere", "open_misere" })
        {
            var c = Section("contracts").GetProperty(k);
            if (Flag(c.GetProperty("exposure"), "includeDiscards") || Text(c.GetProperty("exposure"), "hands") != "declarer" || Text(c.GetProperty("eligibility"), "scope") != "any_player")
                throw new NotSupportedException("Team/discard exposure and self-only eligibility not implemented");
            if (Text(c, "control") != "individual" || Text(c, "partnerVisibility") != "hidden")
                throw new NotSupportedException("Two-hand control/private partner hands are not implemented");
        }
        if (!Flag(Section("match"), "lossPrecedesWin") || Text(Section("match"), "simultaneousWinners") != "contracting_team")
            throw new NotSupportedException("Alternative terminal priority not implemented");
        if (Text(d, "method") == "packets" && (d.GetProperty("packets").EnumerateArray().Sum(x => Number(x, "cardsEach")) != 10 || d.GetProperty("packets").EnumerateArray().Sum(x => Number(x, "kittyCards")) != Number(d, "kittySize")))
            throw new ArgumentException("C03: invalid packet totals");
        if (Text(a, "mode") == "single_round" && Text(a, "passReentry") != "never")
            throw new ArgumentException("Single round cannot reenter");
        if (Text(Section("match"), "mode") != "score_threshold" || Text(Section("play"), "finish") != "play_all_tricks")
            throw new NotSupportedException("Fixed-length matches and early completion are not implemented");
    }
    internal IReadOnlyList<PublicPlay> History = [];
    internal Dictionary<PlayingCard, Suit> Declarations = [];
    // Effective-suit following, forced trumping and Joker restrictions are independent constraints.
    public override List<PlayAction> LegalPlays(IReadOnlyList<PlayingCard> hand, IReadOnlyList<PublicPlay> trick, Contract c)
    {
        var p = Section("play");
        var cards = hand.AsEnumerable();
        var j = ContractOptions(c).GetProperty("joker");
        bool special = c.IsMisere || c.Trump == Suit.Unknown;
        if (trick.Count > 0)
        {
            var led = LedSuit(trick, c);
            var follow = hand.Where(x => EffectiveSuit(x, c) == led).ToArray();
            if (Flag(p, "mustFollowEffectiveSuit") && follow.Length > 0)
                cards = follow;
            if (special)
            {
                bool first = !History.Take(Math.Max(0, History.Count - trick.Count)).Where((x, i) => i % (Text(ContractOptions(c), "activeSeats") == "all_four" ? 4 : 3) == 0).Any(x => LedSuit([x], c) == led);
                string mode = Text(j, "follow");
                if (mode == "anytime" || mode == "first_lead_of_suit_or_void" && first)
                    cards = cards.Concat(hand.Where(x => x.IsJoker && !Declarations.ContainsKey(x))).Distinct();
                if (follow.Length == 0 && Flag(j, "forceWhenVoid") && hand.Any(x => x.IsJoker && !Declarations.ContainsKey(x)))
                    cards = hand.Where(x => x.IsJoker && !Declarations.ContainsKey(x));
            }
            if (!special && follow.Length == 0 && Flag(p, "mustTrumpWhenVoid") && hand.Any(x => EffectiveSuit(x, c) == c.Trump))
                cards = cards.Where(x => EffectiveSuit(x, c) == c.Trump);
            if (!special && Flag(p, "mustOvertrump") && trick.Any(x => EffectiveSuit(x.Action.Card, c) == c.Trump))
            {
                int top = trick.Where(x => EffectiveSuit(x.Action.Card, c) == c.Trump).Max(x => Rank(x.Action.Card, c));
                var higher = cards.Where(x => EffectiveSuit(x, c) == c.Trump && Rank(x, c) > top).ToArray();
                if (higher.Length > 0)
                    cards = higher;
            }
        }
        var result = new List<PlayAction>();
        foreach (var card in cards)
        {
            if (special && card.IsJoker && !Declarations.ContainsKey(card) && trick.Count == 0)
            {
                var choices = Suits.AsEnumerable();
                string mode = Text(j, "leadSuits");
                int active = Text(ContractOptions(c), "activeSeats") == "all_four" ? 4 : 3;
                var leads = History.Where((x, i) => i % active == 0).ToArray();
                if (mode == "never_led")
                    choices = choices.Where(s => !leads.Any(x => LedSuit([x], c) == s));
                if (mode == "not_shown_void")
                    choices = choices.Where(s => !History.Where((x, i) => i % active != 0 && x.Seat == CurrentSeat && EffectiveSuit(x.Action.Card, c) != LedSuit([History[i / active * active]], c)).Any(x => LedSuit([History[History.ToList().IndexOf(x) / active * active]], c) == s));
                if (mode == "last_card_only" && hand.Count != 1)
                    choices = [];
                if (mode == "previously_led_by_declarer")
                    choices = choices.Where(s => leads.Any(x => x.Seat == DeclarerSeat && LedSuit([x], c) == s));
                if (!choices.Any() && Text(j, "noEligibleSuit") == "any_suit" || !choices.Any() && Text(j, "noEligibleSuit") == "forbid_until_last_trick_then_any" && History.Count / active == 9)
                    choices = Suits;
                result.AddRange(choices.Select(s => new PlayAction(card, s)));
            }
            else
                result.Add(new(card));
        }
        if (result.Count == 0)
            throw new InvalidOperationException("Configured Joker rules leave no legal move");
        return result;
    }
    internal int CurrentSeat, DeclarerSeat;
    public override Suit LedSuit(IReadOnlyList<PublicPlay> trick, Contract c) => Declarations.TryGetValue(trick[0].Action.Card, out var named) ? named : base.LedSuit(trick, c);
    public override int Winner(IReadOnlyList<PublicPlay> trick, Contract c)
    {
        var led = LedSuit(trick, c);
        bool low = (c.IsMisere || c.Trump == Suit.Unknown) && Text(ContractOptions(c).GetProperty("joker"), "unassignedRank") == "lowest";
        int Strength(PublicPlay p)
        {
            var card = p.Action.Card;
            var suit = EffectiveSuit(card, c);
            if (card.IsJoker)
            {
                if (Declarations.ContainsKey(card))
                    return suit == led ? 1000 + Rank(card, c) : 0;
                return low ? -100 + Rank(card, c) : 1000 + Rank(card, c);
            }
            return suit == c.Trump && c.Trump != Suit.Unknown ? 500 + Rank(card, c) : suit == led ? 100 + Rank(card, c) : 0;
        }
        return trick.MaxBy(Strength)!.Seat;
    }
    // Contract value and auction order are deliberately independent.
    public override (bool Made, int[] Delta) Score(Contract c, int declarer, int[] tricks)
    {
        var opts = ContractOptions(c);
        int team = declarer % 2, won = tricks[declarer] + (Text(opts, "activeSeats") == "all_four" ? tricks[(declarer + 2) % 4] : 0);
        bool made = c.IsMisere ? tricks[declarer] == 0 : won >= c.Tricks;
        var score = Section("scoring");
        var delta = new int[2];
        int value = Value(c);
        if (made)
        {
            delta[team] = value;
            if (!c.IsMisere && won == 10)
            {
                var slam = score.GetProperty("slam");
                if (!Flag(slam, "onlyBelowTenBid") || c.Tricks < 10)
                    delta[team] = Text(slam, "mode") switch
                    {
                        "floor" => Math.Max(value, Number(slam, "value")),
                        "bonus" => value + Number(slam, "value"),
                        _ => value
                    };
            }
        }
        else
        {
            if (Text(score.GetProperty("failure"), "contractor") == "lose_contract_value")
                delta[team] = -value;
            if (Text(score.GetProperty("failure"), "defenders") == "gain_contract_value")
                delta[1 - team] = value;
        }
        if (c.IsMisere)
        {
            var z = score.GetProperty("zeroContractDefenders");
            if (Text(z, "when") == "always" || !made)
                delta[1 - team] += Number(z, "pointsPerTrick") * (Text(z, "basis") switch
                {
                    "declarer_tricks" => tricks[declarer],
                    "declarer_team_tricks" => tricks[declarer] + tricks[(declarer + 2) % 4],
                    "defender_tricks" => 10 - won,
                    _ => 0
                });
        }
        else
        {
            var d = score.GetProperty("defenderTricks");
            if (Text(d, "when") == "always" || Text(d, "when") == "only_when_contract_fails" && !made)
                delta[1 - team] += (10 - won) * Number(d, "pointsPerTrick");
        }
        return (made, delta);
    }
    public override int Value(Contract c) => c.Kind == ContractKind.Normal ? Section("scoring").GetProperty("normalTable").GetProperty(c.Tricks.ToString()).GetProperty(Symbol(c.Trump)).GetInt32() : Number(ContractOptions(c), "points");
    public override int Order(Contract c) => Array.IndexOf(Section("auction").GetProperty("orderLowToHigh").EnumerateArray().Select(x => x.GetString()).ToArray(), Key(c));
    public override List<Contract?> LegalBids(Contract? high, IReadOnlyList<AuctionAction> history) => Section("auction").GetProperty("orderLowToHigh").EnumerateArray().Select(x => Parse(x.GetString()!)).Where(c => high == null || Order(c) > Order(high)).Where(c => { var min = ContractOptions(c).GetProperty("eligibility").GetProperty("minimumPriorNormalLevel"); return min.ValueKind == JsonValueKind.Null || history.Any(h => h.Bid is { } b && (b.Kind == ContractKind.Normal || b.Kind == ContractKind.Inkle && Flag(Section("auction").GetProperty("inkle"), "countsAsNormalHistory")) && b.Tricks >= min.GetInt32()); }).Cast<Contract?>().Prepend(null).ToList();
    public override Suit EffectiveSuit(PlayingCard card, Contract c)
    {
        if (c.IsMisere || c.Trump == Suit.Unknown)
            return Declarations.GetValueOrDefault(card, card.Suit);
        var p = Section("play");
        if (card.IsJoker)
            return Flag(p, "jokerIsTrump") ? c.Trump : Suit.Joker;
        if (card.Rank == 11 && (card.Suit == c.Trump ? Flag(p, "rightBower") : Flag(p, "leftBower") && ((card.Suit is Suit.Hearts or Suit.Diamonds) == (c.Trump is Suit.Hearts or Suit.Diamonds))))
            return c.Trump;
        return card.Suit;
    }
    public override int Rank(PlayingCard card, Contract c)
    {
        if (card.IsJoker)
            return 200 - Array.IndexOf(Section("deck").GetProperty("jokerOrderHighToLow").EnumerateArray().Select(x => x.GetString()).ToArray(), "X" + (card.Rank - 15));
        if (!c.IsMisere && c.Trump != Suit.Unknown && card.Rank == 11 && EffectiveSuit(card, c) == c.Trump && (card.Suit == c.Trump ? Flag(Section("play"), "rightBower") : Flag(Section("play"), "leftBower")))
            return card.Suit == c.Trump ? 199 : 198;
        return card.Rank;
    }
}
public static class RuleConfiguration
{
    public static Rules Load(string? path = null)
    {
        if (path == null)
            return new Rules();
        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("schemaVersion", out _))
            return JsonSerializer.Deserialize<Rules>(text, ArenaCommand.Json) ?? throw new ArgumentException("Empty rules");
        var root = doc.RootElement;
        var m = root.GetProperty("match");
        var a = root.GetProperty("auction");
        var r = new ConfiguredRules { Document = text, AllowMisere = root.GetProperty("contracts").GetProperty("misere").GetProperty("enabled").GetBoolean(), AllowOpenMisere = root.GetProperty("contracts").GetProperty("open_misere").GetProperty("enabled").GetBoolean(), MiserePoints = root.GetProperty("contracts").GetProperty("misere").GetProperty("points").GetInt32(), OpenMiserePoints = root.GetProperty("contracts").GetProperty("open_misere").GetProperty("points").GetInt32(), Id = root.GetProperty("metadata").GetProperty("id").GetString()!, WinScore = m.GetProperty("winScore").GetInt32(), LossScore = m.GetProperty("lossScore").ValueKind == JsonValueKind.Null ? int.MinValue : m.GetProperty("lossScore").GetInt32(), MustContractToWin = m.GetProperty("mustMakeContractToWin").GetBoolean(), PassIsPermanent = a.GetProperty("passReentry").GetString() == "never" };
        r.Validate();
        return r;
    }
}
