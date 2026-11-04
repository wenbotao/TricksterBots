using FiveHundredArena;
using Trickster.cloud;

namespace FiveHundredLab;

/// <summary>Fast v8 with public-information partner safeguards and a declared low-discard convention.</summary>
public sealed class PartnerFastV9(int seed) : IPlayer
{
    private readonly VisibilityFastV8 fast = new(seed);
    public string Name => "partner-fast-v9";
    public string LastReason { get; private set; } = "none";
    public AdaptiveSearchV1? LastSearch { get; private set; }

    // Canonical inputs remove accidental tie-breaking by the caller's hand layout.
    private static Observation Canonical(Observation v) => v with { Hand = v.Hand.OrderBy(c => c.Suit).ThenBy(c => c.Rank).ToArray() };
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var result = fast.Bid(Canonical(view), legal);
        LastReason = "fast-bid"; LastSearch = fast.LastSearch; return result;
    }
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var v = Canonical(view);
        var selected = fast.Discard(v, count).ToArray(); LastSearch = fast.LastSearch; LastReason = "fast-discard";
        var repaired = PartnerPolicyV9.RepairDiscards(v, selected);
        if (!selected.ToHashSet().SetEquals(repaired)) LastReason = "preserve-discard-structure";
        return repaired;
    }
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var v = Canonical(view);
        var choices = legal.OrderBy(p => p.Card.Suit).ThenBy(p => p.Card.Rank).ThenBy(p => p.JokerLeadSuit).ToArray();
        if (choices.Length == 0) throw new ArgumentException("No legal play");
        if (choices.Length == 1) return Direct(choices[0], "forced-legal");
        var immediate = PartnerPolicyV9.ImmediateMiserePlay(v, choices);
        if (immediate != null) return Direct(immediate, "public-misere-certainty");
        if (v.Contract!.IsMisere && v.Declarer == v.Seat && v.Trick.Count == 0)
        {
            var safe = choices.Where(p => !PartnerPolicyV9.CertainLeadWin(v, p)).ToArray();
            if (safe.Length > 0 && safe.Length < choices.Length) choices = safe;
        }
        if (!v.Contract.IsMisere)
        {
            var protect = PartnerPolicyV9.ProtectPartnerWinner(v, choices);
            if (protect != null) return Direct(protect, "protect-partner-winner");
            var lead = PartnerPolicyV9.PartnerLead(v, choices);
            if (lead != null) return Direct(lead.Value.Play, lead.Value.Reason);
        }
        var result = fast.Play(v, choices);
        LastSearch = fast.LastSearch; LastReason = "fast-play"; return result;
    }
    private PlayAction Direct(PlayAction play, string reason)
    {
        LastReason = reason; LastSearch = new(1, 0, 0, true, "play"); return play;
    }
}
