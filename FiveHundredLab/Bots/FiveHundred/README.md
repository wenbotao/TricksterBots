# 正式四人 Five Hundred bot

命名空间：`FiveHundredLab.Bots.FiveHundred`，目标框架.NET 8。原fork的.NET Standard `Trickster.Bots.FiveHundredBot`保留，避免破坏WebAPI和旧调用方；这里是带完整规则接口的正式入口，不强行向旧项目添加循环依赖。

| 类 | 策略 |
|---|---|
| RandomBot | 在规则允许的动作中随机选择 |
| WeakBot | 无搜索的基础局部策略 |
| MediumBot | 规则驱动的确定性估值与战术 |
| FastBot | 小候选集、四样本续演 |
| StrongBot | 更多候选、八样本续演 |
| PartnerBot | Fast预算加搭档体验优先判断 |

各类均实现`IPlayer`和`IJokerDeclarationPlayer`，构造参数为`(int seed = 1, Rules? rules = null)`，并提供`FromConfiguration(string? path = null, int seed = 1)`。推荐读取一次规则，交给引擎和所有玩家；配置不匹配会拒绝。

```csharp
var rules = RuleConfiguration.Load("FiveHundredRules/presets/au-reference-4p-v1.json");
var bot = new PartnerBot(seed: 7, rules: rules);
// 或 PartnerBot.FromConfiguration(path, seed: 7)
```

`FiveHundredRuleBot`集中处理规则绑定与标准接口，策略算法共用Arena的`Bots/FiveHundred`核心；每类使用不同的策略层级。省略规则时委托Legacy/旧策略，维持历史默认行为。`LastRollouts`提供最近一次搜索的实际续演数。

新接口保留旧profile名称和规则版名称；历史的`RuleAwarePlayer`也继续可用。不要因类名整理而把本轮当作新一轮强度调参。
