using FiveHundredArena;
namespace FiveHundredLab;
public static class Profiles
{
    public static IPlayer Create(string name, int seed, string? model = null) => name switch
    {
        "random-v1.1" or "random-pass-v1.1" or "weak-v1.1" or "medium-v1.1" or "fast-v1.1" or "strong-v1.1" or "partner-v1.1" or "strong-plus-v1.1" or "strong-flow-cached-v1.1" => CreateForRules(name,seed),
        "strong-study-base8-v1" => CreateForRules("strong-study-base8-v1",seed),
        "strong-study-all32-v1" => CreateForRules("strong-study-all32-v1",seed),
        "strong-study-wide32-v1" => CreateForRules("strong-study-wide32-v1",seed),
        "strong-study-tuned32-v1" => CreateForRules("strong-study-tuned32-v1",seed),
        "strong-study-bid32-v1" => CreateForRules("strong-study-bid32-v1",seed),
        "strong-study-discard32-v1" => CreateForRules("strong-study-discard32-v1",seed),
        "strong-study-play32-v1" => CreateForRules("strong-study-play32-v1",seed),
        "strong-structured-v1" => CreateForRules("strong-structured-v1",seed),
        "strong-tactical-v1" => CreateForRules("strong-tactical-v1",seed),
        "strong-misere-proof-v1" => CreateForRules("strong-misere-proof-v1",seed),
        "strong-bid-guard-v1" => CreateForRules("strong-bid-guard-v1",seed),
        "strong-hand-guard-v1" => CreateForRules("strong-hand-guard-v1",seed),
        "strong-discard-guard-v1" => CreateForRules("strong-discard-guard-v1",seed),
        "strong-discard-shape-v1" => CreateForRules("strong-discard-shape-v1",seed),
        "strong-discard-audit-v1" => CreateForRules("strong-discard-audit-v1",seed),
        "strong-discard-shape-efficient-v1" => CreateForRules("strong-discard-shape-efficient-v1",seed),
        "strong-discard-audit-efficient-v1" => CreateForRules("strong-discard-audit-efficient-v1",seed),
        "strong-discard-guard-efficient-v1" => CreateForRules("strong-discard-guard-efficient-v1",seed),
        "strong-discard-guard-lean-v1" => CreateForRules("strong-discard-guard-lean-v1",seed),
        "strong-play-forced-v1" => CreateForRules("strong-play-forced-v1",seed),
        "strong-play-cached-v1" => CreateForRules("strong-play-cached-v1",seed),
        "strong-flow-cached-v1" => CreateForRules("strong-flow-cached-v1",seed),
        "strong-play-incremental-v1" => CreateForRules("strong-play-incremental-v1",seed),
        "strong-contract-proof-v1" => CreateForRules("strong-contract-proof-v1",seed),
        "strong-plus-v1" => CreateForRules("strong-plus-v1",seed),
        "random-pass-v1" => CreateForRules("random-pass-v1",seed),
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
        if (rules is ConfiguredRules { IsV11:true } || name.EndsWith("-v1.1"))
        {
            IPlayer? current = name switch
            {
                "random-v1.1" or "random-rules-v1" or "random-legal-v1" => new Bots.FiveHundred.V11.RandomBot(seed,rules),
                "random-pass-v1.1" or "random-pass-v1" => new Bots.FiveHundred.V11.RandomPassBot(seed,rules),
                "weak-v1.1" or "weak-rules-v1" or "weak-v1" => new Bots.FiveHundred.V11.WeakBot(seed,rules),
                "medium-v1.1" or "medium-rules-v1" or "medium-v1" => new Bots.FiveHundred.V11.MediumBot(seed,rules),
                "fast-v1.1" or "fast-rules-v1" or "visibility-fast-v8" => new Bots.FiveHundred.V11.FastBot(seed,rules),
                "strong-v1.1" or "strong-rules-v1" or "strong-v1" => new Bots.FiveHundred.V11.StrongBot(seed,rules),
                "partner-v1.1" or "partner-rules-v1" or "partner-fast-v9" => new Bots.FiveHundred.V11.PartnerBot(seed,rules),
                "strong-plus-v1.1" or "strong-plus-v1" => new Bots.FiveHundred.V11.StrongPlusBot(seed,rules),
                "strong-flow-cached-v1.1" or "strong-flow-cached-v1" => new Bots.FiveHundred.V11.StrongFlowCachedBot(seed,rules),
                _ => null
            };
            if(current!=null)return current;
        }
        if(name is "Adapt-v2" or "adapt-v2")return new AdaptV2(seed,rules);
        if(name is "Adapt" or "adapt" or "adapt-v1")return new Adapt(seed,rules);
        if(name.StartsWith("opponent-strong-"))return new OpponentStrong(seed,name switch{"opponent-strong-bidding-v1"=>OpponentAdaptationStage.Bidding,"opponent-strong-belief-v1"=>OpponentAdaptationStage.Belief,"opponent-strong-play-v1"=>OpponentAdaptationStage.Play,_=>throw new ArgumentException("Unknown adaptive profile")},rules:rules);
        name=name switch{"random-rules-v1"=>"random-legal-v1","weak-rules-v1"=>"weak-v1","medium-rules-v1"=>"medium-v1","fast-rules-v1"=>"visibility-fast-v8","strong-rules-v1"=>"strong-v1","partner-rules-v1"=>"partner-fast-v9",_=>name};
        return name switch
        {
            "strong-study-base8-v1" => new Bots.FiveHundred.StrongAblationBot(Bots.FiveHundred.StrongAblationProfile.Baseline8, seed, rules),
            "strong-study-all32-v1" => new Bots.FiveHundred.StrongAblationBot(Bots.FiveHundred.StrongAblationProfile.All32, seed, rules),
            "strong-study-wide32-v1" => new Bots.FiveHundred.StrongAblationBot(Bots.FiveHundred.StrongAblationProfile.Wide32, seed, rules),
            "strong-study-tuned32-v1" => new Bots.FiveHundred.StrongAblationBot(Bots.FiveHundred.StrongAblationProfile.Tuned32, seed, rules),
            "strong-study-bid32-v1" => new Bots.FiveHundred.StrongAblationBot(Bots.FiveHundred.StrongAblationProfile.Bid32, seed, rules),
            "strong-study-discard32-v1" => new Bots.FiveHundred.StrongAblationBot(Bots.FiveHundred.StrongAblationProfile.Discard32, seed, rules),
            "strong-study-play32-v1" => new Bots.FiveHundred.StrongAblationBot(Bots.FiveHundred.StrongAblationProfile.Play32, seed, rules),
            "strong-structured-v1" => new Bots.FiveHundred.StrongStructuredBot(seed, rules),
            "strong-tactical-v1" => new Bots.FiveHundred.StrongTacticalBot(seed, rules),
            "strong-misere-proof-v1" => new Bots.FiveHundred.StrongProofAblationBot(Bots.FiveHundred.StrongProofProfile.MisereOnly, seed, rules),
            "strong-bid-guard-v1" => new Bots.FiveHundred.StrongBidGuardBot(seed, rules),
            "strong-hand-guard-v1" => new Bots.FiveHundred.StrongHandGuardBot(seed, rules),
            "strong-discard-guard-v1" => new Bots.FiveHundred.StrongDiscardGuardBot(seed, rules),
            "strong-discard-shape-v1" => new Bots.FiveHundred.StrongDiscardRefinedBot(Bots.FiveHundred.DiscardRefinementProfile.Shape,seed,rules),
            "strong-discard-audit-v1" => new Bots.FiveHundred.StrongDiscardRefinedBot(Bots.FiveHundred.DiscardRefinementProfile.SelectiveAudit,seed,rules),
            "strong-discard-shape-efficient-v1" => new Bots.FiveHundred.StrongDiscardRefinedBot(Bots.FiveHundred.DiscardRefinementProfile.EfficientShape,seed,rules),
            "strong-discard-audit-efficient-v1" => new Bots.FiveHundred.StrongDiscardRefinedBot(Bots.FiveHundred.DiscardRefinementProfile.EfficientAudit,seed,rules),
            "strong-discard-guard-efficient-v1" => new Bots.FiveHundred.StrongDiscardRefinedBot(Bots.FiveHundred.DiscardRefinementProfile.EfficientBaseline,seed,rules),
            "strong-discard-guard-lean-v1" => new Bots.FiveHundred.StrongDiscardLeanBot(seed,rules),
            "strong-play-forced-v1" => new Bots.FiveHundred.StrongPlayForcedBot(seed,rules),
            "strong-play-cached-v1" => new Bots.FiveHundred.StrongPlayCachedBot(seed,rules),
            "strong-flow-cached-v1" => new Bots.FiveHundred.StrongFlowCachedBot(seed,rules),
            "strong-play-incremental-v1" => new Bots.FiveHundred.StrongPlayIncrementalBot(seed,rules),
            "strong-contract-proof-v1" => new Bots.FiveHundred.StrongProofAblationBot(Bots.FiveHundred.StrongProofProfile.ContractOnly, seed, rules),
            "strong-plus-v1" => new Bots.FiveHundred.StrongPlusBot(seed, rules),
            "random-pass-v1" => new Bots.FiveHundred.RandomPassBot(seed, rules),
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
