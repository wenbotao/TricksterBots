"""Generate new-batch primary, pooled auxiliary, and serial-cost report from complete verified data."""
import json
from pathlib import Path
sroot=Path(__file__).resolve().parent
s=json.loads((sroot/'summary.json').read_text());v=json.loads((sroot/'validation.json').read_text())
old=json.loads((sroot.parent/'incremental-vs-strongplus-20261109/summary.json').read_text())
assert s['Complete'] and v['Passed']
pct=lambda x:f'{100*x:.2f}%'
ci=lambda x:f'{100*x[0]:+.2f}～{100*x[1]:+.2f} pp'
fresh=s['ReplacementPaired'];direct=s['Direct'];pooled=s['PooledAuxiliary']
if fresh['ExactMcNemarP']<.05:
    conclusion='新批次的配对检验偏向'+('当前增量版。' if fresh['WinDifference']>0 else 'StrongPlus。')
else:conclusion='新批次尚未确认两版存在胜率差异；不显著不等于证明等价或证明不下降。'
lines=['# 新版与 StrongPlus 扩大测试：新增 320 场','',
'当前版为 `strong-play-incremental-v1`，对照为 `strong-plus-v1`。两版策略、规则、预算与源码哈希均与上一轮一致，本轮没有调参、换用 Strong 内部续演或修改 bot。', '',
'**'+conclusion+'** 本报告首先分析预先固定的新批次；旧 80 场仅在辅助汇总中出现。成本在新增语料上重新串行测量，不混用旧日期的绝对耗时。','',
'## 设计与验证','',
'- 新增相同阵容替换：128 个独立发牌—阵容组，每组运行两版，共 256 场。测试者席位、庄家各位置 32 次，其他三席从 Weak/Medium/Fast/Strong/Partner 独立均匀抽取，可重复。每组的发牌、其他三席、全部种子与庄家相同。',
'- 新增直接对战：32 个独立种子，每种子互换两版席位，两队均搭档 Medium，共 64 场。目标 bot 种子跟随身份，搭档种子跟随物理席位。',
'- 默认规则，200 轮上限，不筛手牌；事前固定新增 320 场，不因中间胜负或显著性停止。所有新种子与上一轮不重叠。比赛按计划序号分成四个单工作进程运行，累计耗时只作描述；成本测量等全部对局结束后进行。',
'- 成本：144 个新公开局面，叫牌、弃牌、前期、中期、后期、唯一合法出牌各 24 个，每层各 12 个来自两版轨迹。每来源比赛每层取首个合格状态，交替来源优先级；排除旧 46 个局面和重复公开观察。每状态三次、交替执行版本、固定打乱状态顺序。',
'- 重复测量先在每状态内取中位数；同一发牌组内的来源状态相关，耗时/分配比先按来源发牌组汇总，再做组级 bootstrap。比例大于 1 表示当前版成本更高。各成本端点属于探索性结果，区间未作多重比较校正。', '',
'方案、输入与源码哈希见 [registration.json](results/registration.json)、[plan.json](results/plan.json)、[micro-registration.json](results/micro-registration.json)。运行环境见 [runtime.txt](results/runtime.txt)，来源提交见 [source-commit.txt](results/source-commit.txt)。','',
'## 胜率：新批次为主要结果','',
'| 版本 | 胜 / 负 / 截断 | 正常结束比赛胜率 | 精确 95% 区间 |','| --- | --- | --- | --- |']
for arm,label in [('incremental','当前增量版'),('plus','StrongPlus')]:
    a=s['Replacement'][arm];interval=a['WinRateExact95']
    lines.append(f"| {label} | {a['Wins']} / {a['Losses']} / {a['Truncated']} | {pct(a['WinRate'])} | {pct(interval[0])}～{pct(interval[1])} |")
