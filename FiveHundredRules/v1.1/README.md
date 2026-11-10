# Five Hundred v1.1 规则与默认值

依据用户提供的《FiveHundred_v1.1.0_版本需求_v0.1.1_草案_20261106.xlsx》实现。逐行文本提取见 [sources/workbook-extract.txt](sources/workbook-extract.txt)。文档里的界面布局说明不作为仓库开发指令；本仓库提供规则配置、引擎和玩家回调，尚无人机游戏设置页或弹窗。

## 默认与旧版

- `RuleConfiguration.Load()`、`new Engine()`、`Engine.FromConfiguration()` 默认读取嵌入的 [澳式 v1.1](presets/au-default-4p-v1-1.json)。不依赖当前目录。
- `RuleConfigurationV11.Create("american")` 读取 [美式 v1.1](presets/us-default-4p-v1-1.json)。
- `new Rules()` / `RuleConfiguration.Legacy()` 明确保留旧规则；旧 JSON 和旧 bot 继续用于回放，不把旧实验改名为新规则成绩。
- 新 bot 位于 `FiveHundredLab.Bots.FiveHundred.V11`，工厂名称以 `-v1.1` 结尾。原目录中的同名类仍是冻结历史版本。工厂收到 v1.1 配置及旧正式别名时，会选择相应 v1.1 类。

文档未要求改变的澳式选项沿用原默认：43 张、逐张发牌、初庄 3、7 阶后允许普通零墩、8 阶后允许明牌零墩、原计分和终局。没有顺便改成旧 AU 参考预设的分包发牌/随机初庄。澳式新默认：Open Misère 排在 10♦/10♥ 之间，第一墩完成后明牌；未命名 Joker 仅缺门跟牌，零墩缺门强制 Joker，领未领过的门；无将/零墩中所有在打牌的持有者可在第一墩前提名；Joker 为高牌。新特殊合约均默认关闭，重发时换庄开启。

美式按文档 3.2：43/3、单轮、Inkle 开启，普通零墩在 8♠/8♣ 之间，Open Misère 启用且可随时叫、位于 10♦/10♥ 之间；Joker 领未显示缺门的花色，提名关闭；第一墩后明牌，零墩防守方按定约者吃墩数每墩 10 分。普通正式合约仍从旧美式预设的七阶起，六阶 Inkle 只传递信息。

## 配置方式

完整 JSON 可直接修改，再用 `RuleConfiguration.Load(path)` 读取。新 `RuleVariants` 是在预设上生成完整配置的便利入口，每次从预设重新生成，不残留上一次启用特殊合约后的分值/序位：

```csharp
var rules = RuleConfigurationV11.Create("australian", new RuleVariants {
    AceNoFace = true,
    AllPass = "play_no_contract_nt", // redeal / force_dealer / play_no_contract_nt
    RotateDealerOnRedeal = false,
    MisereWithoutPickup = true,
    GrandNullo = true,
    GrandSlam = true
});
var bot = new FiveHundredLab.Bots.FiveHundred.V11.StrongFlowCachedBot(7, rules);
var game = new Engine(rules).Run(players, seed: 123);
```

选项值与 JSON 相同：`JokerFollow` 为 only_when_void / anytime / first_lead_of_suit_or_void；`JokerLead` 为 any / never_led / previously_led_by_holder / not_shown_void / last_card_only。OpenTiming 为 before_first_lead / after_first_trick。MisereRanking 为 between_7NT_8S / between_8S_8C / by_points；OpenMisereRanking 为 between_9NT_10S / between_10D_10H / by_points_higher / by_points_lower / by_points_first。同分先到先得使用相同序位，双方均不能用同分合约压叫，而非仅交换数组中的两个位置。

## 新增流程与信息边界

