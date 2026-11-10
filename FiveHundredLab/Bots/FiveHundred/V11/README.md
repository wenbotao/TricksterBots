# 当前 v1.1 九个 Bot

此目录的 Random、RandomPass、Weak、Medium、Fast、Strong、Partner、StrongPlus、StrongFlowCached 是对应已完成策略的规则迁移版本。标准构造 `(int seed = 1, Rules? rules = null)` 与 `FromConfiguration(path, seed)` 保留，省略规则时使用新的澳式 v1.1 默认；原父目录同名类保留作历史版本。

```csharp
using FiveHundredArena;
using FiveHundredLab.Bots.FiveHundred.V11;
var rules = RuleConfiguration.Load();
IPlayer[] players = [new StrongFlowCachedBot(1, rules), new PartnerBot(2, rules),
                    new MediumBot(3, rules), new FastBot(4, rules)];
var game = new Engine(rules).Run(players, seed: 123);
// Profiles.CreateForRules("strong-flow-cached-v1.1", 1, rules) 也可。
```

名字为 random-v1.1、random-pass-v1.1、weak-v1.1、medium-v1.1、fast-v1.1、strong-v1.1、partner-v1.1、strong-plus-v1.1、strong-flow-cached-v1.1。显式传旧规则时仍可使用旧流程；需要复现历史默认，请明确使用 `new Rules()`，而不是依赖已经切换为新默认的 `RuleConfiguration.Load()`。

每个文件保留自己的策略判断。Strong/Plus/Flow 的新默认叫牌保持隐藏世界搜索，使用相同世界比较候选；弃牌/出牌也保留各自预算与置信门槛。Medium 使用规则感知启发式；新默认不再通过原平台模型假设固定 Joker 规则。因规则及适配路径都改变，新旧原始胜率不能作为单独规则效果的消融比较。

- `FiveHundredRuleBot` 负责规则绑定、当前版本名称及新申请/声明/交牌接口的保守默认。
- `VariantSamples` 只用公开信息和本人信息分配隐藏牌，识别所有持有者的公开 Joker 提名、Granola 和 Solo 的手数/死牌数量。
- `VariantRollout` 执行竞叫、取底交换、明牌、三/四人参与、跟牌和实际计分；固定 Medium 作为续演行为，不进行对手在线学习。
- `VariantFlowData` 在一次根决策内缓存契约/公开提名固定时的花色与大小派生牌表，以及分支内历史快照。缓存与未缓存续演的动作/效用有对应测试。
- `RuleBotEvaluation` 使用当前规则的效用与终局判断，不把双人零墩视作“只要定约者不拿墩”。

IRuleVariationPlayer 提供 RequestRedeal、DeclareGrandSlam、TransferCard。Weak 默认不主动声明独战，其他非随机策略使用保守的 Joker/控制与牌力门槛；Random/RandomPass 对新选择保持随机。RandomPass 在规则强制庄家叫牌时选择最低合法叫牌，而不是返回非法 Pass，旧版严格永远 Pass 的策略仍保留在父目录。

Grand Nullo 中双方均避墩、对手寻求让任一定约队成员取墩；Grand Slam 只算个人十墩，队友交一张后退出。对公开必破的证明不跨用到错误的四人/队伍目标。Partner仍额外保留搭档信号和保护策略。

[规则及组合约束](../../../../FiveHundredRules/v1.1/README.md) · [新默认实战、成本与体验报告](../../../../experiments/rules-v1-1-20261110/README.md)
