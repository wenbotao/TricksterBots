# 弃牌补丁工程精简：执行等价验证

新增独立版本 **`strong-discard-guard-lean-v1`**，公共类为 [StrongDiscardLeanBot](../../FiveHundredLab/Bots/FiveHundred/StrongDiscardLeanBot.cs)。对照为 `strong-discard-guard-efficient-v1`，原版本及其冻结源码均不修改。内部续演继续使用 Medium，没有换用 Strong，也没有改变策略参数或搜索预算。

本轮完成工程精简，并在已测范围内确认实际执行一致；**没有证明端到端加速**。因此该版本可用于后续维护和工程研究，不应以“更快”或“更强”替代对照版本。

## 改动

- 从固定的旧补丁策略路径中移除结构特征、结构惩罚与独立复核实现，不再计算没有参与决策的 `beforeShape`。保留诊断字段，结构计算数为零，复核状态始终为未启用。
- 移除为每个候选排序并拼接字符串的全量索引。修补最多查询一次原弃牌，改为一次集合匹配查找。
- 候选排名按需生成并缓存。完整根搜索仍使用相同的稳定降序；独立修补无需排序。
- 保留候选枚举顺序、严格大于的改选方式、A/Joker 保护、原值减 0.5 的下限和 K 支撑判断。自定义牌堆的历史行为也保持原样，不混入规则修正。
- 缓存仅属于当前决策，未引入跨玩家或跨牌局状态。

从 `Play` 到文件结尾的公开战术、配置路径和默认根搜索实现，与对照源码逐字相同。其他动作执行仍清空诊断。新类拥有自身策略主体，不调用冻结旧 bot 的决策实现。

```csharp
var bot = new FiveHundredLab.Bots.FiveHundred.StrongDiscardLeanBot(seed: 7, rules: rules);
// StrongDiscardLeanBot.FromConfiguration(path, seed: 7)
// Profiles.CreateForRules("strong-discard-guard-lean-v1", 7, rules)
```

## 执行验证

验证输入沿用上一轮样本，作为回归集，不当作新的强度验证集。

| 检查 | 结果 |
| --- | --- |
| 256 个状态：6–10NT 与分数边界的独立修补 | 有序弃牌数组及修补诊断全部一致，也与既有 v1 档案一致 |
| 32 次完整根节点弃牌搜索 | 有序动作、搜索报告与续演次数全部一致 |
| 连续叫牌、弃牌、出牌的体验场景 | 动作及搜索报告一致 |
| 独立修补后继续叫牌 | 与新实例结果一致；未推进主搜索随机流 |
| 诊断清理、非法重复弃牌、工厂入口 | 通过 |
| 默认规则及六份配置 | 每份进行一组相同种子的单副上限对局，搭档 Fast、对手 Medium/Strong；规范化主体名称后，完整记录一致，双方均通过回放 |
| 全量测试 | **550/550 通过** |
| 冻结校验 | **82 个源码/模型文件通过** |

回归不等于对所有输入的数学证明；本轮没有新增自然比赛胜率估计。新旧策略在相同测试输入下的动作及随机流一致，因此也没有将回归牌谱另计为“胜率提升”。

[metadata.json](metadata.json) 保存新旧源码哈希与测量方案；[validation.json](validation.json) 保存测试计数及验证结果。新文件以 `frozen-profiles-v18.json` 冻结。

## 串行性能测量

全量回归及其他续演实验结束后，串行执行 8 个手牌 × 3 次重复，共 24 组配对。两版各先预热一次，每组交替执行顺序，使用相同手牌、种子与观察；每次使用新 bot。分配量记录当前线程的决策主体，不含 bot 构造，不强制 GC。所有测量中的有序动作、搜索报告、静态估值次数与续演数相同。

| 指标 | 对照 EfficientBaseline | Lean |
| --- | ---: | ---: |
| 决策耗时中位数 | 584.99 ms | 595.73 ms |
| 决策耗时 P95 | 673.87 ms | 682.80 ms |
| 决策分配量中位数 | 1,167,227,248 B | 1,167,001,748 B |

逐组相对耗时的中位数为 Lean/对照约 1.002。每次平均减少分配约 229,369 B（约 0.23 MB），仅占总体约 0.02%；结构特征计算从一次降到零。排序和索引的精简确实减少了工作，但这部分占完整搜索成本太小，测量未显示整体速度提升，小样本 P95 也只能作描述。

**执行等价已通过本轮验证；速度收益尚未成立。** 原实现仍可继续作为固定对照，不根据这些数据推广“更快”的结论。完整逐次测量见 [results/benchmark.json](results/benchmark.json)。

## 下一步

如继续优化，应先对根节点续演的对象分配做剖析：本批每次决策累计分配约 1.17 GB（累计分配量，不是峰值内存），比候选索引开销大得多。优先寻找可复用的只读规则/牌表、反复构造的状态容器及重复解析；任何可变状态复用都需保证玩家隔离、世界隔离与同样的随机数顺序。先保持预算与续演策略不变，逐步证明牌谱等价，再测耗时；不直接降低样本数来制造加速。

本轮未扩展该方向，未改引擎或共享续演器，未发布或上传。

## 复现

从仓库根目录执行，需 .NET 8 与 Python 3：

```sh
dotnet test TestBots -c Release --logger 'trx;LogFileName=full.trx' --results-directory /tmp/discard-lean-validation
dotnet run --project experiments/discard-lean-v1-20261109 -c Release -- validate
# 等其他计算任务结束后单独执行：
dotnet run --project experiments/discard-lean-v1-20261109 -c Release --no-build -- bench
dotnet build FiveHundredLab -c Release
dotnet run --project FiveHundredLab -c Release --no-build -- verify-frozen --root .
python3 experiments/discard-lean-v1-20261109/verify.py
```

脚本默认读取 `/tmp/discard-lean-validation/full.trx`，可用 `LEAN_TRX` 指定其他路径；`DOTNET` 可指定 dotnet 可执行文件。结果写入本目录 `results`。性能重跑会覆盖测量文件，应先另存需要保留的结果。
