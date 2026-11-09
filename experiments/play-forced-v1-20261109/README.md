# 出牌工程优化第一步：续演中的强制动作快路

新增独立版本 **`strong-play-forced-v1`** / [StrongPlayForcedBot](../../FiveHundredLab/Bots/FiveHundred/StrongPlayForcedBot.cs)，以冻结的 `strong-discard-guard-lean-v1` 为对照。只优化出牌搜索内部的执行方式，保留 Medium 策略、32 个隐藏世界、候选顺序、改选门槛、效用与计分。叫牌与弃牌继续使用原续演路径，未换用 Strong。

## 实现

- 根节点出牌已有唯一合法动作直接返回，本轮不重复改动。
- 新的 [ForcedPlayRollout](../../FiveHundredLab/Bots/FiveHundred/ForcedPlayRollout.cs) 先计算合法动作。只有一个完整 `PlayAction` 时直接应用，不构造观察、不经过 Medium 适配；多个动作仍调用同一个 Medium 默认策略。
- [ForcedConfiguredPlayRollout](../../FiveHundredLab/Bots/FiveHundred/ForcedConfiguredPlayRollout.cs) 提供相同快路，保留查询合法动作前的历史、当前席位、庄家、Joker 声明及独立规则上下文。
- Joker 只有一张牌但有多个可声明花色时，仍有多个动作，必须走原策略。
- 不省略实际出牌、墩胜者判断、历史更新、计分或根节点样本。Medium 当前续演无学习状态、无需要在强制动作时推进的随机流；该快路不能不经验证直接套到未来有状态的续演策略。
- 原 bot、Medium 与共享 `DefaultRollout` / `ConfiguredRollout` 均未修改。新执行器仅供新命名 bot 使用，避免影响其他冻结版本。

`LastPlayWork` 提供当前出牌决策的候选续演次数、续演内部强制动作数与策略调用数。其他决策阶段返回 null，计数不参与选牌。

```csharp
var bot = new FiveHundredLab.Bots.FiveHundred.StrongPlayForcedBot(seed: 7, rules: rules);
// StrongPlayForcedBot.FromConfiguration(path, seed: 7)
// Profiles.CreateForRules("strong-play-forced-v1", 7, rules)
```

## 等价验证

新增五项测试，验证的是执行结果，而不是仅验证计数：

1. 默认规则下普通有将、无将、Misère、Open Misère：相同隐藏世界、相同候选动作，逐步续演动作序列与最终效用对照一致。
2. 单张 Joker 可作四种领出声明的边界：仍调用策略，声明、后续动作及效用与原实现一致。
3. 六份配置：合法抽样世界内的候选续演效用对照一致。
4. 连续叫牌、弃牌、出牌：动作、搜索报告及续演次数一致；额外计数按阶段清空，未改变后续随机流。
5. 默认规则及六份配置的单副上限对局：规范化测试者名称后记录完全一致，双方均可回放；搭档 Fast，对手 Medium/Strong。

全量 **555/555 测试通过**，冻结检查 **85 个源码/模型文件通过**。本轮没有重新跑整场胜率比赛，也没有声称强度提高。

## 串行小规模性能对照

从上一轮自然实战牌谱通过引擎回放恢复公开观察，按前期、中期、后期、根节点强制动作四层各取 6 个局面。每层每场来源比赛最多取第一个合格局面，固定取前 6 个合格来源比赛；不按耗时、动作结果或胜负筛选。全部 24 个观察均与实际遥测的公开上下文哈希匹配。

每个局面重复 3 次，共 **72 组配对**。全量回归结束后串行运行；两版先用独立的未计量体验场景预热，交替执行先后顺序，使用相同观察与种子，每次新建 bot。计时和当前线程分配量覆盖决策主体，不含 bot 构造，不强制 GC。

**全部 72 组的动作、搜索报告、续演次数相同。** 每个局面先取三次测量的中位数，下表再对每层六个局面取中位数。分配量为累计申请对象的字节数，非峰值内存。

| 局面 | 出牌耗时中位数 ms：旧 → 新 | 分配量中位数 MB：旧 → 新 | 续演内部强制动作占比 |
| --- | ---: | ---: | ---: |
| 前期：8–10 张牌、至少 5 个动作 | 406.89 → 329.43 | 731.60 → 592.51 | 25.33% |
| 中期：4–7 张牌、至少 3 个动作 | 159.47 → 110.84 | 264.79 → 195.24 | 29.68% |
| 后期：最多 3 张牌、至少 2 个动作 | 31.54 → 14.21 | 60.75 → 26.99 | 53.59% |

根节点强制动作层两版原本就直接返回，续演均为零；微秒量级计时及约 200 B 分配差异不作速度推断。完整数据见 [summary.json](summary.json) 和 [results/benchmark.json](results/benchmark.json)。

72 次优化版测量共执行 9,984 次候选续演评估；其中续演实际执行了 279,072 个后续动作，77,895 个（27.91%）跳过了观察构造与 Medium 适配。这些是含三次重复的实际工作计数，不是同等数量的独立样本。

## 判断与下一步

第一步有明确的小样本局部收益，值得保留为后续工程基准；旧精简版继续冻结作对照。这次与此前只删除候选索引不同，减少的是每个候选、每个世界中反复发生的工作。

限制：每层只有 6 个局面，且不同层可能来自相同比赛；只给描述性结果，不计算将重复测量当独立样本的显著性。四层等额选样不等于自然出牌分布；不能将表中差值直接换算成整场加速比例。计量局面只有普通有将/无将，Misère 与变体配置只完成行为等价测试，尚未做其性能评估。

本轮按逐步推进的方式只完成第一项。接下来优先研究**非强制动作仍在重复执行的 Medium 适配**：单次决策内预处理不变竞叫和牌编码，然后再考虑历史信息增量维护。继续保留同一策略、随机流和预算，分别验证收益，不叠加未验证的缓存或对象池。

## 复现与文件

[results/registration.json](results/registration.json) 保存计量前方案与策略哈希；[validation.json](validation.json) 保存最终测试结果。新三个源码以 `frozen-profiles-v19.json` 冻结。

```sh
# 仓库根目录；需要 .NET 8、Python NumPy
dotnet test TestBots -c Release --logger 'trx;LogFileName=full.trx' --results-directory /tmp/play-forced-validation
dotnet build experiments/play-forced-v1-20261109 -c Release
# 样本只准备一次；本目录已保存 cases.json，勿覆盖原登记：
# dotnet experiments/play-forced-v1-20261109/bin/Release/net8.0/Study.dll prepare
# 等其他计算任务结束后再测量；会覆盖 benchmark.json：
dotnet experiments/play-forced-v1-20261109/bin/Release/net8.0/Study.dll bench
python3 experiments/play-forced-v1-20261109/analyze.py
dotnet build FiveHundredLab -c Release
python3 experiments/play-forced-v1-20261109/verify.py
```

验证脚本默认读取 `/tmp/play-forced-validation/full.trx`，可用 `FORCED_TRX` 指定其他路径，`DOTNET` 指定 dotnet 命令。保留原始性能文件后再重测。本轮未提交或上传 GitHub。
