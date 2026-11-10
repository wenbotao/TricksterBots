using System.Text.Json.Serialization;
using Trickster.cloud;
namespace FiveHundredArena;

public sealed partial record ConfiguredRules
{
    [JsonIgnore] public bool IsV11 => Root.TryGetProperty("extensions", out var x) && Text(x, "version") == "1.1";
    // Persist only public rule state in v1.1 observations. Legacy JSON remains byte-compatible.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public PublicRuleState? PublicState
    {
        get => IsV11 ? new(History.ToArray(),Declarations.Select(p=>new JokerNomination(DeclarationOwners.GetValueOrDefault(p.Key,DeclarerSeat),new(p.Key,p.Value))).ToArray(),CurrentSeat,DeclarerSeat) : null;
        init
        {
            if(value==null)return;
            History=value.History.ToArray();Declarations=value.Nominations.ToDictionary(n=>n.Action.Card,n=>n.Action.JokerLeadSuit);
            DeclarationOwners=value.Nominations.ToDictionary(n=>n.Action.Card,n=>n.Seat);CurrentSeat=value.CurrentSeat;DeclarerSeat=value.DeclarerSeat;
        }
    }
    internal Contract? PreparedContract;
    internal IReadOnlyDictionary<PlayingCard, (Suit Suit, int Rank)>? PreparedCards;
    internal Dictionary<PlayingCard, int> DeclarationOwners = new();
    internal static string ContractName(Contract c) => c.Kind switch
    {
        ContractKind.Misere => "misere", ContractKind.OpenMisere => "open_misere",
        ContractKind.MisereNoPickup => "misere_no_pickup", ContractKind.GrandNullo => "grand_nullo",
        ContractKind.GrandSlam => "grand_slam", _ => "normal"
    };
    internal bool CanGrandSlam => IsV11 && Flag(Section("extensions"), "grandSlamEnabled");
    internal int[] ActiveSeats(Contract c, int declarer) => Enumerable.Range(0, 4)
        .Where(s => Text(ContractOptions(c), "activeSeats") == "all_four" || s != (declarer + 2) % 4).ToArray();
    internal ConfiguredRules Detached() => this with
    {
        History = History.ToArray(), Declarations = new(Declarations), DeclarationOwners = new(DeclarationOwners)
    };
    internal bool AceNoFace(IReadOnlyList<PlayingCard> hand) => hand.Count(c => c.Rank == 14) == 1
        && !hand.Any(c => c.IsJoker || c.Rank is 11 or 12 or 13);
    internal int? WinnerForContract(Contract contract,int declarer,int[] scores,bool made)
    {
        if(contract.Kind!=ContractKind.NoContractNT)return WinningTeam(scores,declarer%2,made);
        if(scores[0]<=LossScore)return 1;if(scores[1]<=LossScore)return 0;
        if(MustContractToWin)return null;
        bool a=scores[0]>=WinScore,b=scores[1]>=WinScore;
        if(a&&b)return scores[0]==scores[1]?null:scores[0]>scores[1]?0:1;
        return a?0:b?1:null;
    }
    private HashSet<Suit> ShownVoids(Contract contract,int seat)
    {
        var result=new HashSet<Suit>();var seenLeads=new HashSet<Suit>();int active=Text(ContractOptions(contract),"activeSeats")=="all_four"?4:3;
        string follow=Text(ContractOptions(contract).GetProperty("joker"),"follow");
        for(int start=0;start<History.Count;start+=active)
        {
            var led=LedSuit([History[start]],contract);bool first=seenLeads.Add(led);
            foreach(var play in History.Skip(start+1).Take(Math.Min(active-1,History.Count-start-1)))
            {
                bool optionalJoker=play.Action.Card.IsJoker&&!Declarations.ContainsKey(play.Action.Card)&&(follow=="anytime"||follow=="first_lead_of_suit_or_void"&&first);
                if(play.Seat==seat&&!optionalJoker&&EffectiveSuit(play.Action.Card,contract)!=led)result.Add(led);
            }
        }
        return result;
    }
    private void ValidateV11()
    {
        foreach (string name in new[] { "misere_no_pickup", "grand_nullo", "grand_slam" })
            if (!Section("contracts").TryGetProperty(name, out _)) throw new ArgumentException("v1.1 requires complete special contract definitions");
        var noPickup = Section("contracts").GetProperty("misere_no_pickup");
        if (Text(noPickup, "kitty") != "untouched" || Text(noPickup, "target") != "declarer_zero" || Text(noPickup, "activeSeats") != "exclude_declarer_partner")
            throw new ArgumentException("No Pickup needs untouched kitty and one active declarer");
        var grand = Section("contracts").GetProperty("grand_nullo");
        if (Flag(grand, "enabled") && (Text(grand, "target") != "team_zero" || Text(grand, "activeSeats") != "all_four" || Text(grand, "kitty") != "sequential_team_exchange"))
            throw new ArgumentException("Grand Nullo requires team zero, four active seats and sequential exchanges");
        var slam = Section("contracts").GetProperty("grand_slam");
        if (CanGrandSlam != Flag(slam, "enabled") || Text(slam, "target") != "solo_ten" || Text(slam, "activeSeats") != "exclude_declarer_partner")
            throw new ArgumentException("Grand Slam extension and solo-ten contract must agree");
        if (Text(Section("auction").GetProperty("allPass"), "action") == "force_dealer" && !LegalBids(null, []).Any(b => b != null))
            throw new ArgumentException("Forced dealer needs a reachable normal bid");
    }
}

public sealed record JokerNomination(int Seat,PlayAction Action);
public sealed record PublicRuleState(PublicPlay[] History,JokerNomination[] Nominations,int CurrentSeat,int DeclarerSeat);
