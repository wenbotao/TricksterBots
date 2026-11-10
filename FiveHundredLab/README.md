# FiveHundred 策略与验证入口

> 当前规则与 bot 推荐入口更新为 [v1.1 九版](Bots/FiveHundred/V11/README.md)，新默认实战及成本比较见 [迁移报告](../experiments/rules-v1-1-20261110/README.md)。下方旧实验按其产生时的规则版本理解。

## 正式 bot

七种主要规则版 bot 集中在 [Bots/FiveHundred](Bots/FiveHundred/README.md)：Random、Weak、Medium、Fast、Strong、StrongPlus、Partner。每种均有独立公共类、规则对象参数和配置文件入口；工厂`Profiles.CreateForRules`返回这些正式类。

第一阶段后续研究保留为独立实验：搜索预算复验与 `strong-structured-v1` 的弃牌结构覆盖，见[第一阶段报告](../experiments/phase-one-v1-20261109/README.md)；原正式策略不变。

第二阶段新增 `strong-tactical-v1`，在 All32 上独立验证公开信息下的必破、保约与零墩避险判断，见[第二阶段报告](../experiments/public-tactics-v1-20261109/README.md)。

第三阶段将证明层拆为 `strong-misere-proof-v1` / `strong-contract-proof-v1`，并进行固定合约的庄家/防守定向测试；这些结果是单副成约率，不是比赛胜率，见[第三阶段报告](../experiments/proof-split-v1-20261109/README.md)。

第四阶段新增 `strong-bid-guard-v1`、`strong-hand-guard-v1`，分别增量验证弱牌开叫和无将挡张修补，旧版本保留，见[第四阶段报告](../experiments/hand-guards-v1-20261109/README.md)。

第四阶段的扩大收益验证包含 256 场成对比赛与 4,096 副独立暗牌条件续演；弃牌修补显示条件收益，强制 Pass 未显示胜率收益，不自动推广综合版，见[扩大样本报告](../experiments/guard-benefits-v1-20261109/README.md)。

保留弃牌修补的独立入口为 `StrongDiscardGuardBot` / `strong-discard-guard-v1`，恢复基准叫牌，并用同一手牌和暗牌追加 Fast/Strong 续演，与既有 Medium/Weak 比较，见[续演比较报告](../experiments/discard-rollouts-v1-20261109/README.md)。

```csharp
var rules = RuleConfiguration.Load(path); // null：原默认规则
var partner = new FiveHundredLab.Bots.FiveHundred.PartnerBot(seed: 1, rules: rules);
var game = new Engine(rules).Run(players, seed: 123);
```

规则、支持范围和约束见 [FiveHundredRules](../FiveHundredRules/README.md)。省略规则时使用各 bot 文件中的默认策略；原六个基线保持历史等价，StrongPlus使用其独立增强策略。显式配置时采用规则版；变体下Strong的叫牌仍是既有规则版启发式，没有在本轮偷偷替换策略。

## 常用验证

```sh
dotnet build TricksterBots.sln -c Release
dotnet test TestBots -c Release --no-build
dotnet run --project FiveHundredLab -c Release -- bench-mixed \
 --a partner-rules-v1 --b medium-rules-v1 \
 --rules FiveHundredRules/presets/legacy-au-4p-v1.json \
 --pairs 2 --out /tmp/fivehundred-check-new
dotnet run --project FiveHundredLab -c Release -- verify-frozen --root .
```

体验测试见 [Usability](Usability/README.md)。正式策略行为快照位于`TestBots/RuleBotDecisionSnapshots.json`，覆盖原六种策略×六份规则；StrongPlus另有`TestStrongPlus`的配置、确定性及重放验证。

## 历史与停用实验

Adapt已搁置；预训练和中间迭代不作为默认推荐。源码、模型、实验数据和原报告归入 [Archive](Archive/README.md)，保留历史profile和回归对照所需的编译兼容。默认策略已迁入正式 bot 文件；旧 Legacy 实现移入 Archive/Strategies 作为历史对照。历史数据不再散放于主目录。

