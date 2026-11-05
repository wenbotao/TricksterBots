# 六种正式 bot 策略整合

日期：2026-11-05。此次是保持行为的代码归属调整，没有改变强度定位、规则支持范围、搜索预算或参数。

## 新的策略开发入口

`Bots/FiveHundred` 的六个 bot 文件分别包含自己的默认及配置路径决策。公共基类只负责规则绑定和诊断，正式类不再委托共享的 `RuleAwarePlayer`，也没有 `RuleBotLevel` 策略分支。

- Random：随机动作与 Joker 命名。默认动作随机流与命名随机流仍分开，配置路径保持原动作级抽样。
- Weak：默认局部策略、配置叫牌门槛、弃牌排序和出牌战术。
- Medium：完整迁入原 `FiveHundredBot` 及适配代码；默认策略直接继承 `BaseBot<FiveHundredOptions>`。规则版判断和供 Fast 使用的可见手牌变体也在本文件。
- Fast：自己的叫牌、弃牌候选、出牌裁剪、预算和搜索收益门槛。使用 Medium 文件中的可见手牌变体作为默认基准和续演策略。
- Strong：默认竞叫续演、弃牌与出牌搜索，以及配置路径的启发式竞叫和宽候选搜索。Medium 默认基准及续演改为调用新 Medium 文件中的实现。
- Partner：默认和配置两条路径的体验保护均在本文件，以组合方式调用新 Fast；不再调用旧 PartnerFastV9。

公共计算保留在 `RuleBotEvaluation`、`DefaultRollout`、`ConfiguredRollout`、`Shared/CardFrame` 和 Arena 的采样/牌力工具。默认续演器由调用者注入策略；配置续演器调用新 Medium 的规则战术。选择哪些候选、搜索预算、比较门槛与最终动作由 bot 文件负责。

## 兼容性

默认规则和显式新格式配置的策略差异继续保留。规则版 Strong 叫牌仍为原来的计分启发式，未借整合改为旧 Strong 的搜索竞叫。`Name`、配置接口与 `LastRollouts` 原语义保持。

直接实例化公共类、`Profiles.CreateForRules` 或 Lab 的六个 `*-rules-v1` profile 会使用新实现。旧版本 profile、Arena 自身 CLI 的旧入口以及 Adapt 兼容代码继续使用冻结实现，以保证历史实验可复现；修改正式 bot 不会自动修改历史入口。

旧 `FiveHundredBot.cs` 和 `Trickster.Bots.MediumBot` 未改，继续供旧平台和历史测试使用。正式 Medium 不再调用或继承它们。历史 Legacy 策略移到 Archive/Strategies，纯牌表工具移到 Shared，位置映射见 `consolidation-relocations.json`。

原 v8 包装文件与修改前的 Arena 程序集声明以 `.cs.txt` 保存在 Archive/Snapshots/formal-wrappers-v1，原指纹不变、只迁移清单路径，不参与编译。整合后的源码另记 v9 清单；没有用新源码覆盖历史指纹来声称历史内容未变。

## 验证方式

- 原有 `TestCleanup`：六种 bot × 六份规则，36 份优化前行为快照。
- 原有 `TestRuleAwareBots`：配置导入、自然及固定竞叫上下文、重放、规则不匹配、搜索预算和体验保护。
- 新增 `TestBotConsolidation`：在全部体验局面上逐动作对照冻结实现，覆盖默认及配置两条路径；对普通、Misère、Open Misère 强制合法竞叫后的完整十墩逐项对照；检查 Medium 的继承关系。
- 原有体验测试、全量回归及所有历史/新源码指纹校验。

行为一致的结论限于上述覆盖范围，并非对所有可能局面的形式化等价证明。此次不重新评估胜率，也不宣称计算速度提升。

最终验证：Release 全量测试 **514/514 通过**，无失败或跳过；36 份配置行为快照一致；默认普通/零墩完整十墩对照18组；体验局面逐动作对照204次；新旧源码/模型指纹72项通过。机器记录见 [consolidation-validation.json](consolidation-validation.json)。
