# 第四阶段：弱牌开叫与无将挡张

本轮按“开叫边界 → 弃牌修补 → 分别实战”的顺序推进。保留第三阶段仅 Misère 证明为基准，不加入此前收益未确认的普通合约证明，不修改任何旧 bot。

## 第一步：窄范围弱牌开叫判断

新增 [StrongBidGuardBot](../../FiveHundredLab/Bots/FiveHundred/StrongBidGuardBot.cs)，profile 为 `strong-bid-guard-v1`。除以下情形直接 Pass 外，沿用仅 Misère 版的策略：

- 所有历史叫牌均为 Pass，当前允许 Pass。
- 没有可叫的 Misère/Open Misère 或 Inkle。
- 十张手牌没有 Joker/J/Q/K/A，没有五张及以上同花色长套。
- 本队比分高于失分线加 150，双方比分均低于胜分线减 150。

这是一条保守体验偏好，不是“数学上绝不能成约”的结论。它不限制长套低牌、荣张牌力、竞争叫牌和接近终局时的判断。默认路径在未触发时继续使用原搜索叫牌；显式配置继续使用原规则版叫牌。触发时省去本次叫牌搜索，不消耗相应隐藏牌采样，后续随机数位置可能随之改变。

第一步的两个测试覆盖正例、荣张/长套/竞叫/比分压力/Misère/不可 Pass 等反例，以及其他阶段与基准的动作等价。新增实现直接拥有策略主体，不调用旧 Strong 或原 `Trickster.Bots.FiveHundredBot`。

## 第二步：无将弃牌修补

新增 [StrongHandGuardBot](../../FiveHundredLab/Bots/FiveHundred/StrongHandGuardBot.cs)，profile 为 `strong-hand-guard-v1`，累计保留第一步的叫牌判断。先运行原来的弃牌搜索，再仅对以下结果作修补：普通无将合约中，原本有同门小牌保护的 K 被弃成单张，且本手没有该门 A，也没有公开信息说明该 A 已离场。

修补枚举同样数量的合法弃牌，寻找不再产生这种单张 K 的替代；允许连 K 一起弃掉。必须保留原搜索保留的全部 A/Joker，静态估值不得低于原选择减 0.5。没有符合条件的替代就保留原动作。这里不新增续演，额外成本来自小规模组合的静态估值；没有把“保留 K”设为绝对目标，也没有改变普通将牌或 Misère 的弃牌。

累计四项测试覆盖修补合法性、保护 A/Joker 的代码约束、不同合约不介入、A 已离场不介入、四个种子的原体验场景，以及两种新版本在默认和六份配置下运行与重放、规则不匹配拒绝。两种版本均支持 `(int seed = 1, Rules? rules = null)` 及 `FromConfiguration(path, seed)`，已接入 `Profiles.Create` / `CreateForRules`。

## 第三步：固定体验审计

17 个场景 × 4 个种子 × 3 个版本，共 204 次查询，动作全部合法：

| 版本 | 检查失败项 | 严重失败项 |
|---|---:|---:|
| 仅 Misère 基准 | 5 | 0 |
| 叫牌版 | 1 | 0 |
| 综合版 | 0 | 0 |

叫牌版只在 4 次弱牌开叫中改变建议，弃牌和出牌不变；综合版相对叫牌版只改变 1 次无将弃牌，叫牌和出牌不变。这里的“0 失败”仅针对这 17 个固定场景，不能推广为没有其他失误；四个种子也不是四类独立场景。完整审计在 `results/audit.json`。

## 第四步：两批独立实战

计划在实战前固定于 [metadata.json](metadata.json)：

| 阶段 | 对照 | 纯队 | 与 Medium 搭档 | 种子基数 |
|---|---|---:|---:|---:|
| bid | 叫牌版 / 仅 Misère 基准 | 4 对 / 8 场 | 4 对 / 8 场 | 41119001 |
| hand | 综合版 / 叫牌版 | 4 对 / 8 场 | 4 对 / 8 场 | 43119001 |

每对相同发牌和起始庄家、交换队伍；每场每个座位独立创建 bot。相邻种子增加 7919，混合队另偏移 678901。默认规则，200 轮上限，四路并行。逐场保存压缩完整牌谱、计时与修补事件。先完成叫牌批并报告，再进行弃牌批；不因第一批结果临时调参或更换种子。

两批比较的算法不同，不能合并成一个胜率。每批仅 8 个成对种子，只用于快速反馈。若随机牌局中未出现对应修补事件，就不能拿相同胜率证明修补收益或没有副作用。统计、成对全胜/平分/全负、保守区间和按对分层抽样的 bootstrap 区间见 [summary.json](summary.json)。全为平分对时 bootstrap 可能退化，需同时看保守区间。

