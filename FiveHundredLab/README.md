# FiveHundred 策略与验证入口

## 正式 bot

七种主要规则版 bot 集中在 [Bots/FiveHundred](Bots/FiveHundred/README.md)：Random、Weak、Medium、Fast、Strong、StrongPlus、Partner。每种均有独立公共类、规则对象参数和配置文件入口；工厂`Profiles.CreateForRules`返回这些正式类。

```csharp
var rules = RuleConfiguration.Load(path); // null：原默认规则
var partner = new FiveHundredLab.Bots.FiveHundred.PartnerBot(seed: 1, rules: rules);
var game = new Engine(rules).Run(players, seed: 123);
```

规则、支持范围和约束见 [FiveHundredRules](../FiveHundredRules/README.md)。省略规则时使用各 bot 文件中的默认策略；原六个基线保持历史等价，StrongPlus使用其独立增强策略。显式配置时采用规则版；变体下Strong的叫牌仍是既有规则版启发式，没有在本轮偷偷替换策略。

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

体验测试见 [Usability](Usability/README.md)。正式策略行为快照位于`TestBots/RuleBotDecisionSnapshots.json`，覆盖原六种策略×六份规则；StrongPlus另有`TestStrongPlus`的配置、确定性及重放验证。

## 历史与停用实验

Adapt已搁置；预训练和中间迭代不作为默认推荐。源码、模型、实验数据和原报告归入 [Archive](Archive/README.md)，保留历史profile和回归对照所需的编译兼容。默认策略已迁入正式 bot 文件；旧 Legacy 实现移入 Archive/Strategies 作为历史对照。历史数据不再散放于主目录。

整理与效率优化记录见 [MAINTENANCE.md](MAINTENANCE.md)。

开发全貌、规则执行与源码依赖见[开发过程与成果总结](../FIVEHUNDRED_DEVELOPMENT.md)。

本轮策略主体整合与依赖说明见 [CONSOLIDATION.md](CONSOLIDATION.md)。

Random 的始终 Pass 变体为 `random-pass-v1`；[与 Weak 的50场随机实验](../experiments/randompass-weak-50-20261105/README.md)包含完整报告和复核数据。

正式独立bot StrongPlus（`strong-plus-v1`）与Fast、Strong位于同一目录，保留旧 Strong 不变；[实现、行为差异及成对实战报告](../experiments/strong-plus-v1-20261106/REPORT_ZH.md)。

Strong 的独立消融与阶段预算试验按用户要求提前停止，保存136场：[结果与优化方向](../experiments/strong-ablation-v1-20261106/REPORT_ZH.md)。
