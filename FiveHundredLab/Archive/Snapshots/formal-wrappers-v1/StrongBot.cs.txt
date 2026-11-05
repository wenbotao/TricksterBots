using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Search all legal plays with eight sampled continuations. Omitting rules preserves the historical policy.</summary>
public sealed class StrongBot : FiveHundredRuleBot
{
    public StrongBot(int seed = 1, Rules? rules = null)
        : base(RuleBotLevel.Strong, seed, rules, new global::FiveHundredArena.StrongBot(seed)) { }
    public static StrongBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));
}
