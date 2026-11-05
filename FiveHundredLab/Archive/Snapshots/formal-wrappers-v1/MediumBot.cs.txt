using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Use rule-aware hand evaluation and deterministic tactics. Omitting rules preserves the historical policy.</summary>
public sealed class MediumBot : FiveHundredRuleBot
{
    public MediumBot(int seed = 1, Rules? rules = null)
        : base(RuleBotLevel.Medium, seed, rules, new MediumBotPlayer()) { }
    public static MediumBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));
}
