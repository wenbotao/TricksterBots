# FiveHundred 对战测试程序

用于策略开发的四人、两队、完整比赛模拟器。`Engine` 负责规则和裁判，`IPlayer` 只负责决策；不修改原来的 `FiveHundredBot`。默认用仓库已有策略与随机合法策略对战，随机策略只是功能基线，不代表有竞争力的对手。

## 运行

需要 .NET 8。从仓库根目录运行。在本云环境先设置：

```sh
export DOTNET_ROOT=/workspace/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.setup/dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
```

```sh
dotnet build TricksterBots.sln -c Release
dotnet test TestBots -c Release --no-build
dotnet run --project FiveHundredArena -c Release --no-build -- run \
  --pairs 100 --seed 2026 --a medium --b random-legal --max-rounds 200 \
  --out /tmp/fivehundred-experiment

dotnet run --project FiveHundredArena -c Release --no-build -- replay \
  /tmp/fivehundred-experiment/games.jsonl
```

`--pairs 100` 表示 200 场**比赛**，每场包含若干副牌。每对比赛使用相同种子、庄位和每轮牌序：第一场 A 占 0/2 座，第二场 A 占 1/3 座。每个 pair 轮换初始庄位。每轮洗牌的种子独立于前一轮的动作，双方不必打相同数量的轮次。庄家每副顺时针轮换；种子、洗牌算法、规则和完整动作均可复现。

输出目录可以已存在，但 `games.jsonl` 必须不存在，避免覆盖旧实验。命令失败返回非零状态码。非法动作、策略异常会立即终止实验并写 `failure.json`（含当前不完整比赛）；不会代替玩家挑选合法动作，也不会将失败比赛计为正常结果。

## 默认规则：`australian-4-v1`

