"""Render the comparison report from verified complete data."""
import json
from pathlib import Path

study=Path(__file__).resolve().parent;s=json.loads((study/'summary.json').read_text());v=json.loads((study/'validation.json').read_text())
assert s['Complete'] and v['Passed']
names={'incremental':'当前增量版','plus':'StrongPlus'}
pct=lambda x:f'{100*x:.2f}%'
ci=lambda x:f'{100*x[0]:+.2f}～{100*x[1]:+.2f} 个百分点'
lines=['# 当前优化版与 StrongPlus：策略、实战与计算成本','',
'当前版本为 `strong-play-incremental-v1`，对照为正式 `strong-plus-v1`。测试期间两版源码与参数均冻结，没有根据结果调整策略。', '',
'**结论：当前版在混合阵容替换测试中的点估计较好，但未确认胜率优势；直接对战打平。成本也有取舍：当前版的后期出牌较快，StrongPlus 的叫牌和前期出牌更便宜。不能将当前版描述为全面更强或全面更省。**','',
'## 设计与统计单位','',
'- 替换测试：32 组新发牌、64 场。相同发牌、测试席位、初始庄家、队友/对手类型及每席随机种子，分别使用两版测试者。其他三席从 Weak、Medium、Fast、Strong、Partner 独立均匀抽取，可重复，独立实例；不筛手牌。测试席位与庄家均各位置 8 次。',
'- 直接对战：8 个新种子，每个两场，当前版/StrongPlus 位于席位 0/1 后互换，两边均搭档 Medium。目标 bot 的种子跟随身份，Medium 的种子跟随物理席位；相同种子与庄家保证共同轮次发牌一致。共 16 场。',
'- 统一为默认 `Rules()`，每场最多 200 轮；实际全部结束，没有截断。32 个替换发牌—阵容组、8 个直接对战种子对分别作为统计单位，不混合两种胜率，也不把每副或每次出牌当独立比赛。',
'- 比赛两个组并发、组内交替版本顺序，记录决策时间/分配，但这部分成本只作描述。全部比赛及整体回归结束后，另对 46 个固定公开状态各做两次串行测量。','',
'事前方案、种子与源码哈希见 [registration.json](results/registration.json)、[plan.json](results/plan.json)。全部比赛均真实执行，不复用同动作结果，不使用定向发牌或预设合约。','',
'## 整场比赛','',
'### 相同阵容替换','',
'胜率是测试者所在队在这批混合阵容中的胜率，不是对另一版本的直接胜率。','',
'| 版本 | 胜 / 负 | 胜率及精确 95% 区间 | 发牌轮数 / 打牌副数 / 重发 | 自己承接 / 成功 |',
'| --- | --- | --- | --- | --- |']
for arm,a in s['Replacement'].items():
    interval=a['WinRateExact95'];lines.append(f"| {names[arm]} | {a['Wins']} / {a['Losses']} | {pct(a['WinRate'])}（{pct(interval[0])}～{pct(interval[1])}） | {a['Rounds']} / {a['PlayedDeals']} / {a['Redeals']} | {a['SubjectContracts']} / {a['SubjectMade']} |")
