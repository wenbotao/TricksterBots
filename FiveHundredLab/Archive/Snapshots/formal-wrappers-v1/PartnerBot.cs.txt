using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Prioritize proven partner-friendly actions before bounded search. Omitting rules preserves the historical policy.</summary>
public sealed class PartnerBot : FiveHundredRuleBot
{
    public PartnerBot(int seed = 1, Rules? rules = null)
        : base(RuleBotLevel.Partner, seed, rules, new PartnerFastV9(seed)) { }
    public static PartnerBot FromConfiguration(string? path = null, int seed = 1)
        => new(seed, RuleConfiguration.Load(path));
}