完整回归和牌谱重放见 [validation.json](validation.json)。本轮没有自动升级默认 bot，没有训练、调参或上传 GitHub。

## 实战与独立局面的最终结果

| 比较 | 纯队 | Medium 搭档 | 合计 | 新增修正触发 |
|---|---:|---:|---:|---:|
| 叫牌版 / 仅 Misère 基准 | 4–4 | 4–4 | 8–8 | 0 |
| 综合版 / 叫牌版 | 4–4 | 4–4 | 8–8 | 0 |

32 场均正常结束，无截断或非法动作，完整重放全部通过。两批各 8 对换边比赛的 `Rounds` 内容逐字段完全一致；版本名称不同，但同一种子下没有触发新分支，所以实际决策轨迹也相同。这里的五五开是未介入时的等价行为，不是对新增修正效果的有效检验，更不能宣称已确认没有强度下滑。

补充的独立弃牌诊断使用 64 个新种子 `45119001 + index*7919`，不筛选手牌。通过引擎合法竞叫 6NT、拾取 kitty 后捕获观察，在同一输入、同一策略随机种子下分别调用叫牌版和综合版：

- 64 次修补前选择全部与原搜索一致，动作合法，搜索已保留的 A/Joker 均未被额外弃掉。
- 64 次均未触发修补，因此没有提供原挡张场景以外的行为改善证据。
- 捕获后停止引擎，不把这些观察计为完整对局，也不参与胜率统计。复现代码见 [diagnostics/Program.cs](diagnostics/Program.cs)，任务计划与输出分别为 `results/discard-probe-plan.json`、`results/discard-probe.json`。

全部 **539 项回归测试通过，32 场重放通过，79 项冻结源码和模型校验通过**。新策略源码哈希与比赛前登记一致，见 `frozen-profiles-v15.json` 和 [metadata.json](metadata.json)。

本阶段的可靠结论仅为：两项窄范围的体验偏好已经实现、原有定向场景修复、未介入时策略保持原行为；尚未证明一般性决策改善或比赛胜率提升。修补适用条件很窄，不应为了提高测试触发率放宽条件或根据这批数据反复调参。对无将 K 的保护也不是优于搜索的数学证明：A 可能在搭档手中，或能在后续被消耗，保持单张 K 有时是合理取舍；静态价值容差与控制牌保护只能限制修补范围，不能替代成约收益验证。默认庄家已经看到完整 kitty，不能将不在十三张手牌中的 A 假设为仍在未知 kitty；此处在扩大收益测试时更正了原先的表述。

两个版本保留为独立实验入口，不替代 Strong、StrongPlus、Fast 或 Partner。后续若需要评估真实收益，优先构建规则合法、与实际竞叫相符的更多受影响边界局面，再针对这些局面做成对完整续演；当前随机场次没有触发事件，单纯继续跑相同分布的少量比赛难以回答收益问题。本轮不继续增加场次或改变阈值。

随后开展的更大样本、实际触发局面与独立暗牌配对续演，单独记录在[扩大收益验证](../guard-benefits-v1-20261109/README.md)，不混入本报告的小样本统计。

## 复现

需 .NET 8，统计脚本需 NumPy、SciPy。仓库根目录执行：

```sh
dotnet build experiments/hand-guards-v1-20261109/Study.csproj -c Release
dotnet experiments/hand-guards-v1-20261109/bin/Release/net8.0/Study.dll experiments/hand-guards-v1-20261109/results audit
dotnet experiments/hand-guards-v1-20261109/bin/Release/net8.0/Study.dll experiments/hand-guards-v1-20261109/results bid
python3 experiments/hand-guards-v1-20261109/analyze.py
dotnet experiments/hand-guards-v1-20261109/bin/Release/net8.0/Study.dll experiments/hand-guards-v1-20261109/results hand
python3 experiments/hand-guards-v1-20261109/analyze.py
dotnet build experiments/hand-guards-v1-20261109/diagnostics/Probe.csproj -c Release
dotnet experiments/hand-guards-v1-20261109/diagnostics/bin/Release/net8.0/Probe.dll experiments/hand-guards-v1-20261109/results
python3 experiments/hand-guards-v1-20261109/analyze.py
dotnet test TestBots -c Release --logger 'trx;LogFileName=hand-guards.trx' --results-directory /tmp/hand-guards-validation
python3 experiments/hand-guards-v1-20261109/verify.py
```

任务清单和策略程序集哈希分别保存在 `results/plan-*.json`、`results/version-*.txt`。同版本任务可断点续跑；改动程序集后应使用新输出目录。完整牌谱包含暗牌，供离线复盘；实际 bot 仍只接受受限观察。
