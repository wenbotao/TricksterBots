# 无将弃牌修补：结构、选择性复核与工程优化

本轮按“缩小结构副作用 → 风险状态复核 → 工程优化”的顺序实现。结果支持继续保留旧弃牌补丁，没有证据支持将新的结构版或复核版提升为默认策略。建议使用行为等价的工程入口 `strong-discard-guard-efficient-v1`；原 `strong-discard-guard-v1` 及其他既有 bot 不变。

## 实现与命名

策略主体位于 [StrongDiscardRefinedBot.cs](../../FiveHundredLab/Bots/FiveHundred/StrongDiscardRefinedBot.cs)。五个独立 profile 均支持规则对象和配置文件，未覆盖任何旧 profile。

| Profile | 枚举值 | 行为 |
| --- | --- | --- |
| `strong-discard-shape-v1` | `Shape` | 最少改动、保留连续顶张，对失去 Q/J 支撑和可发展长套施加惩罚 |
| `strong-discard-audit-v1` | `SelectiveAudit` | 结构版加选择性独立续演复核 |
| `strong-discard-shape-efficient-v1` | `EfficientShape` | 结构版的缓存实现，保持相同选择 |
| `strong-discard-audit-efficient-v1` | `EfficientAudit` | 复核版的缓存实现，保持相同选择与抽样 |
| `strong-discard-guard-efficient-v1` | `EfficientBaseline` | **保留旧 v1 策略的工程实现，当前推荐入口** |

这些版本沿用原搜索叫牌、出牌和 Misère 公开信息证明；没有重新引入强制 Pass，也没有用预训练替代策略。显式规则配置仍走既有配置路径，不声称已实现与默认路径相同的搜索叫牌。

```csharp
using FiveHundredLab.Bots.FiveHundred;
var bot = new StrongDiscardRefinedBot(
    DiscardRefinementProfile.EfficientBaseline, seed: 7, rules: rules);
// StrongDiscardRefinedBot.FromConfiguration(profile, path, seed)
// Profiles.CreateForRules("strong-discard-guard-efficient-v1", 7, rules)
```

### 第一步：结构修补

旧失败案例只用于形成假设：为解开无将单张 K 而改动多张弃牌，可能损坏其他花色结构。新版本优先最少改动，并保留原有 A/Joker 与连续顶张。候选仍须满足原静态估值下限（原值减 0.5）。在同等改动数量下，对失去有长度支撑的 Q、J 及可发展的长套分别扣 0.45、0.20、0.35。它是有限的牌型启发式，不把未知 Joker 当作已知，也不证明任何长套必定成立。

### 第二步：选择性复核

只在结构取舍、多张改动、埋 K、8NT 以上或接近胜负分界时，将原弃牌与结构修补作成对比较。使用独立随机流和新暗牌样本，普通取舍 8 个世界，高目标/分数风险 16 个世界；双方共用样本。仅当“平均效用差 + 1.5 × 标准误 < −12”时否决修补。复核不推进主搜索随机流，也不访问真实隐藏牌。

这是一项保守的负收益筛查，而不是重新搜索所有弃牌组合。

### 第三步：工程实现

单次无将弃牌决策内复用候选枚举、静态分值及留牌特征；保持原候选顺序、同分处理与随机数使用方式。清理不可达的普通合约证明分支及重复检查。缓存只属于当前决策，不跨玩家或牌局共享。

诊断现在在每次动作开始时清空，避免直接出牌沿用上一动作的搜索报告；新版本的 `LastRollouts` 统计实际搜索及额外复核次数。这是有意的诊断语义改进，旧版本的诊断语义不变。

## 验证设计

[metadata.json](metadata.json) 在结果评估前记录方案和源码哈希。旧案例不作为新验证结果：192 个新候选手牌均排除了此前 256 个完整手牌集合；按旧补丁实际触发筛出 44 个，固定取两类各前 16 个，共 32 个新手牌。该样本有意富集“补丁会触发”的状态，不代表自然对局分布。

每手验证 8 种条件：6–10NT，及 6NT 下己方 400、己方 −400、对方 400 分；每条件 2 副暗牌、2 种续演阵容：

- Medium：四席 Medium。
- Mixed：庄家 Strong、搭档 Fast，防守方 Strong/Medium，随暗牌编号交换位置。

共 256 个决策状态、1,024 个配对组。比较未修补原弃牌、旧 v1、结构版、复核版四个逻辑分支；相同的**有序弃牌数组**共用一次完全相同的实际续演。因此保存的是 **4,096 个逻辑分支单副结果、2,452 局实际单副续演**，不是 4,096 场独立比赛，也不是整场比赛胜率。