整理与效率优化记录见 [MAINTENANCE.md](MAINTENANCE.md)。

开发全貌、规则执行与源码依赖见[开发过程与成果总结](../FIVEHUNDRED_DEVELOPMENT.md)。

本轮策略主体整合与依赖说明见 [CONSOLIDATION.md](CONSOLIDATION.md)。

Random 的始终 Pass 变体为 `random-pass-v1`；[与 Weak 的50场随机实验](../experiments/randompass-weak-50-20261105/README.md)包含完整报告和复核数据。

正式独立bot StrongPlus（`strong-plus-v1`）与Fast、Strong位于同一目录，保留旧 Strong 不变；[实现、行为差异及成对实战报告](../experiments/strong-plus-v1-20261106/REPORT_ZH.md)。

Strong 的独立消融与阶段预算试验按用户要求提前停止，保存136场：[结果与优化方向](../experiments/strong-ablation-v1-20261106/REPORT_ZH.md)。


弃牌修补后续完成结构保护、选择性复核与工程缓存的独立实现。新策略没有可靠增益，当前保留旧补丁策略并提供行为等价的 `strong-discard-guard-efficient-v1`；完整结果与限制见[后续优化报告](../experiments/discard-refinement-v1-20261109/README.md)。


后续工程精简入口为 `strong-discard-guard-lean-v1` / `StrongDiscardLeanBot`。实际决策与选定旧补丁版保持回归等价，不改变内部续演策略；未证明整体加速，见[工程精简与执行验证](../experiments/discard-lean-v1-20261109/README.md)。


工程精简后的自然发牌实战使用 32 组相同发牌与阵容，分别运行未修补基准、保留补丁版和精简版，记录逐动作遥测及完整牌谱，见[自然配对实战报告](../experiments/natural-matches-v1-20261109/README.md)。


出牌工程优化第一步新增 `strong-play-forced-v1`：在固定 Medium 续演内部跳过唯一合法动作的观察构造与策略适配，保留原搜索行为。等价测试与分层小规模计时见[第一步报告](../experiments/play-forced-v1-20261109/README.md)。


出牌工程优化第二步新增 `strong-play-cached-v1`，在默认规则搜索内复用不变的公开数据与 Medium 适配参数。原版本保留，配置路径不变。实现范围、等价测试和同局面计量见[第二步报告](../experiments/play-cached-v1-20261109/README.md)。


出牌工程优化第三步新增 `strong-play-incremental-v1`，对默认规则续演进行公共历史的增量维护。本轮仅做针对性测试和小规模计量，整体测试待后续统一执行，见[第三步报告](../experiments/play-incremental-v1-20261109/README.md)。


当前增量优化版与正式 StrongPlus 已完成 32 组替换配对、8 组镜像直接对战及 46 个固定状态的成本对照。当前版替换胜率点估计较好但未确认优势，直接对战 8∶8；叫牌/前期成本仍高于 StrongPlus。565 项整体回归通过，详见[策略、实战与成本报告](../experiments/incremental-vs-strongplus-20261109/README.md)。


新版与 StrongPlus 的扩大测试新增 128 组替换配对和 32 组镜像直接对战，共 320 场；新批次作为主要结果，旧 80 场仅作辅助合并。另在 144 个新公开局面上做 432 组串行成本对照，详见[扩大测试报告](../experiments/incremental-vs-strongplus-expanded-20261110/README.md)。

新增全流程缓存工程版 `strong-flow-cached-v1`（`StrongFlowCachedBot`），基于增量版保留原搜索策略，复用根决策内的 Medium 派生缓存，并直接构造出牌续演输入。行为对照与阶段成本见[全流程缓存报告](../experiments/flow-cached-v1-20261110/README.md)。

StrongFlowCached 现作为完整独立版本保留，与 Random、RandomPass、Weak、Medium、Fast、Strong、Partner、StrongPlus 一同纳入[九版整体比较](../FIVEHUNDRED_BOT_COMPARISON.md)。新评估包含统一性能、体验探针与规则回放；历史胜率按各自实验分别解读。
