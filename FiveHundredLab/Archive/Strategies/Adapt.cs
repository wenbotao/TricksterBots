using FiveHundredArena;
namespace FiveHundredLab;

/// <summary>The full three-stage opponent-adaptive Strong, with a stable user-facing name.</summary>
public sealed class Adapt : IPlayer,IJokerDeclarationPlayer
{
    private readonly OpponentStrong inner;
    public Adapt(int seed=1,Rules? rules=null)=>inner=new(seed,OpponentAdaptationStage.Play,rules:rules);
    public string Name=>"Adapt";
    public OpponentBeliefs Beliefs=>inner.Beliefs;
    public SearchReport? LastSearch=>inner.LastSearch;
    public Contract? Bid(Observation v,IReadOnlyList<Contract?> legal)=>inner.Bid(v,legal);
    public IReadOnlyList<PlayingCard> Discard(Observation v,int count)=>inner.Discard(v,count);
    public PlayAction Play(Observation v,IReadOnlyList<PlayAction> legal)=>inner.Play(v,legal);
    public IReadOnlyList<PlayAction> DeclareJokers(Observation v)=>inner.DeclareJokers(v);
}
