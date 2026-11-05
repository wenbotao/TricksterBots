using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Use a small candidate set and four sampled continuations. Omitting rules preserves the historical policy.</summary>
public sealed class FastBot : FiveHundredRuleBot
{
    public FastBot(int seed = 1, Rules? rules = null)
        : base(RuleBotLevel.Fast, seed, rules, new VisibilityFastV8(seed)) { }
    public static FastBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));
}
