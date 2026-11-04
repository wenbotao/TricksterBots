using FiveHundredArena;
namespace FiveHundredLab;
public static class Profiles
{
    public static IPlayer Create(string name, int seed, string? model = null) => name switch
    {
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
    private static ValueModelV1 LoadPhaseV2(string? path)
    {
        var m=ValueModelV1.Load(path??throw new ArgumentException("distilled-phase-v2 requires --model"));
        if(m.Teacher!="belief-full16-diverse-phase-v2"||m.PhaseModels.Count==0)throw new InvalidDataException("Expected a phase-v2 teacher model");return m;
    }
}
