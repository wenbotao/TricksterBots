# 本轮结果（2026-11-04）

本轮以旧策略冻结、同牌组、小规模反馈为原则。用户中途明确暂停预训练后，没有再训练模型或新增模型对战。以下推荐全部不使用模型。原有 15 个策略/规则/适配器源码文件的校验值保持不变。

## 主要结果

| 对照 | 场数 | 结果 | 用途 |
|---|---:|---|---|
| `visibility-fast-v6` vs Medium | 20 | 13:7 | 速度优先粗略验证 |
| 同牌组旧 Strong vs Medium | 同上前 20 场 | 8:12 | 配对基准，属于旧 Strong 的 100 场控制实验前缀 |
| `visibility-fast-v6` vs 旧 Strong | 8 | 4:4 | 小规模直接对战 |
| `visibility-fast-v6` + Medium vs 两个 Medium | 8 | 6:2 | 混合搭档兼容性 |
| `selective-quality-v2` vs Medium | 100 | 67:33 | 调整目标前已经启动并完成的较高预算验证 |
| 同牌组旧 Strong vs Medium | 100 | 51:49 | 同场控制，不应与另一牌组的 61% 直接混算 |

小样本用于快速淘汰明显退化配置，不能证明统计等价或非劣效性。没有把“未检出差异”等同于“证明没有下降”。不宣称新策略对任意对手、规则或搭档都优于旧 Strong。

`visibility-quality-v7` 是 `selective-quality-v2` 的可见性解析优化版，**只重跑了 4 场逐动作等价检查，结果完全一致**；没有把它描述成另行测过 100 场。`visibility-fast-v6` 与 `selective-fast-v4` 在全部 20 场中逐动作一致。`visibility-strong-v5` 与冻结旧 Strong 在另一组全部 4 场中逐动作一致，回归还覆盖了四类合约和不同 Joker/公开手牌情况。

## 相同观察的成本

取控制实验第一场完整比赛作为观察语料；每阶段最多评估 16 次，共 41 次（16 叫牌、9 弃牌、16 出牌）。每个策略独立预热，计时建议不改变裁判的记录动作。此结果是这些观察上的成本，**不是所有局面的总体耗时估计**。

| 阶段 | 观察数 | 旧 Strong 平均 ms | 速度版平均 ms | 质量版平均 ms |
|---|---:|---:|---:|---:|
| bid | 16 | 66.953 | 0.063 | 14.749 |
| discard | 9 | 115.652 | 12.094 | 38.345 |
| play | 16 | 40.589 | 7.997 | 18.598 |

按观察数加权：旧 Strong **67.35 ms**；速度版 **5.80 ms**（约 **11.6 倍**加速）；质量版 **21.43 ms**（约 **3.1 倍**加速）。完整 P50、P95、累计分配量、预热与语料指纹见 [results-summary-v1.json](results-summary-v1.json)。累计分配字节不是进程峰值内存，短期 GC/JIT、机器负载会影响时间。

## 没有晋级的试验也保留

- 初步阶段消融：各阶段只做 4 场，不据此断言强弱；但显示叫牌和弃牌的单次成本最高。
- `compact-strong-v1`、`cached-strong-v1` 保持旧动作，仅工程优化；四场完整对局匹配。
- `tactician-v1` 开发集 20 场 8 胜，`adaptive-sprint-v1` 9 胜，`adaptive-balanced-v1` 10 胜，没有作为推荐版本。
- `selective-joint-v1` 在旧固定的 200 场基准上 119 胜，旧 Strong 为 122 胜；表明能以更小成本接近旧强度，但不证明等价。
- 暂停前的首个预训练版本 `distilled-v1` 开发集 20 场仅 4 胜。第二版分阶段模型及 `value-guided-v3` 的开发记录保留，预训练路线仍处于暂停状态，不纳入本轮推荐。

## 复现（少量场次）

```sh
dotnet run --project FiveHundredLab -c Release --no-build -- bench \
  --a visibility-fast-v6 --b strong-v1 --pairs 4 --seed 2301001 --out /tmp/fast-direct-repeat

dotnet run --project FiveHundredLab -c Release --no-build -- profile \
  --input /tmp/fast-direct-repeat/games.jsonl \
  --profiles strong-v1,visibility-fast-v6,visibility-quality-v7 \
  --games 1 --decisions 16 --out /tmp/fast-cost-repeat.json
```

不要把不同观察语料的时间数值直接相除；先用同一输入比较各配置。每个新策略、参数和辅助算法的改变都需要新版本名。
