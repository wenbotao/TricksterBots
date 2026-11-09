# 出牌工程优化第二步：复用重复数据准备

新增独立入口 **`strong-play-cached-v1`** / [StrongPlayCachedBot](../../FiveHundredLab/Bots/FiveHundred/StrongPlayCachedBot.cs)，以第一步 `strong-play-forced-v1` 为对照。保留第一步的强制动作快路、Medium 续演、32 样本、候选顺序、加权统计与改选门槛；本轮继续只做工程优化。

## 复用的内容与边界

新的 [PlayDataContext](../../FiveHundredLab/Bots/FiveHundred/PlayDataContext.cs) 在一次默认规则出牌搜索中准备一次，供该搜索的候选及隐藏世界使用：

- 43 张规则牌的字符串编码；不共享可变 `Card` 对象，不改变牌顺序。
- 按席位从竞叫记录生成的公开叫牌历史及对应旧接口整数编码。继续从 `Auction` 派生，保持旧续演器的取数语义。
- 竞叫、比分的只读快照，以及普通默认规则记录的副本。
- 根据张数生成的未知手牌占位字符串。
- 以 Joker 当前领出声明花色为键的参数对象，每次根搜索最多 5 种；不通过修改同一个参数对象来切换花色。

新的 [CachedPlayRollout](../../FiveHundredLab/Bots/FiveHundred/CachedPlayRollout.cs) 使用上述准备，省去每次观察中的竞叫过滤、数组复制、比分复制和规则记录复制；非强制动作适配时不再反复将同一张牌转成字符串。

手牌、已取牌、历史、当前墩和玩家对象继续逐分支独立构造。传给旧策略的叫牌历史仍创建新的可变列表，策略实例每次仍新建；没有将隐藏世界中的手牌放入共用缓存，也没有全局或跨根节点缓存。`LastDataWork` 提供单次搜索的准备数量，随决策阶段清空。

本轮**未做历史增量维护或策略实例缓存**。Medium 的策略判断继续调用原 `DefaultStrategy`，历史转换的判断循环不变。原 bot、共享续演器和 Medium 均保持冻结；叫牌与弃牌路径也保持原样。

显式规则配置的策略不是这套旧接口适配，本轮沿用第一步的 `ForcedConfiguredPlayRollout`，没有为配置路径添加这项缓存，也不声称其性能改善。

```csharp
var bot = new FiveHundredLab.Bots.FiveHundred.StrongPlayCachedBot(seed: 7, rules: rules);
// StrongPlayCachedBot.FromConfiguration(path, seed: 7)
// Profiles.CreateForRules("strong-play-cached-v1", 7, rules)
```

## 等价性验证

新增 6 项测试，覆盖：

1. 普通有将、无将、Misère、Open Misère 下，共用同一个准备上下文，跨候选和隐藏世界逐步比较续演动作与效用；候选再次执行也保持相同结果，未发现缓存污染。
2. 单张 Joker 多种可声明花色的边界。
3. 配置规则原路径兼容，以及默认与六份配置的单副上限实际对局记录一致、双方回放通过。
4. 连续叫牌、弃牌、出牌的动作、搜索报告、续演次数及诊断清理。
5. Joker 花色按 S→C→D→H→S 交替调用，结果与原适配器一致；只读竞叫、比分与叫牌快照拒绝写入，不同上下文不共用计数/参数缓存。
6. 工厂和配置文件入口。

全量 **561/561 测试通过**，**88 个冻结源码/模型文件校验通过**。根节点候选比较和加权统计的源码另核对与第一步逐字一致。没有重新跑整场胜率比较，本轮不作强度提升结论。

## 小规模串行对照

原样使用第一步保存的 24 个真实公开观察及种子：前期、中期、后期、根节点强制动作各 6 个。它们是固定回归集，不是新的强度留出集；均再次核对与原实战公开上下文哈希相符。输入哈希及计量前方案保存在 [results/registration.json](results/registration.json)。

全量测试结束后串行运行，每个局面重复 3 次，交替新旧执行先后顺序，独立体验局面预热。构造 bot 在计时外，但本次搜索的数据准备包含在计时与分配中。分配量是当前线程累计申请字节，不是峰值内存。

**72 组的动作、搜索报告、续演次数、强制动作数及 Medium 调用次数全部一致。** 因此工作量没有靠减少样本或缩短续演来降低。

下表每个局面先取三次测量中位数，再取每层六个局面的中位数；耗时比列则先在每个局面计算新/旧比，再取中位数。这两种汇总不同，不用两列中位数相除代替配对耗时比。

| 局面 | 耗时中位数 ms：第一步 → 本轮 | 分配量中位数 MB：第一步 → 本轮 | 逐局面配对耗时比中位数 |
| --- | ---: | ---: | ---: |
| 前期 | 274.86 → 269.05 | 592.51 → 529.10 | 0.918 |
| 中期 | 97.10 → 86.73 | 195.24 → 170.05 | 0.915 |
| 后期 | 13.76 → 11.63 | 26.99 → 23.03 | 0.861 |

本次两版各自测量的整体耗时水平与第一步那次不同，不能跨次实验直接相减或把百分比相加。根节点强制动作本来就不搜索、不准备缓存，两版分配约 216 B；微秒计时不作性能推断。

重复测量内共建立 **54 个根搜索上下文、63 份参数**，服务 **201,177 次续演 Medium 适配调用**。旧适配器原本每次调用都会新建参数；上述计数只涉及续演，不包含两版都保留的根节点 Medium 基准调用，也不把三次重复当独立样本。

分配量中位数约下降 11%–15%，本次逐局面配对耗时比中位数显示局部改善。只有每层 6 个局面，且不同层可能来自相同比赛；这些是描述性结果，未证明所有局面都会加速。四层均分不代表自然比赛权重，也不能推出整场加速比例。计量只含普通有将/无将，Misère 和配置变体仅做了行为验证。

## 保留与后续

保留本轮版本作为下一步工程研究入口，第一步版本继续冻结作对照。参数和编码准备已减少重复，但每次非强制动作仍完整遍历历史、重建 `PlayedCards` 与 `VoidSuits`，并重新构造策略实例和解析手牌。

下一项优先研究历史增量维护：在续演每次出牌时更新公开历史摘要，保留原来的牌序、缺门记录顺序，以及“当时暂时赢墩的牌”，不能替换成最终赢墩牌。仍需先验证逐步动作/效用一致，再计量收益；本轮没有提前叠加这些改动。

## 文件与复现

[summary.json](summary.json) 为统计汇总，[results/benchmark.json](results/benchmark.json) 保存逐次测量，[validation.json](validation.json) 保存验证结果。三个新源码以 `frozen-profiles-v20.json` 冻结。

从仓库根目录执行，需 .NET 8、Python NumPy：

```sh
dotnet test TestBots -c Release --logger 'trx;LogFileName=full.trx' --results-directory /tmp/play-cached-validation
dotnet build experiments/play-cached-v1-20261109 -c Release
# prepare 仅新目录首次执行；原样复制第一步的 cases.json：
# dotnet experiments/play-cached-v1-20261109/bin/Release/net8.0/Study.dll prepare
# 等其他计算任务结束后单独测量；会覆盖 benchmark.json：
dotnet experiments/play-cached-v1-20261109/bin/Release/net8.0/Study.dll bench
python3 experiments/play-cached-v1-20261109/analyze.py
dotnet build FiveHundredLab -c Release
python3 experiments/play-cached-v1-20261109/verify.py
```

`CACHED_TRX` 可指定 TRX 路径，`DOTNET` 可指定可执行文件。保留原文件后再重测。未提交或上传 GitHub。
