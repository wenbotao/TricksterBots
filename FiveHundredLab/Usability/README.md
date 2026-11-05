# 人类搭档体验测试 v1

这组测试回答“人类是否容易理解、信任自己的 bot 搭档”，与胜率、搜索耗时测试分开。被测对象固定为 Random、Weak、Medium、Fast（`visibility-fast-v8`）、Strong；没有修改任何 bot 的策略或参数。

本次实测与评估：

- [逐项通过矩阵](results-v1/REPORT.md)
- [评估结论与问题优先级](ASSESSMENT.md)
- [局面、合法动作、动作分布与构建指纹](results-v1/ux-results.json)
- [每次决策的原始记录](results-v1/trials.jsonl)

v1 初次评估时的验证结果：完整测试套件 464 项通过（含新增的 8 项评估器测试），没有跳过或失败；41 个冻结源码/模型文件指纹全部通过。评估运行中无非法动作或异常。

后续新增的人类搭档体验版为 `partner-fast-v9`，改进说明与对照结果见 [PARTNER_FAST_V9.md](../Archive/Reports/PARTNER_FAST_V9.md) 和 [新旧 Fast 比较矩阵](results-partner-v9/REPORT.md)。原有场景、检查条件、层级与默认五种 profile 没有改变。

## 场景与判定

默认规则为澳式四人、43 张牌、永久 Pass、Misère 庄家搭档停打、Open Misère 第一墩后亮牌。被测 bot 为座位 0，人类搭档为座位 2。其他玩家按剧本行动，代表人类与对手此前已经发生的合法行为。

17 个局面覆盖：弱牌竞叫、搭档已 Pass、支持搭档花色、足够出局时停止加叫、保护搭档赢家与自己的后续控制牌、不必要将吃、回应约定垫牌信号、利用已知缺门、Misère 首引/躲墩/防守/危险高牌处理、Open Misère 公开必破、三种弃牌问题，以及被迫吃墩的反例控制。

18 条检查分三层统计：

| 级别 | 含义 | 例子 |
|---|---|---|
| Critical | 本测试明确要求的体验底线；不是完整的最优策略证明 | 有低牌可躲仍在自己的 Misère 吃墩、救下正在取墩的 Misère 对手、明知必破却不立即兑现 |
| Advisory | 有合理战术例外，或依赖明确声明的搭档约定 | 垫低梅花请求回引、给缺门搭档提供将吃机会、保留无将 K 的保护牌 |
| Control | 检查评估器不误报；不计入体验合格率 | 唯一合法牌为 A，必须跟出并取墩 |

低牌垫牌请求不是通用规则，本测试显式声明该约定；不符合只能表示没有体现该约定，不能自动认定 bot 程序有错。人类公开叫过某花色也不意味着 bot 永远不能改叫其他花色，因此相关检查属于 Advisory。

“及时兑现公开必破”是一项刻意加入的体验要求：庄家公开手牌可以证明某首引立刻破约，就优先结束对手的 Misère。另一个首引可能最终也能破约，仍不满足这项要求；**这不等于证明另一个首引降低胜率**。

每个局面、每个 profile 测 16 次：8 个种子，每个种子分别使用原始、反转两种手牌排列，创建全新的 bot。总计 1,360 次决策。确定性策略在相同输入上重复动作是正常现象。通过次数表示这些固定局面的行为稳定性，不能当作真实对局失误率、真人满意度、独立样本胜率或综合 bot 排名。Weak 不主动叫 Misère，但仍测试其处在该角色时的能力；这些局面不代表它在自然对局中的出现频率。

## 可达性与信息边界

`UxCaptureV1` 将预留牌填成完整的 43 张无重复牌，反演固定洗牌，再通过正式 `Engine` 执行叫牌、弃牌和出牌剧本。引擎会验证前缀中的所有行为是否合法。在指定决策点捕获 Observation，才开始测试 bot；没有手工拼凑不可能到达的墩次或亮牌状态。

调用 bot 前会移除用于固定发牌的自定义 Rules，只传标准规则和正常 Observation。其他玩家的手牌只有在 Open Misère 按规则公开后才可见；没有额外传递未见底牌或其他玩家的弃牌。本人按规则取得的底牌、自己丢弃的牌正常可见。完整初始分配单独保存在结果的 `InitialHandsForAuditOnly`、`KittyForAuditOnly` 字段，仅供复核。局面不是随机抽取的实战样本，未指定的牌使用固定顺序补齐。

公开必破判定枚举庄家全部合法回应，并对人类可能持有的每张未知牌进行保守检查；不读取审计用完整手牌。例如该场景引 ♥5 时，庄家公开持有 ♥8–A，Joker 在 bot 手里，庄家必拿；引 ♦4 时，未知的人类手牌可能含 ♦A，因此不能证明本墩必破。

返回非法动作、异常与体验检查失败分开记录。JSONL 包含每次 profile、种子、排列、原始动作、合法性、异常、检查结果及耗时；JSON 汇总动作分布、局面输入、合法动作、评分要求和程序集 SHA256。计时是串行场景执行的附带诊断，受首次加载等因素影响，不是性能基准。

## 运行

仓库根目录，需要 .NET 8，依赖初始化沿用项目已有说明：

```sh
dotnet build TricksterBots.sln -c Release
dotnet run --project FiveHundredLab -c Release --no-build -- ux \
  --trials 16 --seed 314159 --out /tmp/fivehundred-ux-new
dotnet test TestBots/TestBots.csproj -c Release --no-build \
  --filter FullyQualifiedName~TestFiveHundredUsability
dotnet run --project FiveHundredLab -c Release --no-build -- verify-frozen --root .
```

输出目录不能已有 `ux-results.json` 或 `trials.jsonl`，避免覆盖。`--trials` 为 2–256 的偶数，默认 16，默认种子 314159。快速反馈可设为 2；更高次数增加随机策略/搜索种子覆盖，不增加局面种类。

可用 `--profiles visibility-fast-v8,partner-fast-v9` 显式比较新旧策略，省略时仍测原来的五种 bot。profile 必须已注册且不重复；改变测试对象不改变场景或判定标准。

命令成功表示完成评估，不表示五种策略均满足 UX 要求；Random 的失败本来就是预期行为。CI 测试验证剧本可达性、信息隔离、正反例、异常分类、种子复现和报告计数，不把 Random 未通过体验检查当成测试程序失败。新场景可在 `UxFixturesV1` 注册；若改变已有场景、规则、判定或评分层级，应使用新版本重新出报告，不覆盖 v1 结果。
