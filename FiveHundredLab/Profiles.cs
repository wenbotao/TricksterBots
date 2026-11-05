using FiveHundredArena;
namespace FiveHundredLab;
public static class Profiles
{
    public static IPlayer Create(string name, int seed, string? model = null) => name switch
    {
        "Adapt-v2" or "adapt-v2" => new AdaptV2(seed),
        "Adapt" or "adapt" or "adapt-v1" => new Adapt(seed),
        "opponent-strong-bidding-v1" => new OpponentStrong(seed,OpponentAdaptationStage.Bidding),
        "opponent-strong-belief-v1" => new OpponentStrong(seed,OpponentAdaptationStage.Belief),
        "opponent-strong-play-v1" => new OpponentStrong(seed,OpponentAdaptationStage.Play),
        "random-rules-v1" => CreateForRules("random-legal-v1",seed),
        "weak-rules-v1" => CreateForRules("weak-v1",seed),
        "medium-rules-v1" => CreateForRules("medium-v1",seed),
        "fast-rules-v1" => CreateForRules("visibility-fast-v8",seed),
        "strong-rules-v1" => CreateForRules("strong-v1",seed),
        "partner-rules-v1" => CreateForRules("partner-fast-v9",seed),
        "medium-v1" => new MediumBotPlayer(), "strong-v1" => new StrongBot(seed), "weak-v1" => new WeakBot(), "random-legal-v1" => new RandomLegalBot(seed),
        "compact-medium-v1" => new CompactMediumV1(), "compact-strong-v1" => new CompactStrongV1(seed),
        "cached-medium-v1" => new CachedMediumV1(), "cached-strong-v1" => new CachedStrongV1(seed),
        "visibility-fast-v8" => new VisibilityFastV8(seed),
        "partner-fast-v9" => new PartnerFastV9(seed),
        "visibility-fast-v6" => new VisibilityFastV6(seed), "visibility-quality-v7" => new VisibilityQualityV7(seed),
        "selective-fast-v4" => new SelectiveFullV1(seed,SelectiveProfileV1.FastV4),
        "visibility-medium-v5" => new VisibilityMediumV5(), "visibility-strong-v5" => new VisibilityStrongV5(seed),
        "selective-belief-v3" => new SelectiveFullV1(seed,SelectiveProfileV1.BeliefV3),
        "selective-quality-v2" => new SelectiveFullV1(seed,SelectiveProfileV1.QualityV2),
        "selective-play-v1" => new SelectiveFullV1(seed,SelectiveProfileV1.PlayOnly), "selective-joint-v1" => new SelectiveFullV1(seed,SelectiveProfileV1.Joint),
        "tactician-v1" => new TacticianV1(),
        "adaptive-sprint-v1" => new AdaptiveV1(seed, AdaptiveProfileV1.Sprint),
        "adaptive-balanced-v1" => new AdaptiveV1(seed, AdaptiveProfileV1.Balanced),
        "value-guided-v3" => new ValueGuidedV3(seed,LoadPhaseV2(model)),
        "distilled-phase-v2" => new AdaptiveV1(seed,AdaptiveProfileV1.DistilledPhaseV2,LoadPhaseV2(model)),
        "distilled-v1" => new AdaptiveV1(seed, AdaptiveProfileV1.Distilled, ValueModelV1.Load(model ?? throw new ArgumentException("distilled-v1 requires --model"))),
        _ => throw new ArgumentException("Unknown named profile: " + name)
    };
    public static IPlayer CreateForRules(string name,int seed,string? rulePath=null)=>CreateForRules(name,seed,RuleConfiguration.Load(rulePath));
    public static IPlayer CreateForRules(string name,int seed,Rules rules)
    {
        if(name is "Adapt-v2" or "adapt-v2")return new AdaptV2(seed,rules);
        if(name is "Adapt" or "adapt" or "adapt-v1")return new Adapt(seed,rules);
        if(name.StartsWith("opponent-strong-"))return new OpponentStrong(seed,name switch{"opponent-strong-bidding-v1"=>OpponentAdaptationStage.Bidding,"opponent-strong-belief-v1"=>OpponentAdaptationStage.Belief,"opponent-strong-play-v1"=>OpponentAdaptationStage.Play,_=>throw new ArgumentException("Unknown adaptive profile")},rules:rules);
        name=name switch{"random-rules-v1"=>"random-legal-v1","weak-rules-v1"=>"weak-v1","medium-rules-v1"=>"medium-v1","fast-rules-v1"=>"visibility-fast-v8","strong-rules-v1"=>"strong-v1","partner-rules-v1"=>"partner-fast-v9",_=>name};
        return name switch
        {
            "random-legal-v1" => new Bots.FiveHundred.RandomBot(seed, rules),
            "weak-v1" => new Bots.FiveHundred.WeakBot(seed, rules),
            "medium-v1" => new Bots.FiveHundred.MediumBot(seed, rules),
            "visibility-fast-v8" => new Bots.FiveHundred.FastBot(seed, rules),
            "strong-v1" => new Bots.FiveHundred.StrongBot(seed, rules),
            "partner-fast-v9" => new Bots.FiveHundred.PartnerBot(seed, rules),
            _ => throw new ArgumentException("No rule-aware version: " + name)
        };
    }
    private static ValueModelV1 LoadPhaseV2(string? path)
    {
        var m=ValueModelV1.Load(path??throw new ArgumentException("distilled-phase-v2 requires --model"));
        if(m.Teacher!="belief-full16-diverse-phase-v2"||m.PhaseModels.Count==0)throw new InvalidDataException("Expected a phase-v2 teacher model");return m;
    }
}
