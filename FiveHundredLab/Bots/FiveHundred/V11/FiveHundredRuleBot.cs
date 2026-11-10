using System.Text.Json.Nodes;
using FiveHundredArena;
namespace FiveHundredLab.Bots.FiveHundred.V11;

/// <summary>Rule binding and diagnostics only. Each concrete bot owns its decisions.</summary>
public abstract class FiveHundredRuleBot : IPlayer, IJokerDeclarationPlayer, IRuleVariationPlayer
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
        Name = name.EndsWith("-v1.1") ? name : name.Replace("-rules-v1", "-v1.1").Replace("strong-plus-v1", "strong-plus-v1.1").Replace("strong-flow-cached-v1", "strong-flow-cached-v1.1").Replace("random-pass-v1", "random-pass-v1.1");
        bound = rules ?? RuleConfiguration.Load();
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
    protected static Contract? LegalBid(Contract? selected, IReadOnlyList<Contract?> legal) => legal.Contains(selected) ? selected : legal.First();
    public SearchReport? LastV11Search { get; protected set; }
    public virtual bool RequestRedeal(Observation view)
    { RequireMatchingRules(view); return view.Rules is ConfiguredRules r && r.AceNoFace(view.Hand); }
    public virtual bool DeclareGrandSlam(Observation view) { RequireMatchingRules(view);return false; }
    public virtual PlayingCard TransferCard(Observation view, IReadOnlyList<PlayingCard> legal)
    {
        RequireMatchingRules(view);
        return legal.OrderByDescending(c => view.Rules.EffectiveSuit(c, view.Contract!) == view.Contract!.Trump ? 1000 + view.Rules.Rank(c, view.Contract!) : view.Rules.Rank(c, view.Contract!)).First();
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
