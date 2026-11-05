# 人类搭档体验版：partner-fast-v9

基于冻结的 `visibility-fast-v8` 新增 `PartnerFastV9`，目标是减少人类搭档容易感知的低级失误。Random、Weak、Medium、Fast、Strong 均不修改，也不替换旧默认 profile。没有训练或加载模型。

## 策略与边界

新类组合调用旧 Fast；同样使用 Medium 叫牌、弃牌最多四候选四样本、普通出牌早期四/后期八样本、末家例外最多两动作两样本。以下判断位于搜索之外，不扩大 rollout 预算：

1. **公开 Misère 确定性**：任意座位、花色均可判断。对亮牌庄家枚举合法回应，对未知防守手牌允许每张可能牌作保守回应；只有所有分支都让庄家本墩取墩时，才直接兑现。未知 Joker、庄家可以合法躲过时不会当成必破。未亮牌时，只在末家能当场确定结果的情况下使用该规则。
2. **自己的 Misère 避险**：末家有不取墩的牌时，丢掉其中最高的一张；首引有其他选择时，排除公开可证明必取墩的牌。被迫跟 A 正常接受，不误判。已破约后不反复做无意义的必破证明。
3. **保护搭档的赢家**：搭档已拿住当前墩，且后面玩家的所有可能牌都无法改变结果时，优先跟较小的同花色牌。仅凭“现在是搭档最大”不会触发；未知 Joker 等威胁会阻止判断。搭档反复引同一花色、自己只剩短套时，留给 Fast 处理可能的解封，不一律禁止跟大牌。
4. **公开缺门与搭档请求**：有将局中，搭档公开叫过将牌、已知缺某副牌、没有已知缺将，且没有明显对手将吃风险时，优先提供将吃机会；自己将牌较多时仍交给 Fast。采用明确约定：缺门时垫低副牌（点数不超过 7）表示请求回引；自己至少有三张且含 Q 以上、搭档未显示该门也缺、没有明显将吃风险时才响应。公开可兑现的非 Joker 大牌优先。这些是配合启发式，不是牌规或强度保证。
5. **弃牌结构**：保留正常合约的 Joker、丢弃 Misère 的 Joker；无将若弃牌拆出孤 K，尝试最多三次单牌交换，用低牌保留保护结构。保护 K 时不为此埋掉 A/J/Q/Joker，且仅接受启发式保留价值下降不超过 0.15 的修补；这里的 0.15 是牌型估值单位，不是计分点数。没有合适交换时保留 Fast 的取舍。
6. **排列稳定性**：按花色、点数固定自身手牌与合法出牌顺序，避免拖动手牌排列改变决策。它也会固定旧策略原本依赖输入顺序的平手选择；不是与旧 Fast 逐动作等价。

只使用 `Observation` 允许的公开历史、自身手牌、自身弃牌和规则。没有新增隐藏信息字段、读取审计牌谱，也没有引用测试场景或测试判定函数。信号通过公开历史自行推断，没有修改旧 Medium 的 `GoodSuit` 适配器。

`LastReason` 提供最近一次决策原因（如 `public-misere-certainty`、`protect-partner-winner`、`offer-partner-ruff`、`return-partner-request`），便于调试与后续接入动作说明；未自动增加任何玩家界面。

## 原有体验测试

沿用原有全部 17 个场景、检查条件和分级，每种策略每场景 16 次，种子 314159，两种手牌排列，共 544 次决策。没有通过修改判分来提高结果。

| 项目 | Fast v8 | Partner Fast v9 |
|---|---:|---:|
| 明确体验约束 | 92/112 | 112/112 |
| 战术/约定提示 | 143/160 | 160/160 |
| 强制动作控制 | 16/16 | 16/16 |
| 非法动作或异常 | 0 | 0 |

| 原来暴露的问题 | Fast v8 | Partner Fast v9 |
|---|---:|---:|
| 公开 Misère 立即兑现必破 | 0/16 | 16/16 |
| 搭档 A 稳拿时保留 K | 12/16 | 16/16 |
| 响应本测试声明的低梅花请求 | 8/16 | 16/16 |
| 利用搭档已公开的缺门 | 12/16 | 16/16 |
| 无将弃牌保留 K 的保护结构 | 11/16 | 16/16 |

全部通过仅说明满足这些固定局面的要求，不代表通用策略完美、所有战术提示都应强制执行，或真人满意度达到 100%。信号效果依赖人类知道并采用上述约定。

