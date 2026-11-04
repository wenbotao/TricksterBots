using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab.Usability;

public enum UxLevelV1 { Critical, Advisory, Control }
public sealed record UxActionV1(Contract? Bid = null, PlayingCard[]? Discards = null, PlayAction? Play = null);
public sealed record UxCheckV1(string Id, string Expectation, UxLevelV1 Level, Func<Observation, UxActionV1, bool> Accept);
public sealed record UxScenarioV1(string Id, string Title, string Phase, int Dealer,
    IReadOnlyDictionary<int, PlayingCard[]> ReservedHands, PlayingCard[]? Kitty,
    IReadOnlyList<Decision> Auction, IReadOnlyList<PublicPlay> Plays, IReadOnlyList<UxCheckV1> Checks,
    int[]? Scores = null, string? Convention = null);
public sealed record CapturedUxV1(UxScenarioV1 Scenario, Observation View, IReadOnlyList<Contract?> LegalBids,
    IReadOnlyList<PlayAction> LegalPlays, int DiscardCount, PlayingCard[][] InitialHands, PlayingCard[] Kitty);

public static class UxFixturesV1
{
    public static PlayingCard[] Cards(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => PlayingCard.FromBotCard(new Card(t))).ToArray();
    private static Contract Normal(int n, Suit s) => new(n, s);
    private static readonly Contract Misere = new(0, Suit.Unknown, ContractKind.Misere);
    private static readonly Contract Open = new(0, Suit.Unknown, ContractKind.OpenMisere);
    private static Decision B(int s, Contract? c = null) => new(s, "bid", Bid:c);
    private static PublicPlay P(int s, string card, Suit named = Suit.Unknown) => new(s, new(Cards(card).Single(), named));
    private static Dictionary<int, PlayingCard[]> Hands(string own, params (int Seat, string Cards)[] others)
    { var r = new Dictionary<int, PlayingCard[]> { [0] = Cards(own) }; foreach(var p in others) r[p.Seat] = Cards(p.Cards); return r; }
    private static Decision[] OwnContract(Contract c) => [B(0,c), B(1), B(2), B(3)];
    private static Decision[] OwnMisere() => [B(2,Normal(7,Suit.Spades)),B(3),B(0,Misere),B(1),B(2)];
    private static Decision[] OpponentMisere(bool open=false) => [B(2,Normal(open?8:7,Suit.Spades)),B(3),B(0),B(1,open?Open:Misere),B(2)];
    private static UxCheckV1 Check(string id,string text,UxLevelV1 level,Func<Observation,UxActionV1,bool> accept) => new(id,text,level,accept);
    public static IReadOnlyList<UxScenarioV1> Scenarios()
    {
        var list = new List<UxScenarioV1>();
        list.Add(new("bid-weak-opening","无压力、无控制牌时避免高叫","bid",3,
            Hands("5S 6S 7S 5C 6C 7C 4D 5D 4H 5H"),null,[],[],[
                Check("no-high-contract","不以这手牌主动叫七墩以上或 Misère",UxLevelV1.Critical,(_,a)=>a.Bid==null||(!a.Bid.IsMisere&&a.Bid.Tricks==6)),
                Check("prefer-pass","保守体验约定：低牌均型、零分无压力时 Pass",UxLevelV1.Advisory,(_,a)=>a.Bid==null)]));
        list.Add(new("bid-partner-passed","搭档已 Pass，对手高叫时不盲目争抢","bid",1,
            Hands("8S 9S TS 8C 9C TC 9D TD 9H TH"),null,[B(2),B(3,Normal(8,Suit.Hearts))],[],[
                Check("respect-warning","缺少控制且不适合 Misère，期望 Pass；属于保守竞叫偏好",UxLevelV1.Advisory,(_,a)=>a.Bid==null)]));
        list.Add(new("bid-support-human-suit","强红桃配合人类明确的红桃叫牌","bid",1,
            Hands("HJ JH JD AH KH QH TH 9H AC AD"),null,[B(2,Normal(6,Suit.Hearts)),B(3,Normal(6,Suit.Unknown))],[],[
                Check("support-suit","约定测试：选择红桃合约支持搭档，不改叫其他花色",UxLevelV1.Advisory,(_,a)=>a.Bid is {IsMisere:false,Trump:Suit.Hearts})],Convention:"公开叫牌表示对该花色的支持需求；不是强制规则"));
        list.Add(new("bid-partner-can-exit","对手均退出且搭档合约可出局，不无谓加叫","bid",1,
            Hands("HJ JH JD AH KH QH TH 9H AC AD"),null,
            [B(2,Normal(6,Suit.Hearts)),B(3),B(0,Normal(6,Suit.Unknown)),B(1),B(2,Normal(7,Suit.Hearts))],[],[
                Check("leave-contract","搭档已足够出局且双方配合明显，期望 Pass",UxLevelV1.Advisory,(_,a)=>a.Bid==null)],Scores:[480,0]));
        list.Add(new("play-protect-human-winner","人类用绝对大牌拿墩时，不白白跟掉 K","play",1,
            Hands("KH 4H 6C 4D 5D 6D 5S 6S 7S 8C",(2,"HJ AH")),null,
            [B(2,Normal(6,Suit.Unknown)),B(3),B(0),B(1)],
            [P(2,"HJ",Suit.Clubs),P(3,"5C"),P(0,"6C"),P(1,"7C"),P(2,"AH"),P(3,"5H")],[
                Check("save-stopper","Joker 已出、人类 A 稳赢，保留 K，跟红桃 4",UxLevelV1.Critical,(_,a)=>a.Play!.Card==Cards("4H")[0])]));
        list.Add(new("play-avoid-unneeded-ruff","搭档已稳拿当前墩，不轻易花 Joker 将吃","play",0,
            Hands("HJ 4H 5H 5C 6C 7C 4D 5D 6D 7D"),null,
            [B(1,Normal(6,Suit.Hearts)),B(2),B(3),B(0)],
            [P(1,"5S"),P(2,"AS"),P(3,"6S")],[
                Check("conserve-trump","优先垫副牌；接管首引可能有合理性，所以仅作体验提示",UxLevelV1.Advisory,(v,a)=>v.Rules.EffectiveSuit(a.Play!.Card,v.Contract!)!=Suit.Hearts)]));
        list.Add(new("signal-low-discard","人类垫低梅花示意后，回引梅花","play",3,
            Hands("AS HJ 7C 8C 9C JC QC 5H 6H 5D"),null,OwnContract(Normal(6,Suit.Unknown)),
            [P(0,"AS"),P(1,"5S"),P(2,"5C"),P(3,"6S")],[
                Check("return-requested-suit","按低牌垫牌示意约定，回引梅花（Joker 指定梅花也接受）",UxLevelV1.Advisory,(v,a)=>(a.Play!.Card.IsJoker?a.Play.JokerLeadSuit:v.Rules.EffectiveSuit(a.Play.Card,v.Contract!))==Suit.Clubs)],Convention:"本测试约定：缺门时垫低梅花请求回引梅花；不是全体牌手通用约定"));
        list.Add(new("signal-known-void","利用人类已经公开的缺门，为其提供将吃机会","play",1,
            Hands("8C 9C AD 5S 6S 7S 4D 8D 9D TD",(2,"4H JH AH KH QH 8H 9H TH 5D 8S"),(3,"HJ JD 5H 6H 7H 5C 6D TC")),null,
            [B(2,Normal(6,Suit.Hearts)),B(3,Normal(7,Suit.Hearts)),B(0),B(1),B(2)],
            [P(3,"5C"),P(0,"8C"),P(1,"6C"),P(2,"4H"),P(2,"5D"),P(3,"6D"),P(0,"AD"),P(1,"7D")],[
                Check("offer-ruff","优先引梅花利用搭档缺门；抽将和控制等理由可构成例外",UxLevelV1.Advisory,(v,a)=>v.Rules.EffectiveSuit(a.Play!.Card,v.Contract!)==Suit.Clubs)]));
        list.Add(new("misere-own-lead","自己打 Misère，避免首引必拿的 A","play",1,
            Hands("AS 5S AH 4H AD 4D AC 5C 6C 7C"),Cards("HJ KS KH"),OwnMisere(),[],[
                Check("no-certain-self-loss","Joker 已埋，存在可躲的低牌，不首引保证自己取墩的牌",UxLevelV1.Critical,(v,a)=>!GuaranteedLeadWin(v,a.Play!))]));
        list.Add(new("misere-own-duck","自己打 Misère，有低牌可躲时不主动吃墩","play",1,
            Hands("5C 4H AH 5S 6S 7S 4D 5D 6D 7C"),null,OwnMisere(),
            [P(0,"5C"),P(1,"9C"),P(3,"8C"),P(1,"9H"),P(3,"JH")],[
                Check("duck-available","最后一家存在不拿墩的合法选择时，必须躲过",UxLevelV1.Critical,(v,a)=>v.Rules.Winner(v.Trick.Append(new PublicPlay(0,a.Play!)).ToArray(),v.Contract!)!=0)]));
        list.Add(new("misere-forced-high-control","被迫跟唯一的 A 时不能误报为失误","play",1,
            Hands("5C AH 5S 6S 7S 4D 5D 6D 7C 8D"),null,OwnMisere(),
            [P(0,"5C"),P(1,"9C"),P(3,"8C"),P(1,"9H"),P(3,"TH")],[
                Check("forced-is-not-error","唯一合法牌就是 A；接受该强制动作，不因取墩判错",UxLevelV1.Control,(_,a)=>a.Play!.Card==Cards("AH")[0])]));
        list.Add(new("misere-defence-dont-rescue","对手 Misère 正在拿墩，不能反过来救他","play",1,
            Hands("4H 9H 5S 6S 7S 5C 6C 7C 4D 5D",(2,"5H")),null,OpponentMisere(),
            [P(1,"8H"),P(2,"5H")],[
                Check("cash-bust","可直接让庄家取墩时选红桃 4，不用 9 覆盖庄家",UxLevelV1.Critical,(v,a)=>v.Rules.Winner(v.Trick.Append(new PublicPlay(0,a.Play!)).ToArray(),v.Contract!)==1)]));
        list.Add(new("misere-defence-unload","人类已经替防守方拿墩时，趁机处理危险高牌","play",1,
            Hands("4H JH 5S 6S 7S 5C 6C 7C 4D 5D",(2,"9H")),null,OpponentMisere(),
            [P(1,"5H"),P(2,"9H")],[
                Check("unload-high","期望处理 J 而非留着它；首引权取舍可能使其他动作合理",UxLevelV1.Advisory,(_,a)=>a.Play!.Card==Cards("JH")[0])]));
        list.Add(new("open-misere-forced-bust","面对公开 Misère，及时兑现可证明的必破机会","play",1,
            Hands("AC HJ 4D 5H KC QC 8C 9C 5S 6S",(1,"5C 8H 9H TH JH QH KH AH 9D TD"),(2,"6C")),null,OpponentMisere(true),
            [P(1,"5C"),P(2,"6C"),P(0,"AC")],[
                Check("take-public-certainty","引红桃 5 能公开证明庄家本墩必拿；不为同样可能最终获胜而拖延",UxLevelV1.Critical,(v,a)=>ForcesMisereBust(v,a.Play!))]));
        list.Add(new("discard-misere-joker","Misère 拿到底牌 Joker 后必须丢掉","discard",1,
            Hands("5S 6S 7S 5C 6C 7C 4D 5D 4H 5H"),Cards("HJ AS AH"),OwnMisere(),[],[
                Check("bury-joker","保留 Joker 必须打出且必取一墩；应将其埋掉",UxLevelV1.Critical,(_,a)=>a.Discards!.Any(c=>c.IsJoker))]));
        list.Add(new("discard-normal-joker","正常合约存在低副牌可弃时保留 Joker","discard",3,
            Hands("HJ JS JC AS KS QS 9S 8S 4D 5D"),Cards("4H 5H 6H"),OwnContract(Normal(6,Suit.Spades)),[],[
                Check("retain-joker","存在足够低副牌时保留最高将牌",UxLevelV1.Advisory,(_,a)=>!a.Discards!.Any(c=>c.IsJoker))]));
        list.Add(new("discard-nt-stopper","无将弃牌避免拆成没有保护的单张 K","discard",3,
            Hands("KS 5S AC QC 8C 8D 9D AH 8H 9H"),Cards("4D 4H 5C"),OwnContract(Normal(6,Suit.Unknown)),[],[
                Check("keep-king-guard","若保留黑桃 K，期望也保留其保护牌 5；整体取舍可构成例外",UxLevelV1.Advisory,(_,a)=>!a.Discards!.Contains(Cards("5S")[0])||a.Discards!.Contains(Cards("KS")[0]))]));
        return list;
    }

