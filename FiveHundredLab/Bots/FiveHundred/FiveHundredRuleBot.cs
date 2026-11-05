using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Common rule binding and legal decision interface for the six supported strategies.</summary>
public abstract class FiveHundredRuleBot : IPlayer, IJokerDeclarationPlayer
{
    private readonly RuleAwarePlayer strategy;
    protected FiveHundredRuleBot(RuleBotLevel level, int seed, Rules? rules, IPlayer legacy)
        => strategy = new RuleAwarePlayer(level, seed, legacy, rules);
    public string Name => strategy.Name;
    public int LastRollouts => strategy.LastRollouts;
    public Contract? Bid(Observation view, IReadOnlyList<Contract?> legal) => strategy.Bid(view, legal);
    public IReadOnlyList<PlayingCard> Discard(Observation view, int count) => strategy.Discard(view, count);
    public PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal) => strategy.Play(view, legal);
    public IReadOnlyList<PlayAction> DeclareJokers(Observation view) => strategy.DeclareJokers(view);
}
