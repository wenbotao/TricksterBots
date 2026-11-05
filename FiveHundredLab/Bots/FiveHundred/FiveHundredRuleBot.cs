using System.Text.Json.Nodes;
using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Rule binding and diagnostics only. Each concrete bot owns its decisions.</summary>
public abstract class FiveHundredRuleBot : IPlayer, IJokerDeclarationPlayer
{
    private readonly Rules bound;
    protected readonly SeededRandom random;
    public string Name
    {
        get;
    }
    public int LastRollouts
    {
        get; protected set;
    }

    protected FiveHundredRuleBot(string name, int seed, Rules? rules)
    {
        Name = name;
        bound = rules ?? new Rules();
        bound.Validate();
        random = new(seed);
    }
    protected ConfiguredRules? RequireMatchingRules(Observation view)
    {
        if (bound is ConfiguredRules expected)
        {
            if (view.Rules is not ConfiguredRules actual || (actual.Document != expected.Document && !JsonNode.DeepEquals(JsonNode.Parse(expected.Document), JsonNode.Parse(actual.Document))))
                throw new ArgumentException("Bot and engine rule configurations differ");
            return actual;
        }
        if (view.Rules is ConfiguredRules || view.Rules != bound)
            throw new ArgumentException("Bot and engine rules differ");
        return null;
    }
    public abstract Contract? Bid(Observation view, IReadOnlyList<Contract?> legal);
    public abstract IReadOnlyList<PlayingCard> Discard(Observation view, int count);
    public abstract PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal);
    public virtual IReadOnlyList<PlayAction> DeclareJokers(Observation view)
    {
        RequireMatchingRules(view);
        // Preserve the existing choice to decline optional naming. Concrete policies may override it.
        return Array.Empty<PlayAction>();
    }
}
