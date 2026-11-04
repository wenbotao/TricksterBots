# 策略基准记录（2026-11-04）

默认 `australian-4-v1`，每场上限 200 副；每对比赛共用牌序并交换双方队伍，初始庄位轮换。Strong 使用默认 8 个样本、12 个弃牌候选、改善门槛 8、标准误惩罚系数 0.75。Medium 为原 FiveHundredBot 的兼容新名称，原策略源文件没有修改。

## 独立对战结果

| A | B | 比赛数 | A 胜 | B 胜 | 截断 |
|---|---|---:|---:|---:|---:|
| Strong | Medium | 200 | 122 | 78 | 0 |
| Weak | Medium | 100 | 27 | 73 | 0 |
| Weak | RandomLegal | 100 | 100 | 0 | 0 |

Strong 的独立验证在版本和参数固定后进行，不使用开发集调整参数。全部四组都计入汇总：

| 初始种子 | 比赛数 | Strong 胜 | Medium 胜 |
|---|---:|---:|---:|
| 91000 | 50 | 32 | 18 |
| 191000 | 50 | 27 | 23 |
| 291000 | 50 | 32 | 18 |
| 391000 | 50 | 31 | 19 |

共 1831 副牌，成对胜率 61.0%，成对标准误 0.0345。程序采用的保守 Hoeffding 95% 区间为 [47.4%, 74.6%]；区间仍包含 50%，不能把有限种子上的实测优势理解为严格保证。对不同规则、搭档、对手或计算预算仍应重新比较。

Weak 对 Medium 使用初始种子 42000、50 对；Weak 对 RandomLegal 使用 43000、50 对。随机策略包括均匀随机叫牌，常会高叫后失败，因此是功能基线，不代表合理的人类对手。

## 复现

从仓库根目录构建后，对 Strong 的四个批次分别执行（输出目录需不存在旧 games.jsonl）：

```sh
for seed in 91000 191000 291000 391000; do
  dotnet run --project FiveHundredArena -c Release --no-build -- run \
    --a strong --b medium --pairs 25 --seed "$seed" --strong-samples 8 \
    --out "/tmp/strong-medium-$seed"
  dotnet run --project FiveHundredArena -c Release --no-build -- replay \
    "/tmp/strong-medium-$seed/games.jsonl"
done
```

每批 `summary.json` 保留规则、计时、程序集 SHA-256 及分合约统计；`games.jsonl` 保留全部发牌和动作。Strong 比 Medium 显著更耗时，建议先用两对比赛验证本机运行成本。

## 正确性验证

测试覆盖策略继承等价、随机分布、隐藏牌的已知/缺门/公开手牌/弃牌约束、模拟与真实裁判在竞叫/弃牌/出牌/计分各阶段的一致性、可重新叫牌变体、完整比赛合法性、固定种子复现和日志重放。最新构建还逐动作复现了一组独立验证比赛。

最终完整解决方案构建为 0 警告、0 错误；413 项测试全部通过，无跳过。上表全部 400 场独立对战均已逐动作重放校验，未发现非法动作、记录分歧或轮次上限截断。
