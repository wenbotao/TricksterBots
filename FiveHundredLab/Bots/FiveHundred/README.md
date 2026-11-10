# 正式四人 Five Hundred bot

八个主要 bot 文件（另有 RandomPass 变体）是策略开发入口：直接阅读和修改对应文件的 `Bid`、`Discard`、`Play` 及其私有方法，即可追踪动作选择。命名空间为 `FiveHundredLab.Bots.FiveHundred`，目标框架为 .NET 8。

| 文件 | 策略主体 |
|---|---|
| RandomBot.cs | 叫牌、弃牌、出牌及 Joker 声明随机选择；保留默认路径的独立随机流 |
| WeakBot.cs | 局部叫牌、弃牌排序、即时出牌；默认策略也在本文件 |
| MediumBot.cs | 规则版确定性决策、完整迁入的原策略、默认规则适配和 Fast 所用可见手牌变体 |
| FastBot.cs | 候选裁剪、预算、搜索比较；默认及配置路径分别保留原行为 |
| StrongBot.cs | 叫牌、弃牌、出牌搜索的候选与选择；配置路径叫牌仍为既有启发式 |
| [StrongPlusBot.cs](StrongPlusBot.cs) | 独立的强度优先策略：32样本、24个弃牌候选、默认路径零墩证明；策略主体保存在本文件 |
| PartnerBot.cs | 公开必破、保护搭档、信号回应、弃牌修补；组合 Fast 处理剩余搜索 |
| [StrongFlowCachedBot.cs](StrongFlowCachedBot.cs) | 完整独立版本：32 样本、弃牌修补、公开 Misère 证明；默认续演采用根决策缓存、直接输入和每墩快照 |

每类均实现 `IPlayer` 和 `IJokerDeclarationPlayer`，构造参数为 `(int seed = 1, Rules? rules = null)`，提供 `FromConfiguration(string? path = null, int seed = 1)`。推荐读取一次规则，传给引擎和所有玩家；配置不匹配会拒绝。

```csharp
var rules = RuleConfiguration.Load("FiveHundredRules/presets/au-reference-4p-v1.json");
var bot = new PartnerBot(seed: 7, rules: rules);
// 或 PartnerBot.FromConfiguration(path, seed: 7)
```

## 文件内导航

非随机 bot 顶部为公开接口，接着是 `BidConfigured` 等配置策略；`DefaultPolicy` 为保留历史行为的默认路径。Medium 的 `DefaultStrategy : BaseBot<FiveHundredOptions>` 完整包含从原 `FiveHundredBot` 迁入的逻辑，`DefaultAdapter` 将 Observation 转成平台输入；它不调用或继承原 `FiveHundredBot`。`VisibilityAdapter` / `VisibilityStrategy` 为 Fast 续演保留历史可见手牌判断。

Fast/Strong/StrongPlus/StrongFlowCached 的 `SelectByRollout` 负责配置路径候选比较，默认路径的搜索逻辑在同文件 `DefaultPolicy`。Partner 的 `ConfiguredPartnerPolicy` 和 `DefaultPartnerPolicy` 包含两条路径的体验判断，再组合本目录的 FastBot。修改 Fast 或 Medium 的共享基准可能影响其调用者，需一起回归。

## 共享设施

- `FiveHundredRuleBot.cs`：绑定规则、校验观察、名称、随机流及续演计数，不再委托 `RuleAwarePlayer`，没有策略等级分支。
- `RuleBotEvaluation.cs`：纯牌力特征、危险度和结算效用；不选择动作。
- `DefaultRollout.cs`：默认规则下的续演执行，由调用者提供 Medium 的策略实现。
- `ConfiguredRollout.cs`：配置规则下的续演执行，使用 Medium 的规则战术作为固定续演策略。
- `Shared/CardFrame.cs`：公开牌信息、牌表与缓存工具；保留类型名以兼容历史实验。
- Arena 的 `HandEvaluator`、`BeliefSampler` / `RuleSamples`：弃牌组合、基础估值和隐藏牌采样。正式 bot 不依赖旧 `RuleTactics` 或 `RuleRollout` 的动作判断。

省略配置与显式加载新格式配置仍使用不同策略路径；本次没有统一算法、改变预算或调参。原七种 bot 的 `LastRollouts` 保留已有语义：配置路径统计最近决策实际续演数，默认路径保持原包装层的 0。StrongFlowCached 默认路径统计实际续演数，并提供 LastDataWork/LastPlayWork 等诊断；不可将旧包装层的 0 当作没有搜索。

## 兼容与开发入口

原 .NET Standard `Trickster.Bots.FiveHundredBot` 继续供旧 WebAPI 和历史基线使用，正式 Medium 已与其策略实现脱离。旧 `RuleAwarePlayer` 及旧 profile 留作 Arena CLI、Adapt 和回归兼容；它们不会自动转用本目录策略。

