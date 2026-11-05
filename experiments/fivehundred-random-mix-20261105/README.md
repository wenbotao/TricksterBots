# Five Hundred 1,500场随机对战数据

[胜率分析报告](analysis/WIN_RATE_REPORT_ZH.md) · [分析JSON](analysis/analysis.json) · [累计统计](summary-1500.json)

策略来源提交：`42b69e6`。五种正式bot为Weak、Medium、Fast、Strong、Partner；不含Random。使用未显式配置JSON的默认规则路径，每座位独立等概率选类型、允许重复，每场新建四个实例。统计单位为座位参赛人次，同场结果相关。

## 数据内容

- `game-NNN.json`：1,500场逐场阵容、种子、赢家、副数与耗时。
- `game-NNN.json.gz`：对应完整牌谱，包含隐藏牌，供赛后复核。
- `plan.json`、`metadata.json`：完整计划、规则、程序集指纹和种子约定。
- `summary-*.json`、`REPORT-*.md`：各阶段汇总和历史报告；最终结果以1500版本和analysis为准。
- `replay-*.log`：各阶段裁判重放验证结果，覆盖全部1,500场。
- `analysis/`：完整统计、比赛层面bootstrap区间、阵容回归、图表及分析脚本。
- `SHA256SUMS.json`：本数据包文件校验清单，不包含其自身。

原运行器使用三位数文件匹配，超过999场后漏计了部分自动汇总；最终汇总及分析已经从全部逐场记录重算。`provenance/original-runner.cs.txt`保存原运行器，根目录`Runner.cs`已修正匹配范围、默认计划长度及项目引用路径；未改变任何bot决策。

原始元数据中的程序集路径和指纹属于当时构建，重新构建可能产生不同指纹。按提交、计划和动作复现，不将一次重建指纹差异当作策略改变。

## 复算分析

仓库根目录运行，需要Python及numpy、scipy、matplotlib：

```sh
MPLCONFIGDIR=/tmp/fivehundred-mpl python3 experiments/fivehundred-random-mix-20261105/analysis/analyze.py
python3 experiments/fivehundred-random-mix-20261105/analysis/write_report.py
```

## 重跑比赛

需要.NET 8。使用全新的输出目录避免覆盖已有数据。第二、三个参数为起始索引及运行场数：

```sh
dotnet run --project experiments/fivehundred-random-mix-20261105/Runner.csproj -c Release -- /tmp/fivehundred-rerun 0 1500
```

运行器生成相同的1,500场计划；也可复制本目录plan.json到新输出目录后执行。新数据会记录当前程序集；若只复制计划，运行元数据需随新实验另存，不沿用原实验指纹。

完整牌谱可用Python gzip逐一解压并合成JSONL，再调用：

```sh
dotnet run --project FiveHundredArena -c Release -- replay /tmp/replay-games.jsonl
```

四路并发的耗时不用于bot纯算法性能比较。本数据仅评估默认规则，不能据此推断所有显式规则配置下的强度。
