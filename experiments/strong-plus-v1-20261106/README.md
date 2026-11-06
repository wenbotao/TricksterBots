# StrongPlus-v1 强化实验

本实验保留原 Strong 和之前的随机对战记录，新增独立的 `strong-plus-v1`。它接受相同的规则参数，默认路径作为主要实战对象，配置路径只作功能性验证。

策略代码见 [StrongPlusBot.cs](../../FiveHundredLab/Bots/FiveHundred/StrongPlusBot.cs)。共享的规则、采样器和续演器未改。完整基线为同目录 StrongBot。

- 隐藏牌样本：8 → 32。
- 弃牌候选：12 → 24。
- 默认搜索改选门槛：8 → 6；标准误惩罚系数：0.75 → 1.0。
- 默认基准及续演：改用迁入 Medium 文件的可见手牌判断变体。
- 默认根节点：公开可证的零墩躲墩、公开必破，以及避免必吃墩的Misère首引。
- 配置路径：仍是原启发式叫牌，扩大弃牌/出牌搜索；未搬入默认三人零墩的证明代码。

不使用真实隐藏牌、发牌种子、牌谱审计字段、预训练模型或在线学习。所有策略判断在StrongPlusBot文件内，公共计算仍共用。

## 试验设计

`metadata.json`记录种子公式、预算、源码指纹和原提交。`Program.cs`负责：

1. 30组成对纯队比赛：StrongPlus×2 对 Strong×2。
2. 15组成对混合搭档比赛：StrongPlus+Medium 对 Strong+Medium。
3. 每组两场使用相同发牌种子、初始庄位，交换两队；同一座位使用相同bot种子。两条比赛之后的竞叫及比分可不同，不是假定未来观察相同。
4. 四路并发，每场上限200次发牌；截断不按领先方计胜。固定测试规模，不按中途输赢提前停止。
5. 17个体验局面×4个固定种子作相同输入审计；新建策略实例，记录叫牌、弃牌、出牌及判定检查。它们是设计局面，不代表自然实战失误率。

计时分别记录比赛中的动作墙钟耗时，以及单独审计的延迟。前者受并发、角色和比赛长度影响，后者也受JIT、缓存与样本局面影响；均不是纯CPU微基准。

## 运行

从仓库根目录，以新结果目录执行，避免覆盖已有实验：

```sh
dotnet run --project experiments/strong-plus-v1-20261106/Study.csproj -c Release -- /tmp/strongplus-new matches 0 45
dotnet run --project experiments/strong-plus-v1-20261106/Study.csproj -c Release -- /tmp/strongplus-new audit
```

`matches`最后两个参数为起始pair编号和数量，0–29为纯队，30–44为混合。审计附带一个仅把原Strong样本数改为32的`Budget32`控制项，用来观察同局面动作差异是否全由样本数解释；它不是额外的实战胜率对照组。

最终摘要见 `summary.json`，原始逐场结果与压缩牌谱在 `results/`，行为审计为 `results/audit.json`。`analyze.py`按整组配对重采样20,000次，并对全胜/全负配对作双侧精确符号检验；区间为探索性估计，未覆盖多轮未来调参的选择偏差。
