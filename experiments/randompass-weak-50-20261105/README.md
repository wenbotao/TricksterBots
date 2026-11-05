# RandomPass 与 Weak：50场随机对战

[结果报告](REPORT.md) · [统计JSON](summary-050.json) · [验证记录](validation.json)

RandomPass 的正式实现见 [RandomPassBot.cs](../../FiveHundredLab/Bots/FiveHundred/RandomPassBot.cs)，profile 为 `random-pass-v1`。叫牌始终Pass，其他决策复用Random；原Random不变。

本实验采用默认规则路径，四座位独立等概率选择RandomPass或Weak，允许重复，每座位独立实例。49场正常结束；1场四个RandomPass持续全Pass，在1000次发牌上限处截断。该场不计胜负。胜率以已结束的座位参赛人次为分母，同场结果相关。

包含50份逐场结果及对应压缩完整牌谱、完整阵容/种子计划、运行元数据、汇总及原临时运行器文本快照。牌谱包含隐藏牌，仅供赛后审计。全部50份牌谱均已通过裁判重放验证；4项相关测试通过。metadata中的程序集指纹记录原实验构建，重建后不保证二进制指纹相同；validation另记录RandomPass源码指纹。

运行示例（新输出目录）：

```sh
dotnet run --project FiveHundredLab -c Release -- bench-mixed \
 --a random-pass-v1 --b weak-rules-v1 --pairs 2 --out /tmp/randompass-check
```

该示例是工厂接入验证，不等于原实验的四座位独立抽样。精确实验阵容见plan.json，临时随机运行器见Runner.cs.txt（不参与仓库编译）。原实验基于4e15ff9之后新增RandomPass的工作区执行，未对其他策略调参。