规则参考入口：[Pagat 500](https://www.pagat.com/euchre/500.html)。本次实现时该页面访问返回 403，因此本配置尚未逐条与该网页核验。500 存在多种家庭规则，以下是程序实际执行、可审阅的明确约定；尤其 Joker、Misère 的叫牌门槛及亮牌时点应在用于正式策略评估前确认。不要把不同配置的比赛结果混在一起比较。

- 四人顺时针行动，0/2、1/3 固定搭档。43 张牌：黑色花色 5–A，红色花色 4–A，加一个 Joker。每人十张，底牌三张。每副均匀洗牌，从庄家左手开始逐张分发四十张，余三张为底牌。
- 叫牌从庄家左手开始，正常合约 6–10 墩，同级 `Spades < Clubs < Diamonds < Hearts < NoTrump`。必须高于当前最高叫牌；Pass 后本副不能再叫。其他三人退出后最高叫牌者成为庄家（declarer）；不能独自追加叫牌。四人全 Pass 则重新发牌、轮换庄位，不计分。
- Misère 在有人叫过正常七墩或以上合约后开放，排在 7NT 与 8S 之间，分值 250。Open Misère 在有人叫过正常八墩或以上后开放，排在 10H 与 10NT 之间，分值 500。这里**竞叫顺序与分值是两个独立概念**。
- 叫牌者拿起三张底牌，秘密弃掉三张自己的牌。正常合约及两种 Misère 均拿底牌。正常合约四人出牌；Misère 的搭档不参与出牌，叫牌者必须十墩皆不取。Open Misère 在第一墩结束后，公开叫牌者剩余手牌。所有合约均由叫牌者首引，并打完十墩，便于完整复盘。
- 有将时：Joker > 右 Bower（将牌 J）> 左 Bower（同色另一花色 J）> A > K > Q > 10 …。左 Bower 从属于将牌花色，不能当作其印刷花色跟牌。有将必须跟有效花色，无牌才可垫牌或将吃；不强制将吃或超将。无将及 Misère 没有 Bower。
- 无将与 Misère 中 Joker 始终最高，但跟牌时有领出花色的普通牌就必须跟，不能以 Joker 违例垫牌；缺门时可以出 Joker。首引 Joker 必须同时指定四种普通花色之一，其他人按指定花色跟牌。此配置允许首引者指定自己持有的花色，不要求赛前为 Joker 指定花色。这一政策集中在 `LegalPlays`，便于替换其他地区的 Joker 规则。
- 正常合约计分表如下，完成合约加分、失败减去合约分值。叫牌方十墩全取时，若合约分值不足 250 则按 250 加分；不额外累加 250。防守方每取一墩加 10 分，不因叫牌方失败额外加合约分。

| 墩数 | ♠ | ♣ | ♦ | ♥ | NT |
|---|---:|---:|---:|---:|---:|
| 6 | 40 | 60 | 80 | 100 | 120 |
| 7 | 140 | 160 | 180 | 200 | 220 |
| 8 | 240 | 260 | 280 | 300 | 320 |
| 9 | 340 | 360 | 380 | 400 | 420 |
| 10 | 440 | 460 | 480 | 500 | 520 |

- Misère 成功/失败分别加/减其分值，防守方不记墩分。
- 每副结束计分。完成自己合约且总分 ≥500 才能以正分获胜；防守积分可以超过 500，但仅靠防守不获胜。任何队 ≤−500 则立即负于另一队。正常比赛没有“高分领先即可获胜”的替代判定。
- 达到 `--max-rounds`（全 Pass 重发也占一轮）仍未终局，标记 `Truncated`；不以领先方冒充赢家。

## 玩家接口与信息边界

实现 `IPlayer` 的三个方法：

```csharp
Contract? Bid(Observation view, IReadOnlyList<Contract?> legal); // null 表示 Pass
IReadOnlyList<PlayingCard> Discard(Observation view, int count);
PlayAction Play(Observation view, IReadOnlyList<PlayAction> legal);
```

每次收到独立的观察副本，包括自己的手牌和自己的弃牌、轮次、庄位、座位/搭档关系、双方分数、公开叫牌历史、每人剩余张数、每墩归属及已公开的出牌、当前墩和合约。只有 Open Misère 按规则公开的手牌可出现在 `ExposedHand` 中。底牌不单独泄露，只有拿到底牌的玩家在弃牌阶段能通过自己的 13 张手牌看到它们；弃牌不会出现在对手的观察里。

裁判独立生成合法动作并保留自己的校验副本。叫牌必须来自合法列表；弃牌必须是手中三张不重复的牌；出牌（包括 Joker 首引指定花色）必须来自合法列表。玩家修改传入的数组不影响裁判。现有策略适配器用 `0U` 按实际张数掩盖其他人的牌，补充公开推断的缺门、BidHistory、PlayedCards 和 CardsTaken。

接口用于可信的本地策略测试，并非恶意代码安全沙箱：插件与程序同进程，不能阻止插件读取磁盘、运行不终止的计算或访问日志。需要不可信/远程策略或硬性超时，应另实现子进程/IPC 适配器，不应把此接口称作安全隔离。完整日志包含隐藏牌，比赛进行中应只由裁判掌握。

### 接入新策略

最简单的方式是在 C# 中为每个座位新建一个 `IPlayer` 实例：

```csharp
var game = new Engine(new Rules()).Run(
    new IPlayer[] { new MyBot(), new ExistingBotPlayer(), new MyBot(), new ExistingBotPlayer() },
    seed: 2026);
```

CLI 也支持加载引用 `FiveHundredArena` 的 .NET 8 插件程序集：

```sh
dotnet run --project FiveHundredArena -c Release --no-build -- run \
  --assembly /absolute/path/MyBots.dll --a MyNamespace.MyBot --b existing \
  --pairs 1000 --out /tmp/mybot-v1
```

类型实现 `IPlayer` 且有公开无参构造函数；每场、每个座位独立实例。随机策略应在实现中使用可配置、可记录的种子，不应使用墙上时钟作为种子。内置随机策略有独立的固定随机流，重放不依赖策略随机数，只依赖记录的合法动作。

现有 `FiveHundredBot` 仅返回 Joker 卡，不能返回指定花色；适配器在必须首引 Joker 时，选择手中普通牌最多的花色（并列按固定顺序）。这是适配层约定，日志记录最终指定值。现有策略的 Misère 分值参数只支持库已有的 210/230/250、Open Misère 330/430/500/520；其他分值使用新策略或扩展适配器，程序会明确报错。

## 变体扩展

常见四人家庭规则通过 `--rules profile.json` 覆盖。例如：

```json
{
  "Id": "house-no-misere-v1",
  "AllowMisere": false,
  "AllowOpenMisere": false,
  "PassIsPermanent": false,
  "MustContractToWin": false,
  "WinScore": 500,
  "LossScore": -500
}
```

`Rules` 还提供 `MisereRequiresSeven`、`OpenMisereRequiresEight`、两种 Misère 分值、`DefenderTrickPoints`、`SlamMinimum`。允许重新进入叫牌时，最高叫牌之后连续三次 Pass 结束叫牌。`MustContractToWin=false` 时，防守方也可凭达到目标分获胜。规则参数随每场日志保存。

更复杂的竞叫顺序、Joker 使用、计分变体通过继承 `Rules`，覆盖 `LegalBids`、`EffectiveSuit`、`Rank`、`LegalPlays`、`Winner`、`Score`、`WinningTeam` 等方法。当前引擎明确限定四人、43 张牌、三张底牌、十墩和固定搭档；三/五/六人局需要扩展调度器，不能只改配置冒充支持。C# 自定义规则子类还需要自定义持久化/重放类型解析器；默认 CLI JSON 仅恢复 `Rules` 参数，不能恢复任意子类行为。

## 数据和比较方法

- `games.jsonl`：每行一场完整比赛，含规则、版本、种子、庄位、玩家名、每副初始四手牌及底牌、全部决策、每墩赢家、合约/叫牌者、墩数、成败、分数增量和总分。保留了完整信息，可自行构建复盘界面或生成训练样本；给策略训练生成观察时必须重新遵守信息边界。
- `replay`：不调用原策略，逐一提交日志动作给裁判，重新发牌、检查合法性、重新结算，比较整个结果；篡改动作、赢家、分数或不兼容版本会报错。重放本身用于完整性校验，不等同于独立规则正确性证明。
- `summary.json`：A/B 胜场、截断场、发牌与重发次数、完整 pair 数、成对平均胜率、标准误、双方最终分数之和、决策耗时总量，以及按策略和合约分组的承包次数、成功数、取墩数和净合约得分。还记录运行时和引擎/策略程序集 SHA-256，便于区分策略版本。
- 胜率以**两个 leg 都完成**的 pair 为采样单位计算。截断场既不计胜也不计负，必须单独检查；若截断明显，应增加轮次上限，避免选择偏差。区间使用有界 pair 得分（0、0.5、1）的 Hoeffding 95% 界，假设各 pair 独立抽样，比正态近似保守，即使全胜也不会报告虚假的零宽区间。固定伪随机种子实验仍是探索性统计，不是对所有对手、规则或座位分布的水平保证。先用旧策略镜像验证流程，再对多个固定基准策略运行大量独立种子。
- 耗时包括适配器构建观察状态的开销，受 JIT、硬件和负载影响，不是公平的纯算法微基准。输出不含每步 CPU 限时。

## 验证范围

`TestFiveHundredArena` 覆盖牌组、叫牌顺序、Bower、跟牌和 Joker、计分/终局、正常/无将/两种 Misère 完整发牌、信息公开时点、非法动作及篡改传入列表、重放篡改、现有 bot 完整比赛和确定性。规则引擎不复用策略的赢墩算法，避免策略与裁判共同依赖同一个错误判断。

## 策略等级

新策略实现 `FiveHundredArena.IPlayer`，可直接参加本程序的叫牌、弃牌和完整实战。等级名是策略定位，不是对所有变体、对手和计算预算的胜率保证。

| CLI 名称 | 类型 | 行为 |
|---|---|---|
| `medium` | `MediumBotPlayer` → `Trickster.Bots.MediumBot` | 原 `FiveHundredBot` 的新名称，继承全部原决策方法；原类和 `existing` 入口保留兼容，不改变策略 |
| `weak` | `WeakBot` | 简单牌力和低级有将叫牌、固定弃牌、即时抢墩；不记牌、不推断缺门、不主动叫 Misère |
| `strong` | `StrongBot` | 在 Medium 决策基础上，按观察采样隐藏牌，模拟比较叫牌、弃牌和出牌；证据不足时保留 Medium 动作 |
| `random-legal` | `RandomLegalBot` | 包含 Pass 的叫牌均匀抽样，弃牌组合均匀抽样；出牌先均匀选牌，再均匀选 Joker 的合法指定花色 |
| `random` | `RandomPlayer` | 之前的带 Pass 偏好的随机策略，保留用于重现实验，不等同于 `random-legal` |

CLI 默认对阵现在为 `medium` 对 `random-legal`。例如：

```sh
dotnet run --project FiveHundredArena -c Release --no-build -- run \
  --a strong --b medium --pairs 100 --seed 91000 --strong-samples 8 \
  --out /tmp/strong-vs-medium
```

Strong 的 `--strong-samples` 默认为 8，范围 1–256，表示每次非强制决策采样的隐藏牌局数。预算越大通常越慢，不保证胜率单调提高。程序按固定样本数结束搜索，不依赖机器速度或墙上时钟；相同版本、种子和参数可复现。每个座位每场比赛新建实例，玩家随机流不与发牌随机流共享。Strong 的完整模拟开销明显高于 Medium，建议先用 `--pairs 2` 冒烟测试，再扩大场数；`--strong-samples 2` 可用于较快的功能测试，参考强度评估使用默认 8 样本。

C# 可进一步设置：

```csharp
var strong = new StrongBot(seed: 123, options: new StrongBotOptions
{
    Samples = 16,
    DiscardCandidates = 12,
    ImprovementMargin = 8,
    StandardErrorMultiplier = 0.75
});
```

Strong 的实现细节：

- `BeliefSampler` 仅接收 `Observation`，不访问裁判真实手牌、日志、发牌种子或底牌。采样满足已知牌、剩余张数、历史缺门和 Open Misère 公开手牌；保留未知弃牌/底牌槽位，也保留退出 Misère 的搭档手牌。约束矛盾时明确报错，不偷偷放宽约束。
- 使用随机约束匹配生成可能牌局，再根据公开叫牌作温和的软加权。这是近似信念模型，**不是均匀的条件分布或经训练校准的贝叶斯后验**；不会把对手叫牌当作必然持牌条件。
- 叫牌比较 Pass、Medium 原建议、各花色最低竞争叫牌、启发式可行高合约及可能直接获胜的合约；在采样牌局中继续完整竞叫、底牌交换和出牌。额外 Misère 候选须通过低牌结构安全筛选，避免小样本高估高风险合约；原 Medium 建议仍保留为基准。
- 弃牌完整生成 286 个三张组合，按保将、缺门、无将挡张及 Misère 危险度筛选前 12 组，额外保留 Medium 弃牌，然后进行模拟比较。并非对 286 组都做完整搜索。
- 出牌评估所有合法动作，包括 Joker 首引的不同指定花色。所有候选共用同一批采样牌，以减少比较噪声。
- `ForwardModel` 中所有后续玩家都使用 Medium 策略；它们收到各自独立、掩盖其他手牌的观察，自己的弃牌只有自己知道。公开手牌按实际时点展示。不会让后续玩家按全知最优动作出牌，也不会在未来节点调用 Strong 形成递归搜索。
- 一副牌模拟到结束，按真实规则计算得分；若本副导致比赛结束，终局效用为正/负 `4 × WinScore`，否则使用本方与对方分数增量之差。这是明确的近似效用，**不是经过校准的整场获胜概率**。
- 用相对 Medium 动作的平均改善，扣除固定改善门槛和样本标准误惩罚后选择动作。该惩罚是搜索稳定措施，不是多个候选下具有严格覆盖率的统计置信保证。`LastSearch` 提供本次样本数、候选数、基准/最终估值及是否更改决策，便于诊断。

`Trickster.Bots.MediumBot` 与原类具有相同的 `BaseBot<FiveHundredOptions>` 接口；Weak、Strong、RandomLegal 是对战程序侧玩家策略，暂未额外接入 WebAPI 路由。新测试会检查 Medium 的方法确实继承自原类、随机分布、采样约束、模拟与裁判逐阶段一致、合法完整对局、固定种子复现及重放。

默认预算的独立实战结果、种子和复现命令见 [策略基准记录](BENCHMARKS.md)。
