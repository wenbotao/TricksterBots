# 出牌工程优化第三步：增量历史摘要

新增独立入口 **`strong-play-incremental-v1`** / [StrongPlayIncrementalBot](../../FiveHundredLab/Bots/FiveHundred/StrongPlayIncrementalBot.cs)，以数据复用版 `strong-play-cached-v1` 为对照。按用户要求，本轮仅做针对性行为测试及小规模性能对照，**未运行全量回归或整场胜率测试**。

## 实现

- [IncrementalPlayHistory](../../FiveHundredLab/Bots/FiveHundred/IncrementalPlayHistory.cs) 保存每席出牌时的暂时赢墩牌、首次观察到的缺门顺序、当前墩前缀和出牌字符串。每新增一张牌时更新一次，强制出牌同样更新。
- [IncrementalPlayDataContext](../../FiveHundredLab/Bots/FiveHundred/IncrementalPlayDataContext.cs) 为每次根搜索建立一次公共历史前缀；[IncrementalPlayRollout](../../FiveHundredLab/Bots/FiveHundred/IncrementalPlayRollout.cs) 为每个候选/世界复制独立摘要和当前墩，追加后续出牌。
- 历史条目存储 `PlayingCard` 值，不共享可变旧接口模型。每次策略调用仍生成新的 `PlayedCard`、`Card` 与列表；只读字符串可复用。候选分支不能修改前缀或其他分支。
- 原历史中的“当时暂时赢墩牌”保持不变，不用最终赢墩牌回填。缺门使用有序列表，保留首次出现顺序。
- 继续使用原 Medium 判断、样本数、随机流、候选与计分。根节点候选比较及加权统计源码与对照逐字一致。叫牌、弃牌及显式配置路径不变；配置路径仍用此前的强制动作执行器。
- 原版本和共享设施均未修改。`LastDataWork` 新增历史前缀长度、分支复制数与追加数诊断。

```csharp
var bot = new FiveHundredLab.Bots.FiveHundred.StrongPlayIncrementalBot(seed: 7, rules: rules);
// StrongPlayIncrementalBot.FromConfiguration(path, seed: 7)
// Profiles.CreateForRules("strong-play-incremental-v1", 7, rules)
```

## 快速行为验证

仅运行新增的 **4 项针对性测试，全部通过**：

1. 普通有将、无将、Misère、Open Misère 的抽样续演：逐步动作、最终效用和强制/策略调用次数与缓存版一致；重复执行候选结果不变。
2. 每一个历史前缀与冻结旧适配器的完整重建结果比较：缺门顺序、历史条目及暂时赢墩牌、已完成墩的牌和出牌字符串一致；分支追加和返回列表修改不污染父摘要。
3. 连续根节点叫牌、弃牌、出牌的动作及搜索报告一致，诊断清理正常；历史追加数与实际根动作/续演动作数吻合。
4. 默认规则与美式显式配置各一组单副上限对局，规范化名称后牌谱一致，双方回放通过；工厂及文件配置入口可用。

92 个冻结源码/模型文件校验通过。新四个源码以 `frozen-profiles-v21.json` 冻结。上述结果是初步验证，不替代后续整体回归；其余配置未在本轮全面重测。

## 简单性能对照

原样沿用前两步的 24 个真实公开观察，前、中、后期及根节点强制动作各 6 个；每个重复 **2 次，共 48 组配对**。针对性测试结束后串行测量，交替先后顺序，使用相同观察/种子，独立体验场景预热。数据准备包括在决策计时中，不含 bot 构造；分配量是当前线程累计分配，非峰值内存。

**48 组的动作、搜索报告、续演次数、强制动作数及 Medium 调用次数全部一致。** 每局面先取两次测量的中位数，再对每层六个局面取中位数：

| 局面 | 耗时中位数 ms：缓存版 → 增量版 | 分配量中位数 MB：缓存版 → 增量版 |
| --- | ---: | ---: |
| 前期 | 269.37 → 250.81 | 529.10 → 489.68 |
| 中期 | 88.79 → 74.54 | 170.05 → 151.57 |
| 后期 | 12.16 → 10.91 | 23.03 → 20.23 |

根节点唯一动作原本就直接返回，不创建历史摘要，分配量仍约 216 B；微秒计时不作速度判断。

这批数据显示局部耗时下降，分配量中位数约减少 7%–12%。样本很小，使用的是同一批回归局面，不是独立强度留出集；等额分层也不是自然比赛权重。因此不推断整场加速比例或胜率变化。计时样本仅有普通合约，Misère 性能及更多规则变体仍待整体测试。详细计量见 [summary.json](summary.json)、[results/benchmark.json](results/benchmark.json)。

## 后续统一测试

保留增量版供整体测试。后续重点包括：多局连续随机流、全部规则配置、Misère/Open Misère、Joker 声明边界，以及长历史与各种候选分支；同时核对是否存在只有强制续演、却额外建立历史摘要的开销。本轮未为扩大收益改动样本数或省略计分。

## 复现

需 .NET 8、Python NumPy，从仓库根目录执行：

```sh
dotnet test TestBots -c Release --filter FullyQualifiedName~TestPlayIncremental --logger 'trx;LogFileName=targeted.trx' --results-directory /tmp/play-incremental-validation
dotnet build experiments/play-incremental-v1-20261109 -c Release
# prepare 仅首次执行；本目录已有固定 cases.json：
# dotnet experiments/play-incremental-v1-20261109/bin/Release/net8.0/Study.dll prepare
# 单独运行计时，会覆盖 benchmark.json：
dotnet experiments/play-incremental-v1-20261109/bin/Release/net8.0/Study.dll bench
python3 experiments/play-incremental-v1-20261109/analyze.py
dotnet build FiveHundredLab -c Release
python3 experiments/play-incremental-v1-20261109/verify.py
```

[results/registration.json](results/registration.json) 保存事前方案和输入/源码哈希；[validation.json](validation.json) 保存验证结果。`HISTORY_TRX` 可指定 TRX 路径，`DOTNET` 可指定可执行文件。未提交或上传 GitHub。
