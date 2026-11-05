using FiveHundredArena;

namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Always pass in the auction; retain RandomBot's non-bidding policy.</summary>
public sealed class RandomPassBot : FiveHundredRuleBot
{
    private readonly RandomBot randomPolicy;

    public RandomPassBot(int seed = 1, Rules? rules = null)
        : base("random-pass-v1", seed, rules) => randomPolicy = new(seed, rules);

    public static RandomPassBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));

    public override Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        RequireMatchingRules(view);
        LastRollouts = 0;
        if (!legal.Contains(null))
            throw new InvalidOperationException("RandomPassBot requires rules that allow Pass.");
        // Passing consumes no random draws: non-bidding choices keep their own seeded sequence.
        return null;
    }

    public override IReadOnlyList<PlayingCard> Discard(Observation view, int count)
        => randomPolicy.Discard(view, count);

    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
        => randomPolicy.Play(view, legal);

    public override IReadOnlyList<PlayAction> DeclareJokers(Observation view)
        => randomPolicy.DeclareJokers(view);
}