| 文档项 | 执行方式 |
| --- | --- |
| 1.1 Ace–no face | 恰好一张 A，且没有 J/Q/K/Joker，才询问 RequestRedeal；不是“没有 A”。单机队友同意视为总是同意。申请在竞叫前，重发是否换庄由同一配置控制。 |
| 1.2 庄家 Pass | 前三人均 Pass 时，可重发、强制庄家合法叫牌，或开始无合约 NT。无合约双方各得本队墩数×10，不借用庄家充当定约者。 |
| 1.3/1.4 序位 | 显式完整 orderLowToHigh 独立于分值；便利生成器支持文档新增位置及按分值三种同分处理。 |
| 1.5/2.2 Joker | 记录本副全部领牌、持有者领牌与公开显示缺门。允许随时跟出的未命名 Joker 不作为缺门证明。 |
| 2.1 明牌 | 严格以一墩结束为界；三人零墩为第三张出完之后，不能在第三张出牌前泄露。 |
| 2.3 提名 | 所有在打牌的 Joker 持有者均可提名或取消。提名只适用于无将/零墩范围（含无合约NT），有将时Joker仍为最高将。提名公开，其他人的手牌仍隐藏；采样固定该 Joker 的公开持有者，不能假设一定属于定约者。 |
| 2.4 低 Joker | 影响零墩中未命名 Joker。已经命名的牌按 2.3 为指定花色最高牌，并按该门跟牌；缺门强制Joker只针对未命名牌，此优先关系明确保留。 |
| 2.5 No Pickup | 不看/不拿底牌，不弃牌；350 分，在 9♠/9♣ 之间。开启时同时将普通零墩变为 150 分、7♠/7♣ 间，明牌零墩变为 250 分、8♠/8♣ 间。 |
| 2.6 Grand Nullo | 队友先叫普通零墩才可叫，序位紧随 Open Misère；510 分，四人都打，定约队合计必须零墩。定约者取底弃回，再由队友取交换后的底牌再弃回。 |
| 2.7 重发换庄 | Ace–no face 与全 Pass 重发均执行该开关；正常打完一副仍换庄。 |
| 2.8 Grand Slam | 普通合约中标并看底牌后、弃牌前询问 DeclareGrandSlam。队友通过 TransferCard 私下给一张后停打；定约者多弃一张、独吃十墩。有将500、NT520，失败-500，对手每墩10分。 |

取底、弃牌和交牌只在参与者的 PrivateEvents 留下历史观察。Grand Nullo 第一次弃回的牌在队友再次取底后，不再被错误标作第一位玩家“已知仍在死牌堆”的牌；对手看不到这段私有记录。提名是公开事件，交牌的具体牌不是。

`IRuleVariationPlayer` 提供 RequestRedeal / DeclareGrandSlam / TransferCard；这些决定也写入 Decision，ReplayPlayer 可逐步重放。旧牌谱不会因为新增空字段而改变规范化决策摘要。

## 组合约束与明确解释

- 按用户确认，文档 2.6 开关说明中复制自 2.5 的“启用无底牌零墩”修正为启用 Grand Nullo；该合约紧随 Open Misère。
- No Pickup 是分值与序位成套选项，不同时接受另一个相冲突的 Misere/Open 排序覆盖；若要完全自定义，可直接编辑完整 JSON 并显式表达最终状态。
- Grand Nullo 使用四个活动座位、team_zero 与 sequential_team_exchange；单人零墩仍排除定约者队友，不能混用目标和人数。
- 第一墩前的事件顺序为取底/交换、独战与交牌（若适用）、弃牌、可选Joker提名、开局明牌（若选择）再首攻；另一明牌选项在第一墩完整结束后触发。
- Grand Slam 只在普通合约的取底阶段声明；不进入竞叫序位，不能对零墩或无合约 NT 再声明。
- 低 Joker 与提名组合时，已提名牌仍是该门最大；不把随时可跟 Joker 的行为当作公开缺门。
- 领牌条件无可选花色时，保留项目既有“最后一墩可选任意门”的明确兜底；last_card_only 仍严格要求手中仅一张。
- 无合约 NT 若仍要求“必须完成合约才能出局”，这副只加分而不会凭正分出局；允许防守出局时两队对称判断，同时达标取较高总分，同分继续，不让虚构的定约队取得优先。
- Inkle 是信号叫牌，不成为正式合约，也不算 Pass。“前三人全 Pass 时强制庄家”的条件只认实际 Pass；此前只有 Inkle、无人正式叫牌时，仍可能重发。若希望所有无人正式叫牌的情形都强制庄家，需要另行约定条件。

[all-features 示例](examples/all-features-4p-v1-1.json) 是完整的可运行混搭配置。默认规则下未启用的新特殊流程在测试中单独强制触发，不能用普通默认实战代替这些分支的验证。

专项边界、九版 bot 在澳/美预设下的特殊流程与新增540场随机对战，见[扩展验证报告](../../../experiments/v11-compliance-expanded-20261110/README.md)。
