using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Use inexpensive local decisions without rollouts. Omitting rules preserves the historical policy.</summary>
public sealed class WeakBot : FiveHundredRuleBot
{
    public WeakBot(int seed = 1, Rules? rules = null)
        : base(RuleBotLevel.Weak, seed, rules, new global::FiveHundredArena.WeakBot()) { }
    public static WeakBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));
}