lines += ['', f"完整替换配对 {fresh['Pairs']} 组：当前版由负转胜 {fresh['Improved']} 组、由胜转负 {fresh['Worsened']} 组，同赢 {fresh['BothWin']}、同输 {fresh['BothLose']}。", '',
    f"配对胜率差为 **{100*fresh['WinDifference']:+.2f} 个百分点**；配对发牌组 bootstrap 95% 区间 **{ci(fresh['PairedWinBootstrap']['blockBootstrap95'])}**，精确配对检验 p={fresh['ExactMcNemarP']:.4f}。更保守的 CP/Bonferroni 差值范围为 {ci(fresh['ConservativeExact95Difference'])}。", '',
'这是测试者所在队在随机混合阵容中的表现，不是两版直接对打胜率。单行胜率按正常结束比赛计；配对分析仅使用双方都结束的组。','',
f"最终本队相对得分的配对平均变化 {fresh['PairedTerminalRelativeScore']['mean']:+.2f} 分，95% 区间 {fresh['PairedTerminalRelativeScore']['blockBootstrap95'][0]:.2f}～{fresh['PairedTerminalRelativeScore']['blockBootstrap95'][1]:.2f}。平均轮数差 {fresh['PairedRounds']['mean']:+.2f}。最终得分受终局停止影响，不替代胜负。",'',
'### 新增直接对战','',
f"正常结束 {direct['Completed']} 场、截断 {direct['Truncated']} 场；当前版 **{direct['IncrementalWins']} 胜**，StrongPlus **{direct['PlusWins']} 胜**，原始胜率 {pct(direct['IncrementalWinRate'])}。完整种子对 {direct['CompleteSeedPairs']} 组，其中当前版双胜 {direct['IncrementalSweeps']}、StrongPlus 双胜 {direct['PlusSweeps']}、互胜 {direct['SplitPairs']}。",'',
f"按完整种子对聚类，当前版胜率估计 {pct(direct['ClusterWinRate']['mean'])}，95% 区间 {pct(direct['ClusterWinRate']['blockBootstrap95'][0])}～{pct(direct['ClusterWinRate']['blockBootstrap95'][1])}。同一对的两场不当作独立样本。",'',
'## 与上一轮及合并结果对照','',
'| 批次 | 替换配对数 | 当前版胜场 | StrongPlus 胜场 | 配对胜率差 | 配对 bootstrap 95% 区间 |','| --- | ---: | ---: | ---: | ---: | --- |']
for label,a,ar in [('旧批次（辅助）',old['ReplacementPaired'],old['Replacement']),('新增（主要）',fresh,s['Replacement']),('合并（辅助）',pooled['Replacement'],pooled['Replacement'])]:
    lines.append(f"| {label} | {a['Pairs']} | {ar['incremental']['Wins']} | {ar['plus']['Wins']} | {100*a['WinDifference']:+.2f} pp | {ci(a['PairedWinBootstrap']['blockBootstrap95'])} |")
pd=pooled['Direct']
lines += ['',f"合并直接对战：当前版 {pd['IncrementalWins']} 胜、StrongPlus {pd['PlusWins']} 胜，共 {pd['Games']} 个结束的比赛、{pd['CompleteSeedPairs']} 个完整种子对；聚类胜率 {pct(pd['ClusterWinRate']['mean'])}，95% 区间 {pct(pd['ClusterWinRate']['blockBootstrap95'][0])}～{pct(pd['ClusterWinRate']['blockBootstrap95'][1])}。",'',
'策略源哈希一致且新旧种子不重复，但旧样本已用于提出扩大测试，因此以独立新增批次为主要依据；合并结果只辅助观察稳定性。环境/日期不同，不合并两批绝对毫秒数。胜率差“不显著”也不能作为严格不劣于某个阈值的证明。','',
'![胜率差和直接对战的复制结果](outcomes.png)','',
'## 新语料的串行成本','',
'旧版 StrongPlus 默认 LastRollouts 包装计数为 0，本轮继续从新生成的 SearchReport.Samples×Candidates 推算搜索次数，排除陈旧报告；当前版使用实际计数。此差别保留在原始遥测中。','',
'下表的耗时和分配量为状态中位数；比值先在同一状态配对，再按来源发牌组等权汇总对数比，得到几何平均及来源组 bootstrap 区间。因此比值不等于两列中位数直接相除。','',
'| 阶段 | 状态 / 来源发牌组 | 耗时 ms：当前 / Plus | 当前/Plus 耗时几何比（95%） | 分配 MB：当前 / Plus |','| --- | --- | ---: | --- | ---: |']
labels={'bid':'叫牌','discard':'弃牌','early':'前期出牌','middle':'中期出牌','late':'后期出牌','forced':'唯一合法出牌'}
for key,label in labels.items():
    g=s['MatchedMicro']['Groups'][key];a=g['Incremental'];b=g['Plus'];ratio=g.get('TimeRatio')
    rt='不推断微秒比率' if not ratio else f"{ratio['GeometricMeanBySourceBlock']:.3f}（{ratio['SourceBlockBootstrap95'][0]:.3f}～{ratio['SourceBlockBootstrap95'][1]:.3f}）"
    lines.append(f"| {label} | {g['States']} / {g['IndependentSourceBlocks']} | {a['Milliseconds']:.3f} / {b['Milliseconds']:.3f} | {rt} | {a['AllocatedBytes']/1e6:.3f} / {b['AllocatedBytes']/1e6:.3f} |")
lines += ['', '![新语料按来源发牌组估计的配对成本比](costs.png)','',
'后期优势受局面构成影响：其中 8 个状态仅余两张手牌、两种合法动作，两版均执行 64 次续演；当前版可跳过后续唯一合法动作。这不是搜索预算减少带来的收益。成本语料的出牌和弃牌均为普通合约，不能据此推断 Misère 成本。','',
'分配量为累计申请对象字节数，不是峰值内存。三次计时重复不当作三个独立局面，两个策略轨迹来自相同种子的情况也归同一来源组。等额分层并不代表自然出牌频率，不据此计算整场总体加速比例。原始数据另保存每层来源两版的敏感性结果与搜索预算。','',
'| 阶段 | 续演数中位数：当前 / Plus | 不同行为的固定状态数 |','| --- | ---: | ---: |']
for key,label in labels.items():
    g=s['MatchedMicro']['Groups'][key];lines.append(f"| {label} | {g['Incremental']['Rollouts']:.0f} / {g['Plus']['Rollouts']:.0f} | {g['DifferentActions']} / {g['States']} |")