实际开发应使用本目录公共类，或 `Profiles.CreateForRules`，或 Lab 的 `random-rules-v1` / `weak-rules-v1` / `medium-rules-v1` / `fast-rules-v1` / `strong-rules-v1` / `strong-plus-v1` / `strong-flow-cached-v1` / `partner-rules-v1` 名称。不传规则文件时，这些正式名称使用各自文件中的默认策略。历史的 `strong-v1`、`visibility-fast-v8` 等名称仍指向冻结基线，便于比较。

旧 Legacy 策略已移至 Archive/Strategies，继续编译以兼容历史 profile；迁入前七个包装文件以 `.cs.txt` 非编译快照保存于 Archive/Snapshots。它们不再是正式 bot 的默认策略依赖。

整合记录见 [CONSOLIDATION.md](../../CONSOLIDATION.md)，全局说明见 [开发总结](../../../FIVEHUNDRED_DEVELOPMENT.md)。

## Random 的永远 Pass 变体

`RandomPassBot`（profile：`random-pass-v1`）叫牌始终返回 Pass，不消耗随机数；弃牌、出牌和 Joker 声明组合调用原 RandomBot。支持相同的规则参数和 FromConfiguration 入口。四个此类 bot 同场时将持续全 Pass 重发，直到运行上限，记为截断而非胜负。若传入不允许 Pass 的合法动作列表，明确报错。

## StrongPlus 正式入口

`StrongPlusBot`（profile：`strong-plus-v1`）独立于冻结 Strong：隐藏牌样本32，弃牌候选24；默认路径改选门槛6、标准误系数1.0，使用本目录Medium中的可见手牌变体作基准及续演。默认路径在根节点加入公开信息可证明的Misère躲墩/破约判断。配置路径仍保留原启发式叫牌，仅扩大弃牌/出牌搜索，不将默认的三人Misère证明套用于四人零墩变体。

StrongPlus作为独立正式bot保留，不替代Strong，也不从消融配置动态选择参数。正式整理不表示已确认它在所有搭档或规则下更强。

```csharp
var rules = RuleConfiguration.Load(path); // null：默认规则
var bot = new StrongPlusBot(seed: 7, rules: rules);
// 或 StrongPlusBot.FromConfiguration(path, seed: 7)
// 或 Profiles.CreateForRules("strong-plus-v1", 7, rules)
```

此版本不使用预训练或在线对手学习。配置入口和绑定规则检查与其他正式类相同。验证、行为差异和成对实战记录见[StrongPlus实验](../../../experiments/strong-plus-v1-20261106/README.md)。

## 研究用消融控制（不属于正式bot列表）

`StrongAblationBot`提供固定命名的`strong-study-base8-v1`、`all32`、`wide32`、`tuned32`、`bid32`、`discard32`、`play32`控制（后六者同样使用`strong-study-…-v1`全名）。各配置保留旧策略算法，仅按矩阵改变预算或阈值；基线等价及阶段预算已测试。设计、限制和结果见[消融试验](../../../experiments/strong-ablation-v1-20261106/README.md)。

`StrongStructuredBot`（`strong-structured-v1`）是独立实验策略：沿用 All32 的叫牌、出牌和续演，保持弃牌候选数量，改为覆盖不同留牌结构。它支持同样的规则参数与 `FromConfiguration` 入口，没有替代正式 Strong 或 StrongPlus。第一阶段的独立种子验证、结构实验及限制见[实验报告](../../../experiments/phase-one-v1-20261109/README.md)。

`StrongTacticalBot`（`strong-tactical-v1`）在 All32 基础上增加有节点上限的公开当前墩证明：Misère 必破与避险，以及普通合约关键一墩的保约/破约。默认与配置规则均可用，未知手牌不使用真实发牌信息。它仍为独立实验版本，见[第二阶段报告](../../../experiments/public-tactics-v1-20261109/README.md)。

`StrongProofAblationBot` 提供 `strong-misere-proof-v1`、`strong-contract-proof-v1` 两种独立固定配置，分别启用 Misère 或普通合约证明；构造参数为 `(StrongProofProfile profile, int seed = 1, Rules? rules = null)`，支持 `FromConfiguration(profile, path, seed)`。两层拆分、120 副定向牌的成约/破约结果与限制见[第三阶段报告](../../../experiments/proof-split-v1-20261109/README.md)。它们不自动替换现有策略。

`StrongBidGuardBot`（`strong-bid-guard-v1`）在仅 Misère 版上增加窄范围的弱牌开叫 Pass 判断；`StrongHandGuardBot`（`strong-hand-guard-v1`）累计增加无将弃牌的 K 保护修补，不增加续演次数。两者支持标准规则接口及工厂入口，均为独立实验版本，详见[第四阶段报告](../../../experiments/hand-guards-v1-20261109/README.md)。

`StrongDiscardGuardBot`（`strong-discard-guard-v1`）为保留弃牌修补的独立版本：沿用仅 Misère 证明版的原叫牌和出牌，加入同一无将弃牌修补，去掉强制 Pass。支持 `(seed, rules)`、`FromConfiguration(path, seed)` 与规则工厂入口。128 个既有案例的修补前后选择已核对一致，Fast/Strong 等续演下的条件收益见[续演比较报告](../../../experiments/discard-rollouts-v1-20261109/README.md)。