c=s['ReplacementPaired']
lines += ['',f"32 对中，当前版相对 StrongPlus **{c['Improved']} 对由负转胜、{c['Worsened']} 对由胜转负**，另有 {c['BothWin']} 对同赢、{c['BothLose']} 对同输。配对胜率差 **{100*c['WinDifference']:+.2f} 个百分点**；按发牌组 bootstrap 的 95% 区间为 **{ci(c['PairedWinBootstrap']['blockBootstrap95'])}**，精确配对检验 p={c['ExactMcNemarP']:.3f}。",'',
    f"更保守地将改善/恶化频率各自的 Clopper–Pearson 区间经 Bonferroni 合成，差值范围为 {ci(c['ConservativeExact95Difference'])}。两种区间都跨零，当前样本不能确认胜率提高，也不能证明差距小于某个严格容忍值。",'',
    f"当前版最终本队相对得分比对照平均增加 {c['PairedTerminalRelativeScore']['mean']:.2f} 分，配对 bootstrap 95% 区间 {c['PairedTerminalRelativeScore']['blockBootstrap95'][0]:.2f}～{c['PairedTerminalRelativeScore']['blockBootstrap95'][1]:.2f}；平均少 {abs(c['PairedRounds']['mean']):.2f} 轮。最终得分受终局停止影响，不替代胜率指标。",'',
'### 互换队伍的直接对战','']
d=s['Direct'];interval=d['ClusterWinRate']['blockBootstrap95']
lines += [f"当前版 **{d['IncrementalWins']} 胜**、StrongPlus **{d['PlusWins']} 胜**，观察胜率 {pct(d['IncrementalWinRate'])}。8 个种子对中，当前版双胜 {d['IncrementalSweeps']} 对，StrongPlus 双胜 {d['PlusSweeps']} 对，互胜 {d['SplitPairs']} 对。按种子对聚类 bootstrap，当前版胜率的描述性 95% 区间为 **{pct(interval[0])}～{pct(interval[1])}**。",'',
    f"当前版在席位 0 为 {d['ByIncrementalSeat']['0']['Wins']}/{d['ByIncrementalSeat']['0']['Completed']}，席位 1 为 {d['ByIncrementalSeat']['1']['Wins']}/{d['ByIncrementalSeat']['1']['Completed']}。镜像设计有助抵消手牌与位置影响；只有 8 个独立种子，不能据此细分位置优劣。",'',
'![两种设计下的胜率与区间](outcomes.png)','','## 策略与实现差别','',
'| 项目 | 当前增量版 | StrongPlus |', '| --- | --- | --- |',
'| 默认搜索世界数 | 32 | 32 |',
'| 弃牌静态候选 | 前 12，再加 Medium 基准并去重，最多 13 | 前 24，再加基准并去重，最多 25 |',
'| 改选证据 | 均值改善 − 8 − 0.75×标准误 | 均值改善 − 6 − 1.0×标准误 |',
'| 默认基准/续演策略 | 原 Medium DefaultStrategy 语义 | Medium VisibilityStrategy 变体 |',
'| 无将弃牌修补 | 搜索后 K 支撑保护，保留 A/Joker、静态下限 | 无此修补 |',
'| Misère 根节点判断 | 有节点上限的公开当前墩证明、必破/避险 | 原 ImmediateMiserePlay 与 CertainLeadWin 判断 |',
'| 强制动作 | 根节点与出牌续演中跳过适配 | 默认续演仍调用适配器，根节点通常先取基准 |',
'| 数据准备 | 每根搜索复用编码、竞叫、比分与 Joker 参数 | 静态编码表、线程内按规则/将牌/Joker 复用 VisibilityStrategy |',
'| 历史处理 | 公共前缀准备一次、分支增量更新；仍生成新的 PlayedCards 模型 | 适配器不构造 PlayedCards 明细；仍提供公开出牌历史、重建缺门 |',
'| 策略对象 | 默认非强制续演每次新建 DefaultStrategy | 复用 VisibilityStrategy 的牌堆/花色等实例缓存 |',
'| 显式配置路径 | 保留规则版策略；本轮主要优化不套用该路径 | 原规则版策略；更宽弃牌搜索 |','',
'两版默认叫牌都仍使用搜索，均不是 Strong 内部续演。公开信息、采样与原效用计分保持各自冻结实现。显式规则配置仍有不同执行路径，本轮胜率和成本结果不推广到其他规则预设。','',
'较小的固定门槛不等于 StrongPlus 总是更激进：如果均值和标准误相同，其门槛在 SE<8 时较低，SE>8 时反而较高；实际两版续演估值也可能不同。各自 SearchReport 的效用均值不能直接当成真实胜率，或在不同模型之间当成等价预测。','',
'## 行为差异','']
b=s['NaturalBehavior']
lines += [f"32 个替换组中，{b['ChangedBlocks']} 组出现测试者行为分歧。首次分歧为叫牌 {b['FirstDifferencePhases'].get('bid',0)} 组、弃牌 {b['FirstDifferencePhases'].get('discard',0)} 组、出牌 {b['FirstDifferencePhases'].get('play',0)} 组。可对齐的 {b['SameContextComparisons']} 个公开观察中，动作相同 {b['IdenticalActions']} 次、不同 {b['SameContextDifferentActions']} 次。这里只描述覆盖，不把这些动作当独立胜率样本。",'',
'首次分歧前的公开观察相同已核对；分歧后的对齐观察可能对应不同内部随机流位置。因此这些自然差异不是逐动作的严格因果实验。固定状态测试则让两版用相同公开观察与新种子独立开始。', '',
'46 个固定状态中有 **45 个动作相同、1 个不同**；该覆盖很小且含强制动作/体验场景，不能解释成策略“约 98% 等价”。','',
'唯一固定状态差异为 case 12：测试者是 8NT 庄家，零比分、首墩领出。手牌为 AC、9S、AS、AH、8S、JH、KS、7H、JD、9H。当前版领 7H，StrongPlus 领 AS；当前版的报告为改选基准，StrongPlus 保留基准。动作上分别体现红桃低张试探与兑现黑桃顶张，不能仅凭这一例判断哪张更好。差异涉及估值模型与置信门槛，尚未单独消融。','',
f"无将 K 保护在替换比赛中实际触发 {s['Replacement']['incremental']['RepairEvents']} 次，直接比赛中也没有触发；本轮胜负差不能归功于这项补丁。所有实际合约均为普通合约，没有形成 Misère 实战样本。",'',
'另测的 6 个 Misère 体验状态，两版动作全部相同，均通过 Critical 4/4、Advisory 1/1、Control 1/1。这只是既有场景检查，不证明所有 Misère 情况都处理良好。原始差异与场景结果见 [comparisons.json](results/comparisons.json)、[micro-benchmark.json](results/micro-benchmark.json)。','',
'## 同局面计算成本','',
'串行测量在全部比赛和整体回归结束后进行。使用上一轮 24 个公开出牌状态、从旧自然牌谱提取的 8 个叫牌/8 个弃牌状态，加上 6 个既有 Misère 体验状态，共 46 个，各重复两次。按预先固定的首个合格观察取样，不按结果或耗时选牌；每阶段两版均预热，交替执行顺序、使用新实例和同样种子。','',
'计时与分配覆盖决策主体，包含内部准备，不含构造测试 bot、序列化及体验检查。分配量是累计申请字节，非峰值内存。每状态先取两次中位数，再对该组取中位数。耗时比列先逐状态计算，再取中位数，与两列中位数之比不同。','',
'| 固定状态 | 状态数 | 耗时中位数 ms：当前 / Plus | 分配量中位数 MB：当前 / Plus | 候选续演数中位数：当前 / Plus | 当前/Plus 配对耗时比中位数 |',
'| --- | ---: | ---: | ---: | ---: | ---: |']
labels={'natural-other-phase/bid/all':'叫牌','natural-other-phase/discard/all':'弃牌','natural-play-regression/play/early':'前期出牌','natural-play-regression/play/middle':'中期出牌','natural-play-regression/play/late':'后期出牌','natural-play-regression/play/forced':'唯一合法出牌','ux-misere/play/all':'Misère 体验'}
for key,label in labels.items():
    g=s['MatchedMicro']['Groups'][key];a=g['Incremental'];z=g['Plus'];ratio=g['MedianStateTimeRatioIncrementalOverPlus'];ratio_text='—' if ratio is None else f'{ratio:.3f}'
    lines.append(f"| {label} | {g['States']} | {a['Milliseconds']:.3f} / {z['Milliseconds']:.3f} | {a['AllocatedBytes']/1e6:.3f} / {z['AllocatedBytes']/1e6:.3f} | {a['Rollouts']:.0f} / {z['Rollouts']:.0f} | {ratio_text} |")
