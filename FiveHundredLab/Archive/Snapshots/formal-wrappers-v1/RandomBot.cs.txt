using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Choose uniformly from legal actions. Omitting rules preserves the historical policy.</summary>
public sealed class RandomBot : FiveHundredRuleBot
{
    public RandomBot(int seed = 1, Rules? rules = null)
        : base(RuleBotLevel.Random, seed, rules, new RandomLegalBot(seed)) { }
    public static RandomBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));
}