## 弃牌修补的后续策略与工程版本

`StrongDiscardRefinedBot` 提供五个独立固定配置：`strong-discard-shape-v1`、`strong-discard-audit-v1`、对应的 `strong-discard-shape-efficient-v1` / `strong-discard-audit-efficient-v1`，以及保留旧修补策略的 `strong-discard-guard-efficient-v1`。构造参数为 `(DiscardRefinementProfile profile, int seed = 1, Rules? rules = null)`，支持 `FromConfiguration(profile, path, seed)`。

新样本未证明结构版或选择性复核优于旧补丁，当前推荐工程入口为 `EfficientBaseline`（`strong-discard-guard-efficient-v1`），旧 bot 不变。缓存消除了重复静态估值，但未证明端到端加速。新类的 `LastRollouts` 统计实际搜索与复核，动作开始会清空诊断；此处有别于旧默认包装层的计数语义。实现、条件成约率及 2,452 局实际续演见[本轮报告](../../../experiments/discard-refinement-v1-20261109/README.md)。


`StrongDiscardLeanBot`（`strong-discard-guard-lean-v1`）为 `EfficientBaseline` 的独立工程精简版：移除无用结构计算及全量字符串索引，按需排序，保留相同 Medium 续演、预算和策略。支持 `(seed, rules)` 与 `FromConfiguration(path, seed)`。256 状态、32 次根搜索及默认/六配置对局记录等价，550 项回归通过。少量分配减少尚未转化为可确认的速度收益，见[工程报告](../../../experiments/discard-lean-v1-20261109/README.md)。


`StrongPlayForcedBot`（`strong-play-forced-v1`）以 Lean 为基准，仅在出牌续演内直接执行唯一合法的完整动作，省去观察与 Medium 适配；保留 Joker 多声明、规则上下文、原预算及非强制动作策略。叫牌、弃牌与共享续演器不变。支持 `(seed, rules)`、`FromConfiguration(path, seed)` 与工厂入口；`LastPlayWork` 记录续演内部工作量。555 项测试通过，72 组测量动作/搜索一致并显示局部加速，见[出牌优化第一步](../../../experiments/play-forced-v1-20261109/README.md)。


`StrongPlayCachedBot`（`strong-play-cached-v1`）继续第一步的强制动作快路，并通过每次根搜索独立的 `PlayDataContext` 复用公开竞叫、比分、牌字符串编码及按 Joker 花色分开的参数。策略实例、历史及各世界状态仍独立，默认策略判断与预算不变；显式配置路径沿用第一步。支持标准规则参数、`FromConfiguration` 和工厂入口。`LastDataWork` 提供准备计数。561 项测试和 72 组配对检查通过，小样本分配量约再降 11%–15%，见[重复数据优化报告](../../../experiments/play-cached-v1-20261109/README.md)。


`StrongPlayIncrementalBot`（`strong-play-incremental-v1`）在缓存版上增量维护公共历史摘要，保留暂时赢墩牌与缺门顺序；每个候选/世界的摘要独立，旧接口模型仍按调用新建。默认续演更新，显式配置路径不变。4 项针对性测试及 48 组小规模计量通过，全量回归按用户要求留待统一测试，见[增量历史报告](../../../experiments/play-incremental-v1-20261109/README.md)。

`StrongFlowCachedBot`（`strong-flow-cached-v1`）基于增量版优化默认规则下的全流程数据准备。策略逻辑仍在 bot 文件中；`FlowPolicyCache` 负责根决策内的 Medium 策略实例/派生缓存及输入适配，`FlowCachedRollout` 负责分支隔离和每墩只读快照。规则配置路径保留原实现，详见[工程优化报告](../../../experiments/flow-cached-v1-20261110/README.md)。

## StrongFlowCached 正式版本

`StrongFlowCachedBot` 现作为完整正式版本保留，profile 为 `strong-flow-cached-v1`。标准构造、FromConfiguration、规则绑定、Joker 声明接口与其余正式 bot 一致；它不自动替换 Strong 或 StrongPlus。

```csharp
var rules = RuleConfiguration.Load(path);
var bot = new StrongFlowCachedBot(seed: 7, rules: rules);
// 或 StrongFlowCachedBot.FromConfiguration(path, seed: 7)
// 或 Profiles.CreateForRules("strong-flow-cached-v1", 7, rules)
```

策略判断集中于本文件；`FlowPolicyCache` 与 `FlowCachedRollout` 负责输入适配及分支执行。此版本不是 StrongPlus 的逐动作等价副本：弃牌宽度、证据门槛和基准适配仍有差异。公开状态中不读隐藏牌、发牌种子或审计元数据；无预训练和在线学习。显式配置路径保留原配置策略，默认路径的缓存收益不外推到全部变体。

九个已完成版本的策略、体验、成本与历史实战证据见[整体比较报告](../../../FIVEHUNDRED_BOT_COMPARISON.md)。源码与支持文件按 frozen-profiles-v22.json 固定，以后的策略或参数变化使用新命名。
