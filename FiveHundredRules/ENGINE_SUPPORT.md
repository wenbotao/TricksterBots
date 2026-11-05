# 配置引擎接入与约束

## 使用

`new Engine()` 或 `Engine.FromConfiguration()` 保持原默认规则；`Engine.FromConfiguration(path)` 导入展开配置。命令行：

```sh
dotnet run --project FiveHundredArena -c Release -- run \
 --rules FiveHundredRules/presets/au-reference-4p-v1.json \
 --a random-legal --b random-legal --pairs 1 --out /tmp/au-config-game
dotnet run --project FiveHundredArena -c Release -- replay /tmp/au-config-game/games.jsonl
```

原扁平 Rules JSON 仍可用。新配置结构和跨字段校验先于开局执行；不认识的字段、缺少字段、错误排序和不支持的流程均报错，不静默回退。预设及模板的 metadata.integrationStatus 是原规格标记，不是运行时能力声明；以本支持说明和加载校验为准。

六份已交付配置均可导入并运行。已支持43/46张牌、3/6张底牌（同样可按清单支持45/5）、分包发牌、单轮/多轮、Inkle、永久/可重入Pass、指定普通阶数、特殊排序与门槛、按合约取底/首攻/停打/亮牌、Joker策略、普通/零墩计分、Slam、防守封顶和分数阈值终局。

Joker预命名由可选的 `IJokerDeclarationPlayer` 提供；未实现此接口的玩家选择不预命名，仍可正常执行澳式参考。声明作为公开决策保存、重放。旧bot策略没有修改；现已新增六个规则版策略，命令行新格式可选random-legal、weak、medium、fast、strong、partner。规则版入口和支持边界见BOT_RULES.md，不将其视为旧策略逐动作等价的变体。

完整展开Document及类型信息随牌谱保存，重放不重新读取可变的预设文件。运行时只给玩家自身牌和公开上下文。特殊手牌公开及Joker声明按阶段执行。

## 尚未实现，加载时明确拒绝

- Double/Grand Nullo、team_zero目标、庄家控制两手和私看搭档牌。
- 自加叫、改变定约后重新开放叫牌、强制庄家叫牌、无定约NT。
- Ace–no-face申请重发、固定副数赛事、提前结束打牌。
- 公开弃牌/整队手牌、仅本人的历史资格门槛。
- 非定约方优先的同时达标裁决、正分优先于负分的终局顺序。
- 不声明花色的Joker首引。

因此“Schema通过”不等于任意自定义组合均已可运行。高级流程仍需后续实现，不能宣称整个自定义目录已全部支持。

## 冲突与制约

1. **牌组守恒**：牌总数=40+底牌，牌身份唯一；Joker排序必须恰好覆盖实际Joker。分包手牌总和10、底牌总和匹配，分包位置须对应发牌模式。
2. **叫牌集合**：排序恰好覆盖启用的普通和特殊合约。分值变化不修改顺序。正式阶数上限不得低于任一位置起叫门槛；特殊合约资格必须可达。
3. **单轮与重入冲突**：单轮需四次机会、不可重入及自加叫。Inkle仅单轮启用，须有允许位置，不作为最终合约。多轮永久Pass需其他人全部退出；允许重入需连续三Pass结束。
4. **重发与轮庄**：每次发牌轮庄对应全Pass换庄；只在打过牌后轮庄对应全Pass保留庄家。矛盾配置拒绝。
5. **参与者与信息**：停打不等于交出手牌；两手控制需要四个行动位置及庄家可见搭档牌，不能只切换停打开关。没有公开事件不能公开弃牌。
6. **Joker**：是否可出、强制出、领哪门、牌力、命名是独立约束。已命名Joker按其花色跟牌，不能按未命名万能大牌裁决。无Joker不能启用强制出/预命名。先前领门与本人显示缺门不可混用。
7. **动态无合法动作**：某些领门限制与剩余手牌可形成死局。现阶段运行时明确抛错并保存部分牌谱，不伪造动作；静态校验无法证明所有可能牌局无死局。此类配置需要额外可达性验证后才能用于可靠比赛。
8. **计分独立性**：防守墩分与破约取得定约分若都启用即相加；零墩按独立收益规则。Slam只用于普通合约。防守封顶只限制新入账，不能把已有520分倒扣到490。
9. **终局与结算**：目前只支持完整打完后结算、负分判负优先、同时正向达标定约方优先。固定副数、并列策略和提前终止不能仅更改字段而沿用旧流程。
10. **bot能力**：规则合法不代表旧bot支持。双Joker、Inkle和命名等影响动作及信息结构；不得静默转换成旧澳式局面。

配置生成时的 validation-report.json 和 sources 中源文件哈希是接入前历史记录。引擎基础文件本次获授权变更，原冻结清单另存 sources/frozen-before-configured-engine.json，现行清单仅更新 Engine/Model/Rules/Program 四份基础文件；策略源码指纹不变。

## 验证

测试文件 `TestBots/TestRuleConfiguration.cs` 覆盖六份配置的运行、序列化及重放、默认快照对照、计分/排序独立性、双Joker牌组、单轮Inkle、强制Joker与命名Joker、结构错误及冲突拒绝。运行：

```sh
dotnet test TestBots -c Release --filter FullyQualifiedName~TestRuleConfiguration
```

当前测试并非穷举全部自定义组合；动态死局与尚不支持的流程明确保留为边界。