同一组发牌、庄家、合约、玩家随机种子完全一致，所有实际续演均完成 10 墩并重放验证。256 个状态核对结构/复核缓存版与参考版的动作和复核报告；32 个基准状态另核对旧 v1 与工程保留版的完整弃牌决策。

分数边界通过测试夹具实施：原引擎记录从零分起算，但玩家观察中的双方分数与 `PublicPlayer.Score` 加上场景起始分；副结束后用权威 `Rules.WinningTeam` 对起始分加本副分数结算。档案保存原记录、起始分、最终分及胜者。这不是引擎新增了起始分接口。效用为终局胜负 ±2000，未终局则为双方本副得分差。

统计以 **32 个手牌为聚类单位**，同一手牌下条件与暗牌等权；两种续演阵容分别报告。95% 区间为手牌聚类 bootstrap 区间。条件与手牌筛选均未按结果挑选；结果出来后未重新调参。

## 结果与选择

下表为成约率变化（百分点），括号内为 95% 区间。完整得分、终局效用及条件分组结果见 [summary.json](summary.json)。

| 比较 | Medium 续演 | Mixed 续演 |
| --- | --- | --- |
| 旧 v1 − 未修补 | +4.69（+1.56 至 +7.81） | +5.66（+1.95 至 +9.57） |
| 结构版 − 旧 v1 | −0.78（−2.34 至 +0.98） | +0.20（−3.13 至 +3.32） |
| 复核版 − 结构版 | 0 | 0 |

旧补丁的条件收益得到新手牌、不同目标和混合续演的支持。结构版相对旧补丁没有可靠提升，得分差也没有显示稳定收益：Medium −5.18 分，Mixed +1.66 分，区间均跨零。

结构版在 174/256 个状态与旧 v1 不同。复核在 138 个状态触发，增加 4,352 次续演评估，却没有否决任何选择；不能据此宣称复核机制已验证有效，也不应承担这项成本作为默认策略。当前选择保留旧策略，仅提供行为等价工程版；结构版、复核版作为独立实验入口保留，方便后续研究。

工程对照的 8 个新案例中，动作、搜索报告和续演数完全相同。相对**本轮结构参考实现**，静态估值计算从每次 572 降为 286，消除了重复工作。该数字不是旧 v1 总成本减半，更不是整次决策耗时减半。并发实验期间的小样本计时，中位数参考版 882.34ms、缓存版 903.51ms，**没有证明端到端加速**。搜索续演仍是主要成本；后续若优化执行器，应先做串行隔离计时，再验证完全相同的动作与随机流。

## 回归与数据

- 全量测试 547/547 通过；新增测试覆盖合法性、最少改动、风险门槛、随机流隔离、诊断清理、缓存等价、默认与六份配置，以及完整对局记录等价。
- 2,452 局实际续演全部再次重放通过；发牌配对、分数覆盖、动作别名及旧手牌排除均已核对。
- 81 个冻结源码/模型文件校验通过。
- [validation.json](validation.json) 保存最终验证结果；[results](results) 保存逐状态动作、逐副指标、压缩回放与工程计数。

主要复现入口（从仓库根目录执行，需 .NET 8；分析脚本另需 Python 的 NumPy）：

```sh
dotnet run --project experiments/discard-refinement-v1-20261109 -c Release -- /tmp/refinement-fresh prepare
dotnet run --project experiments/discard-refinement-v1-20261109 -c Release -- /tmp/refinement-fresh run
dotnet run --project experiments/discard-refinement-v1-20261109 -c Release -- /tmp/refinement-fresh verify
python3 experiments/discard-refinement-v1-20261109/analyze.py
dotnet test TestBots -c Release --logger 'trx;LogFileName=discard-refinement.trx' --results-directory /tmp/refinement-validation
python3 experiments/discard-refinement-v1-20261109/verify.py
```

分析与综合验证脚本读取本目录保存的 `results`；前三条可在新目录独立重跑。保存结果还校验运行程序集哈希，重建后的程序集如哈希变化，应使用新输出目录，不能覆盖混用旧结果。`verify.py` 同时读取上述 TRX 与已保存工程基准；基准入口为 `bench/Bench.csproj`，参数为结果目录。

后续优先研究：先记录复核中实际差值与方差，判断是估值噪声过大还是筛查门槛失效；另用新样本拆分“少改牌”与“长套惩罚”的贡献。已有验证集应保留为回归集，不能边看本轮结果边调参后继续称为独立验证。尚无证据支持为了追求指标而扩大默认复核预算。
