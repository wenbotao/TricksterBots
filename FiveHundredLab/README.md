# FiveHundred 性能与策略实验室

目标是在保留旧版本的前提下，以较小的同牌组实验快速比较强度和决策成本。**预训练方向目前搁置**：相关实验代码和已产生的模型仅存档，下面的推荐配置不加载模型、不训练、不依赖额外机器学习包。

人类搭档体验测试独立于胜率实验：场景、复现命令与评估见 [Usability/README.md](Usability/README.md)，五种 bot 的逐项结果见 [Usability/results-v1/REPORT.md](Usability/results-v1/REPORT.md)。

## 推荐配置

| 名称 | 用途 | 策略与预算 |
|---|---|---|
| `partner-fast-v9` | 人类搭档体验优先，基于 Fast v8 | 相同搜索预算，增加公开 Misère 必破、保牌、公开信号和弃牌结构判断；见 [PARTNER_FAST_V9.md](PARTNER_FAST_V9.md) |
| `visibility-fast-v8` | 默认，修缮后的速度优先版 | 修复当前墩分界剪枝；按有效样本数评估风险；普通局面仍为早期四/后期八个样本，末家例外最多两动作、两样本 |
| `visibility-fast-v6` | 冻结的旧速度版 | Medium 叫牌；弃牌保留基准加三个候选、四个样本；出牌早期四、后期八个样本。明确动作直接决定，其他动作完整模拟到本副结束 |
| `visibility-quality-v7` | 更高计算预算 | 叫牌筛选三个候选加基准/Pass、四个样本；弃牌六个候选加基准、八个样本；出牌早期十二、后期二十个样本 |
| `visibility-strong-v5` | 原决策的工程优化对照 | 保留旧 Strong 的候选、样本和评估逻辑，优化状态构造、编码、牌表及可见性判定；逐动作等价检查通过 |
| `strong-v1` / `medium-v1` | 冻结基准 | 原 Strong / Medium，不改变原文件或默认参数 |

新程序独立于 `FiveHundredArena`，原命令、原 bot 和原配置保持不变。V8 的必要修缮与小样本结果见 [FAST_V8.md](FAST_V8.md)，历史实验见 [RESULTS.md](RESULTS.md)。不要把小样本中的胜率差当作正式非劣效性结论。

## 快速开始

从仓库根目录运行，需要 .NET 8；本云环境工具初始化沿用已有配置：

```sh
export DOTNET_ROOT=/workspace/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.setup/dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages

dotnet build TricksterBots.sln -c Release
dotnet run --project FiveHundredLab -c Release --no-build -- bench \
  --a visibility-fast-v8 --b strong-v1 --pairs 2 --seed 2301001 \
  --out /tmp/fast-feedback-01
```

`--pairs 2` 是四场完整比赛；两场使用同一牌序并交换队伍，每对轮换初始庄位。建议先 2–10 对快速筛选，再按需要扩大。输出目录不能包含已有的 `games.jsonl`，避免覆盖旧实验。`--max-rounds` 默认 200；截断单独统计，不记作胜负。

`bench-mixed` 使用相同参数，但让 A 队每场只有一个新策略，搭档为 Medium，测试其搭档兼容性。新策略在两个搭档座位间轮换；`TimingAPartner` 与新策略自身计时分开。

## 分清强度测试和耗时测试

`bench` 输出完整 `games.jsonl`、`summary.json`，统计胜负、完整 pair、截断及阶段耗时。每阶段包含数量、平均值、P50、P95、每次决策分配字节数、相对 Medium 改变动作的次数。参考动作比较在计时区间外进行。JIT 预热使用独立的未计分牌局。耗时包含适配器构造，分配量是累计分配量，不是峰值驻留内存。

不同策略会走入不同局面，所以还有**同一观察语料**的成本对照：

```sh
dotnet run --project FiveHundredLab -c Release --no-build -- profile \
  --input /tmp/fast-feedback-01/games.jsonl \
  --profiles strong-v1,visibility-fast-v6,visibility-fast-v8 \
  --games 1 --decisions 16 --out /tmp/same-observations-01.json
```

裁判重放记录动作，策略建议只用于计时和合法性检查，不会改变语料的后续状态。默认每阶段最多评估 16 次决策，适合快速反馈；统计中保留实际样本数。此命令只测成本，不能从建议动作推导胜负。用原程序重放日志：

```sh
dotnet run --project FiveHundredArena -c Release --no-build -- replay /tmp/fast-feedback-01/games.jsonl
```

## 优化内容

- **状态与牌表**：紧凑牌编码、规则/合约牌表缓存；仅缓存与玩家隐藏牌无关的基础 bot 常量。缓存按线程隔离，最多 128 个规则/将牌/Joker 指定花色组合。
- **信息可见性**：此适配器只发送全已知的两字符牌串或 `0U` 掩码；直接判定是否未知，避免原 `CanSeeHand` 为每次检查反复解析整手牌。公开手牌、自己手牌、缺门仍按相同协议处理；不向策略暴露实际隐藏牌。
- **候选约简**：有将/无将正常合约中，如果两张同有效花色的牌之间没有任何尚未知的牌，则合并相对大小等价候选，保留基准代表。Joker 的指定花色不合并；Misère 不采用此约简。V8 另外保留当前墩中的分界牌及不同赢墩结果；该约简仍不建模精细信号差异。
- **按局面分配预算**：只有一个合法动作时采用基准。V8 的末家只对少数接近成约、残局或接管首引的选择进行最多两动作、两样本评估，其余直接采用基准；普通局面使用固定的早/晚期样本数。新默认保留完整的有限信息续演，避免短程估值误差主导动作。
- **基准保护**：基准动作始终参与比较，候选只有在平均改善超过固定门槛和标准误惩罚后才取代基准。该惩罚是稳定措施，不是正式置信检验。

所有续演玩家只收到自己的手牌和公开状态，未知弃牌/底牌及退出 Misère 的搭档手牌仍由采样模型保留。所有真实动作均由冻结裁判校验。缓存不保存任何玩家私有手牌或决策状态，不允许通过线程共享来传递搭档的私牌。

## 版本纪律

```sh
dotnet run --project FiveHundredLab -c Release --no-build -- verify-frozen --root /workspace/TricksterBots
```

`frozen-baselines-v1.json` 校验此前 bot 与其依赖源码；`frozen-profiles-v1.json` 校验本次新增策略、共享策略辅助代码和存档模型。以后策略、参数或共享策略逻辑有变化，应新增有版本号的类/配置/模型和注册名称，保留旧版本。实验 CLI 不提供无版本的样本数覆盖开关。模型文件以内容哈希参与名称，并禁止覆盖已有训练产物。

## 已保留的研究配置

`compact-*`、`cached-*` 是逐动作等价的工程优化阶段；`tactician-v1`、`adaptive-sprint-v1`、`adaptive-balanced-v1` 是启发式/短程评估试验；`selective-play-v1`、`selective-joint-v1`、`selective-quality-v2`、`selective-belief-v3`、`selective-fast-v4` 保留各自固定预算，便于对照，不再覆盖修改。

`ablate --pairs 2 --seed N --out DIR` 比较仅替换叫牌、弃牌、出牌和全阶段 Strong 的四种消融。小样本用于定位成本来源，不足以给阶段策略定级。

预训练实验（`distilled-v1`、`distilled-phase-v2`、`value-guided-v3`、`models/`）目前暂停且不推荐作为默认。它们展示了从合法观察采样教师标签、按完整发牌种子划分数据、拟合小型价值模型的链路，但部分实战结果不理想。保留它们以便将来继续，不应只凭训练误差宣布强度提升。