lines += ['', '成本是完整版本的比较，不是纯粹缓存消融：两版弃牌宽度（12/24）、置信门槛（8+0.75SE / 6+1.0SE）、Medium 适配变体和公开 Misère 判断不同。源码差异详见[上一轮策略报告](../incremental-vs-strongplus-20261109/README.md)。StrongPlus 的策略实例缓存和省略 PlayedCards 明细仍是成本差异的重要待剖析来源；本轮未替换任何实现。','',
'## 行为与自然曝光','']
b=s['NaturalBehavior']
lines += [f"{b['Blocks']} 个替换组中，{b['ChangedBlocks']} 组出现测试者首次分歧，分布为 {b['FirstDifferencePhases']}。共对齐 {b['SameContextComparisons']} 个公开观察，动作相同 {b['IdenticalActions']}、不同 {b['SameContextDifferentActions']}。首次分歧前观察相同已核对；后续随机流位置可能不同，这些动作不是独立强度样本。",'',
f"当前版自然弃牌修补在替换测试中触发 {s['Replacement']['incremental']['RepairEvents']} 次。不能将两版总体差异直接归因于补丁或任何一个组件；完整场景与比较见 [comparisons.json](results/comparisons.json)。",'',
'各合约类别、本队承接和破约、阶段总耗时/均值/P50/P95/分配及来源配对差异均保存在 [summary.json](summary.json)。自然比赛并发且轨迹不同，其耗时不与同局面串行成本混用。','',
'## 后续判断','',
'1. 胜率判断以新批次区间和配对检验为准；不根据点估计挑选有利阵容、剔除长局或更改预算。',
'2. 工程方向继续由同局面成本定位：可研究完整规则/将牌/Joker 键下的策略只读派生缓存，以及确认未被消费的接口字段能否按需构造；需另起版本并做行为等价测试。',
'3. 若要作严格不劣效判断，应事先给出容忍下降幅度；本轮置信区间不能自动解释成“同样强”。规则变体与罕见 Misère 的强度仍需专项验证。','',
'## 数据与复现','',
f"本轮验证：{v['ActualMatches']} 个实际比赛，截断 {v['Truncated']}；全部权威引擎回放通过。{v['SubjectDecisionsMatchedToActualRecords']} 个测试者动作匹配实际记录；144 个新公开状态、432 次配对测量的来源、合法性、重复确定性与预算来源均核对通过。565 项整体回归通过；92 个冻结文件校验通过。",'',
'原始比赛位于 `results/replacement`、`results/direct`，每场保存牌谱、遥测、指标；固定成本语料与逐次计量位于 `micro-cases.json`、`micro-benchmark.json`。最终核对见 [validation.json](validation.json)。','',
'从仓库根目录，需 .NET 8、Python NumPy/SciPy；绘图需 Matplotlib：','',
'```sh',
'dotnet build experiments/incremental-vs-strongplus-expanded-20261110 -c Release',
'# 新目录准备一次；四个 run 进程可并发，索引互斥：',
'dotnet experiments/incremental-vs-strongplus-expanded-20261110/bin/Release/net8.0/Study.dll /tmp/inc-plus-expanded-new prepare',
'dotnet experiments/incremental-vs-strongplus-expanded-20261110/bin/Release/net8.0/Study.dll /tmp/inc-plus-expanded-new run 0 4',
'# 另三个进程分别用 run 1 4、run 2 4、run 3 4',
'# 以下默认处理本目录 results；旧文件先备份：',
'dotnet build experiments/incremental-vs-strongplus-expanded-20261110/micro -c Release',
'# micro prepare 仅首次执行；bench 在所有对局结束后串行运行：',
'# dotnet experiments/incremental-vs-strongplus-expanded-20261110/micro/bin/Release/net8.0/Bench.dll prepare',
'dotnet experiments/incremental-vs-strongplus-expanded-20261110/micro/bin/Release/net8.0/Bench.dll bench',
'python3 experiments/incremental-vs-strongplus-expanded-20261110/verify.py',
'python3 experiments/incremental-vs-strongplus-expanded-20261110/analyze.py',
'python3 experiments/incremental-vs-strongplus-expanded-20261110/plot.py',
'python3 experiments/incremental-vs-strongplus-expanded-20261110/report.py',
'```','',
'生成器记录程序集哈希以防续跑混入不同版本；权威回放脚本不要求跨机器生成器二进制相同。`COMPARISON_TRX` 可指定本轮测试 TRX，默认 `/tmp/strongplus-expanded-validation/full.trx`；`DOTNET` 可指定可执行文件。所有策略源码保持冻结。','']
(sroot/'README.md').write_text('\n'.join(lines));print('Expanded report generated')
