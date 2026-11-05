using FiveHundredArena;
namespace FiveHundredLab;

/// <summary>The full three-stage opponent-adaptive Strong, with a stable user-facing name.</summary>
public sealed class AdaptV2 : IPlayer,IJokerDeclarationPlayer
{
    private readonly LearningStrongV2 inner;
    public AdaptV2(int seed=1,Rules? rules=null)=>inner=new(seed,OpponentAdaptationStage.Play,rules:rules);
    public string Name=>"Adapt-v2";
    public OpponentBeliefsV2 Beliefs=>inner.Beliefs;
    public SearchReport? LastSearch=>inner.LastSearch;
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>inner.Bid(v,legal);
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner.DeclareJokers(v);
}
