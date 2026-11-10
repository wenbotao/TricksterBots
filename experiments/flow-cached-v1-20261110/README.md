# 全流程缓存优化 v1

新增 `StrongFlowCachedBot`（`strong-flow-cached-v1`），基于 `strong-play-incremental-v1`。历史 bot 和策略支持文件保持冻结。

同局面动作和搜索结果全部保持一致。新缓存相对增量版的配对耗时几何比：叫牌 0.573，弃牌 0.518，前期 0.736，中期 0.707。小于 1 表示加速；这是默认规则固定语料的阶段成本结果。

## 实现

- 根决策内按将牌、Joker 领牌声明复用同一个 Medium.DefaultStrategy；规则由缓存生命周期绑定，复用 BaseBot 的牌堆、花色及最高牌派生缓存。每次根决策新建缓存并替换上一份，不跨 bot 或线程共享。
- 叫牌、弃牌的续演统一使用该缓存。唯一合法的完整动作直接执行，跳过无随机性、无学习状态的 Medium 调用；保留 Joker 声明动作。
- 出牌续演直接从分支状态构造策略输入，省去中间 Observation/PublicPlayer 和多层数组副本；分支拥有独立手牌、缺门、历史、得墩和明牌状态。
- 缺门按实际规则增量维护；已完成各墩的牌在每墩只构造一份只读快照，各分支不共享可写列表；叫牌历史每分支编码一次。
- 审查 DefaultStrategy 及其 BaseBot 出牌调用链后，省略未被消费的 PlayedCards 与 cardsPlayedInOrder 模型。保留已完成牌、缺门和所有参与决策的公开字段。此项依赖当前策略读取范围；未来若策略开始读取这些字段，须补回构造并更新对照测试。
- 采样、候选顺序、32 世界预算、弃牌修补、评分、置信门槛、Misère 证明及规则接口保持原样。显式 ConfiguredRules 路径目前不使用新缓存。

## 验证

全量回归 569/569 通过，92 个既有源码/模型冻结文件校验通过。4 项专项测试覆盖逐步续演轨迹、收益、分支隔离、Joker、Misère/Open Misère、两组传统规则参数，以及默认/澳式/美式配置的引擎对照和权威回放。

固定成本语料中 144 个公开状态、每状态 3 次，共 432 组，新增版与增量基线的动作、搜索报告、续演预算及战术/弃牌修补诊断全部一致；每版重复运行确定。未新增胜率比赛，本轮结果支持工程行为保持，不作为一般意义的强度等价证明。

## 成本

沿用上一轮完整 144 状态语料，六阶段各 24 个。三个版本在同一次无并发对局的串行运行中重新测量；不比较历史绝对毫秒数。固定种子与状态打乱顺序、轮换版本执行顺序，实例创建在计量区外，根决策内缓存建立包含在计量内。各状态三次取中位数，再按来源发牌组等权汇总对数比并做 20,000 次组级 bootstrap。区间为探索性、未做多重比较校正。

| 阶段 | 耗时中位数 ms：增量 / 新缓存 / Plus | 新缓存/增量耗时比（95%） | 新缓存/Plus 耗时比（95%） | 分配 MB：增量 / 新缓存 / Plus |
| --- | ---: | --- | --- | ---: |
| 叫牌 | 291.021 / 167.258 / 175.231 | 0.573（0.561～0.585） | 0.952（0.935～0.971） | 564.668 / 318.983 / 300.466 |
| 弃牌 | 534.874 / 274.781 / 577.355 | 0.518（0.508～0.531） | 0.471（0.461～0.482） | 1055.239 / 564.799 / 1049.416 |
| 前期出牌 | 208.044 / 152.556 / 181.851 | 0.736（0.726～0.745） | 0.846（0.830～0.863） | 418.912 / 303.498 / 312.070 |
| 中期出牌 | 77.716 / 54.760 / 76.528 | 0.707（0.693～0.725） | 0.727（0.699～0.753） | 156.256 / 105.290 / 127.326 |
| 后期出牌 | 9.268 / 5.727 / 13.687 | 0.649（0.591～0.722） | 0.284（0.258～0.316） | 18.927 / 11.476 / 22.380 |
| 唯一合法出牌 | 0.000 / 0.000 / 0.009 | 不推断微秒比率 | 不推断微秒比率 | 0.000 / 0.000 / 0.009 |

分配量表示累计对象分配，并非峰值内存。比值是配对且来源组等权的几何平均，不是表中两列中位数直接相除。分层等额语料不代表自然阶段频率，不汇总为整场加速。普通合约的成本不外推到 Misère；已复用语料不作为独立的新场景强度验证。

## 尚未覆盖的优化

本轮优化默认规则的全流程。显式 ConfiguredRules 的续演状态构造保留原样；其历史、Joker 声明和明牌状态具有规则相关的可变语义，需要独立设计缓存与失效条件。Medium 的传统叫牌评估内仍有逐次牌堆分组，默认可见手牌检查仍会解析手牌字符串；这些是后续可剖析的重复计算，本轮没有改动旧策略源码。

## 源码依赖与使用

`Profiles → StrongFlowCachedBot → FlowPolicyCache / FlowCachedRollout → MediumBot.DefaultStrategy → BaseBot`。隐藏世界仍由 BeliefSampler 产生，规则执行与计分仍由 Rules/ConfiguredRules 决定，真实游戏仍由 Engine 校验。

```csharp
var bot = new StrongFlowCachedBot(seed: 7);
var configuredBot = StrongFlowCachedBot.FromConfiguration("FiveHundredRules/presets/au-reference-4p-v1.json", seed: 7);
// 或 Profiles.CreateForRules("strong-flow-cached-v1", 7, rules)
```

## 复现与原始数据

```sh
dotnet test TestBots -c Release --logger "trx;LogFileName=full-final.trx" --results-directory /tmp/flow-cache-validation
dotnet run --project experiments/flow-cached-v1-20261110 -c Release
python3 experiments/flow-cached-v1-20261110/analyze.py
```

请串行运行测试与计量；运行前备份本目录 results。原始 [measurements.json](results/measurements.json)、[registration.json](results/registration.json)、[validation.json](results/validation.json) 和 [summary.json](summary.json) 保留逐状态数据及源码/输入哈希。来源语料见 [上一轮扩大测试](../incremental-vs-strongplus-expanded-20261110/README.md)。