lines += ['',
'本批逐状态耗时比中位数显示：当前版叫牌约慢 64%、前期出牌约慢 27%、中期约慢 5%；后期约快 24%、弃牌约快 4%。这是各小组描述，不是整体比赛加速比例。唯一合法动作及低成本证明的微秒数据不宜算稳定百分比。', '',
'StrongPlus 的默认包装层 LastRollouts 保持历史值 0，不能据此认定它没有搜索。本报告从新生成的 SearchReport 推算 Samples×Candidates，排除未更新的旧报告；该默认路径的候选均唯一且包含基准。当前版使用实际计数。推算和直接计数已分别验证，并明确保留来源。', '',
'弃牌时当前版使用约一半候选续演，但耗时没有减半、分配量也略高，说明差异不只是搜索宽度。源码显示 StrongPlus 已复用策略实例、缓存牌堆并省略 PlayedCards 明细；当前版保留更完整的旧接口输入、每次新建策略实例。这些是值得继续剖析的成本来源，不是本轮单独验证过的因果分解。','',
'![同局面串行决策成本](costs.png)','','## 自然比赛成本与合约表现','',
'下表仅列替换测试的测试席累计决策时间；两版比赛长度和轨迹不同，且并发运行，不用于隔离速度收益。完整平均/P50/P95、分配量、计数及直接对战遥测见 [summary.json](summary.json)。','',
'| 版本 | 阶段 | 调用数 | 累计决策秒数 | 续演数（Plus 为推算） |','| --- | --- | ---: | ---: | ---: |']
for arm in ['incremental','plus']:
    for phase,g in s['Telemetry']['replacement/'+arm]['Phases'].items():
        lines.append(f"| {names[arm]} | {phase} | {g['Milliseconds']['count']} | {g['TotalMilliseconds']/1000:.2f} | {g['Rollouts']} |")
