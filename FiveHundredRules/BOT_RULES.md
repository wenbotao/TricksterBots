# 六种 bot 的规则配置接入

## 入口与版本

旧 Random/Weak/Medium/Fast/Strong/Partner 源码和参数保持不变。新增六个规则适配版本：

| 历史策略 | 规则版名称 | 新格式配置中的决策 |
|---|---|---|
| random-legal-v1 | random-rules-v1 | 在引擎提供的合法动作中随机选择；可随机选择合法的 Joker 预命名或不命名 |
| weak-v1 | weak-rules-v1 | 局部牌力和保守叫牌、简单弃牌/出牌，不搜索 |
| medium-v1 | medium-rules-v1 | 计分驱动叫牌、牌型弃牌、规则感知的赢墩/避墩判断，不调用旧平台适配器 |
| visibility-fast-v8 | fast-rules-v1 | 最多4个弃牌/出牌候选、4个样本，完整续演 |
| strong-v1 | strong-rules-v1 | 最多12个弃牌候选、全部合法出牌候选、8个样本，完整续演 |
| partner-fast-v9 | partner-rules-v1 | Fast规则版预算，加公开必破、保护赢家、缺门/低牌请求、弃牌结构修补 |

这是**新命名的策略移植版本**，不是宣称旧策略在所有变体中逐动作等价。默认不传规则时委托对应旧策略，保持原决策；显式传入新格式（包括legacy兼容预设）时采用规则版。变体下的强度排序、胜率和体验尚未系统校准，合法性/导入测试不能证明它们仍具有原版同样的水平。

六种策略共用配置检查、信息边界和规则续演设施，保留不同的策略层级与搜索预算。当前所有非随机规则版都用启发式期望计分叫牌；Strong 的完整续演用于弃牌/出牌，**没有移植旧 Strong 的叫牌竞叫续演**。它不是在所有阶段都等价于旧 Strong，不能据其名称作这样的推断。

引擎仍是最终裁判；bot使用传入Observation里的规则上下文（包括公开Joker命名），不使用自己重新构造的旧澳式Rules。绑定的配置与观察不符时抛错，不取其中一份偷偷继续。

## 使用

推荐在同一处读取一次，传给引擎及所有bot：

```csharp
var rules = RuleConfiguration.Load(path); // path=null：原默认规则
var players = new[] {
    Profiles.CreateForRules("medium-rules-v1", 11, rules),
    Profiles.CreateForRules("strong-rules-v1", 12, rules),
    Profiles.CreateForRules("fast-rules-v1", 13, rules),
    Profiles.CreateForRules("partner-rules-v1", 14, rules)
};
var game = new Engine(rules).Run(players, seed: 123);
```

`Profiles.CreateForRules(name,seed,rulePath: path)` 可自行读取文件；推荐上一种共享快照方式，避免中途文件变化。name同时接受表中的历史名字；输出名字明确标记为规则版。

Arena（新格式下增加fast、partner选择）：

```sh
dotnet run --project FiveHundredArena -c Release -- run \
 --rules FiveHundredRules/presets/canadian-platform-reference-4p-v1.json \
 --a strong --b medium --pairs 1 --out /tmp/configured-bots-new
```

Lab（六种策略均可用；省略--rules保持历史路径）：

```sh
dotnet run --project FiveHundredLab -c Release -- bench-mixed \
 --rules FiveHundredRules/presets/us-single-round-4p-v1.json \
 --a partner-rules-v1 --b medium-rules-v1 --pairs 1 \
 --out /tmp/configured-partner-new
```

Lab的引擎、被测bot及mixed搭档都使用同一份配置。新格式下停用旧Medium动作差异基准计数，因为旧适配器不支持这些变体；计时和正常胜负统计仍保留。Arena的--strong-samples对规则版Strong也生效，非8样本使用单独的名称后缀。

## 规则对决策的影响

- 分值、破约双方收益、零墩防守分、防守封顶和终局分数都会进入预期收益或续演结算，不只是通过JSON校验。
- Inkle当作信息动作处理，不当作可执行合约。位置、门槛和是否可重新叫牌以引擎合法动作集合为准。
- 弃牌使用实际count；支持16张中弃6张，以及不取底时弃0张。零墩高/低Joker会改变弃牌风险。
- 续演从配置读取活跃人数、首攻和明牌时机；Joker合法动作和赢墩由同一规则实现裁决。每个假设世界有独立上下文，不修改真实局面。
- 隐藏牌采样随机生成满足公开约束的可行分配，不保证均匀分布或精确后验，未移植旧采样器的软叫牌权重；它按实际牌组和底牌数工作，支持六张未知弃牌；公开已命名Joker只能分给其已知持有者。不会读取实际对手手牌、审计分配或发牌种子。
- 隐藏手牌续演中的行动者只看到自己的假设手牌和按时公开的牌。用完整假设手牌计算模拟结果不等于把这些牌暴露给根节点策略。
- Partner的低牌垫牌请求是明确沿用的搭档约定，不是强制牌规；只在有效花色跟牌成立、持有可配合牌型、没有明显将吃风险时使用。低Joker不会被当成必须埋掉的高Joker。
- 除Random外，策略当前主动放弃可选的Joker预命名，避免未经搜索将万能大牌改为边门牌；它们仍识别其他玩家已作出的命名。暂不声称已优化这一新决策阶段。

## 支持范围与测试边界

引擎尚未实现的高级配置（双手控制、固定副数等）仍由统一加载器拒绝；bot不会绕过它。配置的Schema、语义和可执行性限制见ENGINE_SUPPORT.md。

测试 `TestBots/TestRuleAwareBots.cs` 包括：

- 六种bot×六份配置的72次场景运行/重放：36次自然叫牌运行（最多3副）和36次固定叫牌上下文（1副）。后者仍验证bot本来的叫牌返回是否合法，再固定上下文以确保弃牌/出牌被测；不是72场都打到分数终局。
- 六种策略默认路径与历史版本同种子决策对照。
- 六种策略配置不匹配拒绝；四人均参与零墩时，队友保护定约者、对手争取破约。
- 修改计分表改变叫牌、修改Joker高低改变弃牌的行为正反例。
- 六张未知底牌与公开Joker持有者约束。
- Fast/Strong搜索预算区分，Partner原有体验场景检查。
- Arena六种选择及Lab混搭命令行接入、牌谱重放，计时包装层转发Joker声明。

这些测试关注正确导入、合法执行、行为响应和信息隔离，不构成强度不下降的证明。

最终验证：完整套件492项通过，无失败或跳过；45项冻结指纹验证通过。机器可读汇总见 `bot-validation-report.json`。

## 正式策略主体整合

六种正式类的判断现已迁入各自 bot 文件，共享计算和续演仍复用。默认路径使用迁入的历史等价策略，正式 Medium 不再调用原 FiveHundredBot。本文前述预算、配置行为与测试边界不变；源码导航见 [正式 bot](../FiveHundredLab/Bots/FiveHundred/README.md)。旧 RuleAwarePlayer 保留为历史/CLI/Adapt 兼容实现。
