using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Bots.FiveHundred;

/// <summary>Uniform legal randomness, preserving the distinct historical card/suit sampling order.</summary>
public sealed class RandomBot : FiveHundredRuleBot
{
    private readonly SeededRandom defaultRandom;
    public RandomBot(int seed = 1, Rules? rules = null) : base("random-rules-v1", seed, rules) => defaultRandom = new(seed);
    public static RandomBot FromConfiguration(string? path = null, int seed = 1) => new(seed, RuleConfiguration.Load(path));
    public override Contract? Bid(Observation view, IReadOnlyList<Contract?> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        var source = rules == null ? defaultRandom : random;
        return legal[source.Next(legal.Count)];
    }
    public override IReadOnlyList<PlayingCard> Discard(Observation view, int count)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules != null && count == 0)
            return Array.Empty<PlayingCard>();
        if (count < 0 || count > view.Hand.Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        var cards = view.Hand.ToArray();
        var source = rules == null ? defaultRandom : random;
        source.Shuffle(cards);
        return cards.Take(count).ToArray();
    }
    public override PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal)
    {
        var rules = RequireMatchingRules(view);
        LastRollouts = 0;
        if (rules != null)
            return legal[random.Next(legal.Count)];
        var cards = legal.Select(p => p.Card).Distinct().ToArray();
        var chosen = cards[defaultRandom.Next(cards.Length)];
        var choices = legal.Where(p => p.Card == chosen).ToArray();
        return choices[defaultRandom.Next(choices.Length)];
    }
    public override IReadOnlyList<PlayAction> DeclareJokers(Observation view)
    {
        RequireMatchingRules(view);
        var named = new List<PlayAction>();
        foreach (var card in view.Hand.Where(c => c.IsJoker))
        {
            int choice = random.Next(5);
            if (choice > 0)
                named.Add(new(card, Rules.Suits[choice - 1]));
        }
        return named;
    }
}