详见 [比较矩阵](../../Usability/results-partner-v9/REPORT.md)、[局面与动作分布](../../Usability/results-partner-v9/ux-results.json)、[逐决策记录](../../Usability/results-partner-v9/trials.jsonl)。新增回归还覆盖不同种子、全部四个座位及同色花色互换、未知 Joker、庄家可躲过、可能需要解封、单张无法支持请求、不应埋 A 保护 K 等反例。

## 少量实战回归

每组 8 对、16 场，同牌序交换队伍、轮换庄位；上限 200 副，无截断或非法动作。全部 48 场都已用正式引擎逐动作重放核对牌序、墩主与计分。

| 对阵 | 新版/被测方胜:负 |
|---|---:|
| 两名 Partner Fast v9 对两名 Fast v8，种子 2702001 | 8:8 |
| 一名 Partner Fast v9 + Medium 对两名 Medium，种子 2701001 | 9:7 |
| 一名 Fast v8 + Medium 对两名 Medium，同种子对照 | 11:5 |

直接交手持平，搭档代理对局中新版比旧版少赢两场。这组数据暂未显示明显实力崩塌，但不足以证明统计不劣，也没有证据宣称新版更强。Medium 只是稳定的搭档代理，不能代表真正人类的风格。没有按这 48 场的结果反复调参追求胜率。

逐组汇总及压缩完整牌谱在 [结果目录](../../Usability/results-partner-v9)。压缩牌谱供审计，包含隐藏牌；不应在真人局未结束时展示给玩家。

## 决策成本

在相同两场牌谱捕获的 79 次观察上，独立预热、每阶段最多 32 次决策，交换 profile 测量顺序。默认运行时下首测存在明显顺序效应，所以额外固定 `DOTNET_TieredCompilation=0`，排除分层编译切换的主要干扰，再各测两种顺序。

| 版本 | 两次顺序对照的平均决策耗时 |
|---|---:|
| Fast v8 | 5.97 ms |
| Partner Fast v9 | 5.93 ms |

此处先按观察数加权，再对两次运行取平均；单次均值分别为 Fast 5.89/6.04 ms、新版 5.47/6.38 ms。它支持这批观察上成本接近的判断，不作为普遍加速保证。默认编译模式的初测和顺序复测也一并保留，没有拿它们的单次差异宣称加速。完整阶段均值、P95、分配量见结果目录的 `timing-*.json`。

## 使用与复现

仓库根目录，.NET 8：

```sh
dotnet build TricksterBots.sln -c Release
dotnet run --project FiveHundredLab -c Release --no-build -- ux \
  --profiles visibility-fast-v8,partner-fast-v9 --trials 16 --seed 314159 \
  --out /tmp/partner-v9-ux-new
dotnet run --project FiveHundredLab -c Release --no-build -- bench-mixed \
  --a partner-fast-v9 --b medium-v1 --pairs 8 --seed 2701001 \
  --out /tmp/partner-v9-mixed-new
dotnet run --project FiveHundredLab -c Release --no-build -- bench-mixed \
  --a visibility-fast-v8 --b medium-v1 --pairs 8 --seed 2701001 \
  --out /tmp/partner-v9-mixed-fast
dotnet run --project FiveHundredLab -c Release --no-build -- bench \
  --a partner-fast-v9 --b visibility-fast-v8 --pairs 8 --seed 2702001 \
  --out /tmp/partner-v9-head-new
DOTNET_TieredCompilation=0 dotnet run --project FiveHundredLab -c Release --no-build -- profile \
  --input /tmp/partner-v9-mixed-fast/games.jsonl \
  --profiles visibility-fast-v8,partner-fast-v9 --games 2 --decisions 32 \
  --out /tmp/partner-v9-timing.json
dotnet test TricksterBots.sln -c Release --no-build
dotnet run --project FiveHundredLab -c Release --no-build -- verify-frozen --root .
```

计时顺序复测交换 `--profiles` 两项，使用新的输出路径。所有实验输出路径应为新目录/文件，避免覆盖历史记录。

接入人机局时使用 `Profiles.Create("partner-fast-v9", seed)`，它实现原有 `IPlayer`；将它安排在人类的对面座位即可，策略不假定人类一定在座位 2。该 profile 属于 FiveHundredLab，与旧 Fast 一样通过 Lab 注册使用。

完整测试 475 项通过，无失败或跳过。冻结检查覆盖 43 个源码/模型文件：原有 41 个保持一致，新版两份策略文件单独存入 `frozen-profiles-v3.json`。今后改变策略或参数应另取新名，不覆盖 V9 或 V8。
