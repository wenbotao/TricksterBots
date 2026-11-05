# FiveHundred 策略与验证入口

## 正式 bot

六种规则版 bot 集中在 [Bots/FiveHundred](Bots/FiveHundred/README.md)：Random、Weak、Medium、Fast、Strong、Partner。每种均有独立公共类、规则对象参数和配置文件入口；工厂`Profiles.CreateForRules`返回这些正式类。

```csharp
var rules = RuleConfiguration.Load(path); // null：原默认规则
var partner = new FiveHundredLab.Bots.FiveHundred.PartnerBot(seed: 1, rules: rules);
var game = new Engine(rules).Run(players, seed: 123);
```

规则、支持范围和约束见 [FiveHundredRules](../FiveHundredRules/README.md)。省略规则时保留旧策略决策。显式配置时采用规则版；变体下Strong的叫牌仍是既有规则版启发式，没有在本轮偷偷替换策略。

## 常用验证

```sh
dotnet build TricksterBots.sln -c Release
dotnet test TestBots -c Release --no-build
dotnet run --project FiveHundredLab -c Release -- bench-mixed \
 --a partner-rules-v1 --b medium-rules-v1 \
 --rules FiveHundredRules/presets/legacy-au-4p-v1.json \
 --pairs 2 --out /tmp/fivehundred-check-new
dotnet run --project FiveHundredLab -c Release -- verify-frozen --root .
```

体验测试见 [Usability](Usability/README.md)。正式策略行为快照位于`TestBots/RuleBotDecisionSnapshots.json`，覆盖六种策略×六份规则。

## 历史与停用实验

Adapt已搁置；预训练和中间迭代不作为默认推荐。源码、模型、实验数据和原报告归入 [Archive](Archive/README.md)，保留历史profile和回归对照所需的编译兼容。Legacy子目录是正式策略在不传规则时使用的默认行为基线，不能直接删除。历史数据不再散放于主目录。

整理与效率优化记录见 [MAINTENANCE.md](MAINTENANCE.md)。

开发全貌、规则执行与源码依赖见[开发过程与成果总结](../FIVEHUNDRED_DEVELOPMENT.md)。
