# 正式四人 Five Hundred bot

六个文件现在是策略开发入口：直接阅读和修改对应文件的 `Bid`、`Discard`、`Play` 及其私有方法，即可追踪动作选择。命名空间为 `FiveHundredLab.Bots.FiveHundred`，目标框架为 .NET 8。

| 文件 | 策略主体 |
|---|---|
| RandomBot.cs | 叫牌、弃牌、出牌及 Joker 声明随机选择；保留默认路径的独立随机流 |
| WeakBot.cs | 局部叫牌、弃牌排序、即时出牌；默认策略也在本文件 |
| MediumBot.cs | 规则版确定性决策、完整迁入的原策略、默认规则适配和 Fast 所用可见手牌变体 |
| FastBot.cs | 候选裁剪、预算、搜索比较；默认及配置路径分别保留原行为 |
| StrongBot.cs | 叫牌、弃牌、出牌搜索的候选与选择；配置路径叫牌仍为既有启发式 |
| PartnerBot.cs | 公开必破、保护搭档、信号回应、弃牌修补；组合 Fast 处理剩余搜索 |

每类均实现 `IPlayer` 和 `IJokerDeclarationPlayer`，构造参数为 `(int seed = 1, Rules? rules = null)`，提供 `FromConfiguration(string? path = null, int seed = 1)`。推荐读取一次规则，传给引擎和所有玩家；配置不匹配会拒绝。

```csharp
var rules = RuleConfiguration.Load("FiveHundredRules/presets/au-reference-4p-v1.json");
var bot = new PartnerBot(seed: 7, rules: rules);
// 或 PartnerBot.FromConfiguration(path, seed: 7)
```

## 文件内导航

非随机 bot 顶部为公开接口，接着是 `BidConfigured` 等配置策略；`DefaultPolicy` 为保留历史行为的默认路径。Medium 的 `DefaultStrategy : BaseBot<FiveHundredOptions>` 完整包含从原 `FiveHundredBot` 迁入的逻辑，`DefaultAdapter` 将 Observation 转成平台输入；它不调用或继承原 `FiveHundredBot`。`VisibilityAdapter` / `VisibilityStrategy` 为 Fast 续演保留历史可见手牌判断。

Fast/Strong 的 `SelectByRollout` 负责配置路径候选比较，默认路径的搜索逻辑在同文件 `DefaultPolicy`。Partner 的 `ConfiguredPartnerPolicy` 和 `DefaultPartnerPolicy` 包含两条路径的体验判断，再组合本目录的 FastBot。修改 Fast 或 Medium 的共享基准可能影响其调用者，需一起回归。

## 共享设施

- `FiveHundredRuleBot.cs`：绑定规则、校验观察、名称、随机流及续演计数，不再委托 `RuleAwarePlayer`，没有策略等级分支。
- `RuleBotEvaluation.cs`：纯牌力特征、危险度和结算效用；不选择动作。
- `DefaultRollout.cs`：默认规则下的续演执行，由调用者提供 Medium 的策略实现。
- `ConfiguredRollout.cs`：配置规则下的续演执行，使用 Medium 的规则战术作为固定续演策略。
- `Shared/CardFrame.cs`：公开牌信息、牌表与缓存工具；保留类型名以兼容历史实验。
- Arena 的 `HandEvaluator`、`BeliefSampler` / `RuleSamples`：弃牌组合、基础估值和隐藏牌采样。正式 bot 不依赖旧 `RuleTactics` 或 `RuleRollout` 的动作判断。

省略配置与显式加载新格式配置仍使用不同策略路径；本次没有统一算法、改变预算或调参。`LastRollouts` 保留已有语义：配置路径统计最近决策实际续演数，默认路径保持原包装层的 0。

## 兼容与开发入口

原 .NET Standard `Trickster.Bots.FiveHundredBot` 继续供旧 WebAPI 和历史基线使用，正式 Medium 已与其策略实现脱离。旧 `RuleAwarePlayer` 及旧 profile 留作 Arena CLI、Adapt 和回归兼容；它们不会自动转用本目录策略。

实际开发应使用本目录公共类，或 `Profiles.CreateForRules`，或 Lab 的 `random-rules-v1` / `weak-rules-v1` / `medium-rules-v1` / `fast-rules-v1` / `strong-rules-v1` / `partner-rules-v1` 名称。不传规则文件时，这些正式名称也使用迁入的默认策略。历史的 `strong-v1`、`visibility-fast-v8` 等名称仍指向冻结基线，便于比较。

旧 Legacy 策略已移至 Archive/Strategies，继续编译以兼容历史 profile；迁入前七个包装文件以 `.cs.txt` 非编译快照保存于 Archive/Snapshots。它们不再是正式 bot 的默认策略依赖。

整合记录见 [CONSOLIDATION.md](../../CONSOLIDATION.md)，全局说明见 [开发总结](../../../FIVEHUNDRED_DEVELOPMENT.md)。

## Random 的永远 Pass 变体

`RandomPassBot`（profile：`random-pass-v1`）叫牌始终返回 Pass，不消耗随机数；弃牌、出牌和 Joker 声明组合调用原 RandomBot。支持相同的规则参数和 FromConfiguration 入口。四个此类 bot 同场时将持续全 Pass 重发，直到运行上限，记为截断而非胜负。若传入不允许 Pass 的合法动作列表，明确报错。
