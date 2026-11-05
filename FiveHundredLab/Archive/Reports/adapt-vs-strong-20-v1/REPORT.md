# Adapt 与原 Strong：20组配对实战

Adapt 是第三阶段完整机制的命名封装，搜索参数仍为8样本/12弃牌候选/改善门槛8/标准误系数0.75。旧阶段别名保留，Strong为原搜索版strong-v1，不是启发式叫牌的规则版。

## 实验设计

默认澳式规则。种子2711001固定生成20组计划，根座位每个位置5次。其余三个座位从Random、Weak、Medium、Fast v8、Strong、Partner v9中独立等概率、有放回抽取；整个单场保持阵容不变。两次实战使用相同阵容、同一座位的玩家初始RNG种子、相同发牌种子与庄位；每场重新创建玩家。Adapt不获知其他人的bot名称，模型每场重新初始化，只在本场内学习。

每副牌的生成由相同种子和副序号决定，所有共同副数的初始手牌和底牌均已逐项比较。两次因决策不同可在不同副数结束；不强制重放其他玩家的动作，那样会失去正常实战含义。其他玩家即使初始RNG种子相同，也会因局面与调用次数不同产生不同后续动作。

独立预热后逐组交替执行顺序，200副上限；全部40场自然结束、无截断、通过引擎重放。计时为各自真实局面的bid/discard/play平均，非同观察微基准；可选Joker声明不计入该平均。

## 总体结果

| 指标 | Adapt | Strong |
|---|---:|---:|
| 胜场 | 14 | 14 |
| 负场 | 6 | 6 |
| 平均终局本队分 | 59.5 | 73.0 |
| 平均终局分差 | 130.0 | 181.0 |
| 平均副数 | 4.85 | 4.05 |
| 决策次数 | 1123 | 934 |
| 平均决策耗时ms | 44.15 | 40.44 |
| 总决策耗时s | 49.58 | 37.77 |

配对结果：共同胜 13 组、共同负 5 组；Adapt独胜 1 组、Strong独胜 1 组。双方均14/20（70%），胜场差为0。仅2组结果不同，精确配对符号检验双侧p=1.00。

平均配对分差差值（Adapt−Strong）为 -51.0，粗略配对t区间约 [-256.2, 154.2]；20组分布不保证正态，这个区间仅辅助说明不确定性。不能据此证明二者等强或一方稳定更强。

本轮Adapt实际平均决策约慢 9.2%。总决策用时还受到Adapt平均副数更多的影响，不全部归因于单次算法开销。其叫牌权重更新总计367次、出牌偏好更新31次；这些是处理次数，不是确认识别正确的次数。

结论：这批随机混合阵容中，没有观察到Adapt的胜率收益；分差均值较低、计算成本略高。样本不足以作总体强弱结论。每场学习窗口较短，Random等策略也未被三个叫牌风格模型精确覆盖；这些是可能限制，未经消融不能认定为原因。

## 每组明细（座位从0开始）

| 组 | 根座位 | 队友 | 对手 | Adapt结果/分数 | Strong结果/分数 |
|---:|---:|---|---|---|---|
| 1 | 0 | partner-fast-v9 | weak-v1/strong-v1 | 胜 680:-460 | 负 140:520 |
| 2 | 1 | random-legal-v1 | strong-v1/medium-v1 | 负 -920:50 | 负 -520:30 |
| 3 | 2 | random-legal-v1 | visibility-fast-v8/partner-fast-v9 | 负 -820:180 | 负 -920:120 |
| 4 | 3 | weak-v1 | partner-fast-v9/medium-v1 | 胜 520:310 | 胜 570:190 |
| 5 | 0 | strong-v1 | random-legal-v1/partner-fast-v9 | 胜 100:-740 | 胜 100:-740 |
| 6 | 1 | medium-v1 | medium-v1/medium-v1 | 胜 620:510 | 胜 500:40 |
| 7 | 2 | medium-v1 | random-legal-v1/random-legal-v1 | 胜 100:-520 | 胜 100:-520 |
| 8 | 3 | partner-fast-v9 | strong-v1/random-legal-v1 | 胜 80:-520 | 胜 130:-880 |
| 9 | 0 | visibility-fast-v8 | visibility-fast-v8/partner-fast-v9 | 负 -380:770 | 负 70:720 |
| 10 | 1 | weak-v1 | medium-v1/random-legal-v1 | 胜 80:-520 | 胜 80:-540 |
| 11 | 2 | medium-v1 | visibility-fast-v8/medium-v1 | 胜 600:260 | 胜 510:190 |
| 12 | 3 | random-legal-v1 | strong-v1/strong-v1 | 负 -940:140 | 负 -960:80 |
| 13 | 0 | random-legal-v1 | partner-fast-v9/visibility-fast-v8 | 负 -560:60 | 负 -980:60 |
| 14 | 1 | visibility-fast-v8 | medium-v1/visibility-fast-v8 | 负 -90:510 | 胜 550:550 |
| 15 | 2 | medium-v1 | medium-v1/strong-v1 | 胜 790:370 | 胜 720:50 |
| 16 | 3 | random-legal-v1 | random-legal-v1/random-legal-v1 | 胜 40:-520 | 胜 40:-520 |
| 17 | 0 | medium-v1 | random-legal-v1/weak-v1 | 胜 110:-700 | 胜 110:-700 |
| 18 | 1 | medium-v1 | random-legal-v1/random-legal-v1 | 胜 80:-520 | 胜 80:-520 |
| 19 | 2 | weak-v1 | medium-v1/medium-v1 | 胜 600:100 | 胜 570:20 |
| 20 | 3 | weak-v1 | strong-v1/visibility-fast-v8 | 胜 500:-170 | 胜 570:-310 |

## 复现

```sh
dotnet run --project FiveHundredLab -c Release -- adapt-check \
 --games 20 --seed 2711001 --out /tmp/adapt-strong-new
gzip -dc FiveHundredLab/adapt-vs-strong-20-v1/games.jsonl.gz > /tmp/adapt-strong-games.jsonl
dotnet run --project FiveHundredArena -c Release -- replay /tmp/adapt-strong-games.jsonl
```

plan.json保存阵容和全部种子；results.json保存每场成绩、阶段计时和学习次数；games.jsonl.gz保存完整审计牌谱，不能作为未结束真人局的公开数据。