lines += ['', '| 版本 | 合约类型 | 本队承接 / 成功 | 对手承接 / 本队破约 |','| --- | --- | --- | --- |']
for arm in ['incremental','plus']:
    for kind,g in s['Telemetry']['replacement/'+arm]['Contracts'].items():lines.append(f"| {names[arm]} | {kind} | {g['OwnTeamContracts']} / {g['OwnTeamMade']} | {g['OpponentContracts']} / {g['OpponentSet']} |")
lines += ['',
'副牌成约率混有不同合约、手牌与角色，不能代替配对整场胜率。当前版少打了若干轮，也不能将自然总时间直接与固定状态延迟混用。','',
'## 判断与下一步','',
'1. 当前增量版可保留为可读、可观测的后续开发入口；本轮没有确认它比 StrongPlus 更强，也没有满足“各阶段都更便宜”的结论。',
'2. 若继续保持原策略做工程优化，优先核对 DefaultStrategy 的字段访问与实例状态，研究按完整规则/将牌/Joker 键复用只读派生缓存，以及对确实未消费的旧接口字段按需构造。保留独立命名及等价测试；不将 VisibilityStrategy 的行为变化混作纯工程优化。',
'3. 若继续提高胜率，先对弃牌宽度、置信门槛、可见性策略分别做消融。自然补丁事件稀少，应分开报告定向条件收益与总体比赛收益。',
'4. 若要判断稳定胜率差或容忍范围，需要更多独立发牌对，并事先确定主指标、样本量及可接受下降幅度；本轮只适合探索性比较。',
'5. 保持不换用 Strong 内部续演；实际策略强度与接口兼容仍需在更多规则、Misère 合约和阵容下验证。','',
'## 验证、数据与同步','',
f"整体回归 **{v['FullTestCounters']['passed']}/{v['FullTestCounters']['total']} 通过**；冻结检查 **92 个文件通过**。80 场均完成并重新回放；{v['SubjectDecisionsMatchedToActualRecords']} 个测试者动作逐条匹配实际引擎记录。32 组替换、8 组直接种子对的共同发牌前缀均一致。46 个固定观察、92 次配对测量的合法性、重复动作确定性及计数来源也已核对。",'',
'验证结果：[validation.json](validation.json)。原始牌谱、逐动作数据及指标位于 `results/replacement`、`results/direct`，每场保存 `.game.gz`、`.calls.gz` 和 `.json`。测试过程中未改 bot 策略源码。','',
'本次同步包括此前各阶段尚未提交的 bot、冻结清单、回归测试、实验源码与数据，以及本报告。目标分支为 `feature/fivehundred-partner-fast-v9`；实际提交号与推送结果见聊天汇报和 Git 日志。此前阶段报告中的“未上传”是各阶段完成时的状态记录。','',
'复现需 .NET 8、Python NumPy/SciPy；图表另需 Matplotlib。新建输出目录生成比赛，避免混用旧运行程序集：','',
'```sh',
'dotnet test TestBots -c Release --logger "trx;LogFileName=full.trx" --results-directory /tmp/strongplus-comparison-validation',
'dotnet build experiments/incremental-vs-strongplus-20261109 -c Release',
'dotnet experiments/incremental-vs-strongplus-20261109/bin/Release/net8.0/Study.dll /tmp/inc-plus-new prepare',
'dotnet experiments/incremental-vs-strongplus-20261109/bin/Release/net8.0/Study.dll /tmp/inc-plus-new run',
'# 以下处理本目录保存的结果：',
'dotnet build experiments/incremental-vs-strongplus-20261109/micro -c Release',
'# micro prepare 仅首次生成；本目录已有固定语料。bench 会覆盖微测量：',
'# dotnet experiments/incremental-vs-strongplus-20261109/micro/bin/Release/net8.0/Bench.dll prepare',
'dotnet experiments/incremental-vs-strongplus-20261109/micro/bin/Release/net8.0/Bench.dll bench',
'python3 experiments/incremental-vs-strongplus-20261109/verify.py',
'python3 experiments/incremental-vs-strongplus-20261109/analyze.py',
'python3 experiments/incremental-vs-strongplus-20261109/plot.py',
'python3 experiments/incremental-vs-strongplus-20261109/report.py',
'```','',
'比赛生成器记录程序集哈希，防止续跑混入不同执行器。最终 Python 验证器直接通过权威 Arena CLI 回放标准牌谱，不要求另一台机器重建出逐字节相同的比赛生成器；记录生成与回放两种程序集哈希。`COMPARISON_TRX` 可指定测试结果路径，`DOTNET` 可指定可执行文件。','']
(study/'README.md').write_text('\n'.join(lines));print('REPORT generated')