    public static bool GuaranteedLeadWin(Observation v,PlayAction play)
    {
        if(play.Card.IsJoker)return true;
        var unavailable=v.Hand.Concat(v.OwnDiscards).Concat(v.History.Select(p=>p.Action.Card)).ToHashSet();
        return !v.Rules.Deck().Except(unavailable).Any(c=>c.IsJoker||(c.Suit==play.Card.Suit&&c.Rank>play.Card.Rank));
    }
    public static bool ForcesMisereBust(Observation v,PlayAction lead)
    {
        if(v.Trick.Count!=0||v.Declarer!=1||v.Players[1].ExposedHand==null)return false;
        var exposed=v.Players[1].ExposedHand!;var prefix=new[]{new PublicPlay(0,lead)};
        var replies=v.Rules.LegalPlays(exposed,prefix,v.Contract!);
        var unavailable=v.Hand.Concat(exposed).Concat(v.OwnDiscards).Concat(v.History.Select(p=>p.Action.Card)).ToHashSet();
        var potential=v.Rules.Deck().Except(unavailable).ToArray();
        return replies.Count>0&&potential.Length>0&&replies.All(reply=>potential.All(card=>v.Rules.Winner(new[]{prefix[0],new PublicPlay(1,reply),new PublicPlay(2,new(card))},v.Contract!)==1));
    }
}
