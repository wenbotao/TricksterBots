"""Render the completed natural-match report from verified data."""
import json
from pathlib import Path

study = Path(__file__).resolve().parent
s = json.loads((study/'summary.json').read_text())
v = json.loads((study/'validation.json').read_text())
assert s['Complete'] and v['Passed']
names = {'unpatched':'未修补基准','guard':'保留补丁版','lean':'工程精简版'}
pct = lambda x:f'{100*x:.2f}%'
interval = lambda x:f'{100*x[0]:+.2f}～{100*x[1]:+.2f} 个百分点'
lines = [
'# 自然配对实战：弃牌补丁与工程精简版', '',
'本轮按“先确认自然比赛收益，再决定是否推进结构规则”的顺序执行。采用固定的 32 组发牌与阵容，实际运行三种版本，共 96 场完整比赛；没有筛选受影响手牌、调参或改动 bot。', '',
'## 参赛版本与设计', '',
'| 分支 | Profile | 用途 |', '| --- | --- | --- |',
'| 未修补基准 | `strong-misere-proof-v1` | 同样的 32 样本搜索与公开 Misère 判断，不加 K 支撑补丁 |',
'| 保留补丁版 | `strong-discard-guard-efficient-v1` | 验证旧补丁在自然比赛中的总体效果 |',
'| 工程精简版 | `strong-discard-guard-lean-v1` | 验证实际执行等价，观察工程成本 |', '',
'每组固定游戏种子、初始庄家、测试席位、其他三席 bot 类型及每席种子。测试席位与庄家均各位置 8 次；其他三席从 Weak、Medium、Fast、Strong、Partner 独立均匀抽取，允许重复、独立创建实例。默认规则 `Rules()`，每场 200 轮上限。', '',
'三分支均真实执行，没有复用相同动作的比赛结果。以两个并发组执行，每组内轮换分支次序。事前方案及策略源码哈希见 [registration.json](results/registration.json)，完整发牌与阵容计划见 [plan.json](results/plan.json)。没有引入 Strong 内部续演。', '',
'**统计单位是 32 个独立的发牌—阵容组，不是 96 个独立比较样本。** 胜率指测试 bot 所在队的整场胜率，不是测试 bot 对 Strong 的直接对打胜率，也不是其他参赛 bot 的排行榜。', '',
'## 整场结果', '',
'| 版本 | 胜 / 负 / 截断 | 所在队胜率（精确 95% 区间） | 发牌轮数 / 实际打牌副数 / 全 Pass 重发 |',
'| --- | --- | --- | --- |']
for arm,a in s['Arms'].items():
    ci=a['WinRateExact95']
    lines.append(f"| {names[arm]} | {a['Wins']} / {a['Losses']} / {a['Truncated']} | {pct(a['WinRate'])}（{pct(ci[0])}～{pct(ci[1])}） | {a['Rounds']} / {a['PlayedDeals']} / {a['Redeals']} |")
lines += ['', '截断不作输赢处理；配对分析只使用双方均结束的比赛。上表各版本共享发牌，不能把三行相加以缩窄区间。', '',
'| 配对比较 | 负转胜 / 胜转负 | 胜率差 | 保守精确 95% 差值范围 | 精确配对 p |', '| --- | --- | --- | --- | --- |']
for key,c in s['Comparisons'].items():
    new,old=key.split('-')
    lines.append(f"| {names[new]} − {names[old]} | {c['Improved']} / {c['Worsened']} | {100*c['PairedWinRateDifference']:+.2f} 个百分点 | {interval(c['ConservativeExact95Difference'])} | {c['ExactMcNemarP']:.3f} |")
lines += ['', '差值范围通过改善/恶化概率各自的 Clopper–Pearson 区间及 Bonferroni 合成，比较保守。没有不一致胜负对时，普通配对 bootstrap 会退化为 `[0,0]`；本报告不把它当作真实收益精确为零，也不把“不显著”解释成证明等价。', '',
'## 自然触发与行为差异', '']
e=s['NaturalRepairExposure'];behavior=s['Behavior']
lines += [f"工程精简版在 {e['ObservedMatches']} 场比赛中，测试席亲自承接了 {s['Arms']['lean']['SubjectContracts']} 个合约，其中普通无将 {e['SubjectNormalNoTrump']} 个；真正改变弃牌 {e['Events']} 次，涉及 {e['MatchesWithRepair']} 场比赛。只计工程精简版，不把等价补丁版重复当作独立暴露样本。", '',
    f"未修补基准与工程精简版有 {behavior['IdenticalUnpatchedActions']} 组动作完全相同，{behavior['ChangedUnpatchedActions']} 组发生动作差异。复核了共同轮次的全部发牌及行为记录；如存在差异，验证器还要求首次差异必须由同一公开观察下的实际补丁解释。详见 [comparisons.json](results/comparisons.json)。", '']
if e['Events']==0:
    lines += ['**本批自然比赛没有触发补丁，因此不能估计补丁介入时对整场胜率的影响。** 相同胜率主要来自实际走了相同策略路径，不能用来否定此前定向牌型测试中的条件收益。自然普通无将也不等于符合修补条件：还需原搜索留下特定的无保护 K 结构。', '']
else:
    lines += ['本批发生了补丁介入，但事件数量与独立比赛组数仍需同时考虑。首次弃牌之后的行为差异可能通过比分和后续采样继续传播，不能把全部后续差异独立归功于单个动作。', '']
lines += ['工程精简版与保留补丁版的 32 组完整牌谱，规范化测试者名称后逐字段一致；逐动作公开观察哈希、有序动作、续演次数、搜索报告及修补诊断也全部一致。这是在实际比赛中对工程精简的回归确认。', '',
'## 合约与得分', '',
'以下只列工程精简版，避免重复等价数据；所有分支明细保存在 [summary.json](summary.json)。', '',
'| 合约类别 | 总副数 | 本队承接 / 成功 | 对手承接 / 本队破约 |', '| --- | --- | --- | --- |']
for kind,c in s['Arms']['lean']['ContractTypes'].items():
    lines.append(f"| {kind} | {c['Deals']} | {c['SubjectTeamContracts']} / {c['SubjectTeamMade']} | {c['OpponentContracts']} / {c['OpponentSet']} |")
if not any(k in s['Arms']['lean']['ContractTypes'] for k in ['Misere','OpenMisere']):
    lines += ['', '本批没有出现 Misère 或 Open Misère 合约，因此没有提供这些合约下的新实战证据。']
lines += ['', '| 配对比较 | 最终本队相对得分差的平均变化 | 比赛轮数差的平均变化 |', '| --- | --- | --- |']
for key,c in s['Comparisons'].items():
    new,old=key.split('-')
    lines.append(f"| {names[new]} − {names[old]} | {c['PairedRelativeTerminalScore']['mean']:+.2f} | {c['PairedRounds']['mean']:+.2f} |")
lines += ['', '最终相对得分为本队累计分减对方累计分；受到终局停止和比赛长度影响，不能替代胜率。合约类别数据按自然发生的副牌汇总，同一场内多副不当作独立比赛样本。', '',
'## 决策成本', '',
'计时与分配统计只覆盖测试席 bot 决策主体，不包括其他三席、观察哈希、日志压缩与回放；当前线程的累计分配量不是峰值内存。各阶段的样本数和续演计数均保存。未修补旧基准的默认续演计数无法按同一语义读取，记录为缺失，不填零冒充没有搜索。', '',
'| 版本 | 阶段 | 次数 | 累计耗时 s | 平均 / 中位 / P95 耗时 ms | 平均分配 MB |', '| --- | --- | ---: | ---: | --- | ---: |']
for arm,a in s['Arms'].items():
    for phase,c in a['SubjectPhaseTelemetry'].items():
        t=c['Milliseconds'];mem=c['AllocatedBytes']
        lines.append(f"| {names[arm]} | {phase} | {t['count']} | {c['TotalMilliseconds']/1000:.2f} | {t['mean']:.2f} / {t['p50']:.2f} / {t['p95']:.2f} | {mem['mean']/1e6:.3f} |")
phase_data = s['Arms']['lean']['SubjectPhaseTelemetry']
total_ms = sum(d['TotalMilliseconds'] for d in phase_data.values())
total_alloc = sum(d['AllocatedBytes']['mean']*d['AllocatedBytes']['count'] for d in phase_data.values())
play = phase_data['play']
play_alloc = play['AllocatedBytes']['mean']*play['AllocatedBytes']['count']
lines += ['', f"工程精简版的出牌阶段占测试席总决策用时约 {100*play['TotalMilliseconds']/total_ms:.2f}%，占累计分配约 {100*play_alloc/total_alloc:.2f}%。全部阶段累计分配约 {total_alloc/1e12:.2f} TB，这是整个测试期间反复申请对象的累计量，不是常驻或峰值内存。下一轮应先剖析搜索续演的共享热点；继续仅精简无将弃牌索引，难以明显影响总体成本。"]
lines += ['', '本轮两个组并发，计时含 JIT、GC 和调度影响，阶段样本也具有同场相关性；这些数字用于定位成本，**不用于宣称工程版加速**。此前串行隔离的 24 组测量也尚未证明整体加速，见[工程精简报告](../discard-lean-v1-20261109/README.md)。', '',
'## 阵容分层', '', '| 搭档 | 场数 | 结束场数 | 本队胜场 |', '| --- | ---: | ---: | ---: |']
for name,a in s['LeanPartnerStrata'].items():
    lines.append(f"| {name} | {a['Matches']} | {a['Completed']} | {a['Wins']} |")
lines += ['', '每层还混有不同对手与发牌，样本很小；这不是搭档策略的因果比较，也不用于挑选有利阵容重算总胜率。', '',
'## 结论与后续顺序', '',
'1. 保留旧弃牌补丁及原 Medium 内部续演；工程精简版可作为后续代码维护入口，旧补丁版继续作为固定对照。没有根据本批小样本宣称它胜率更高或速度更快。',
'2. 工程精简的实际执行等价得到完整比赛验证；本轮没有进一步修改策略。',
'3. 不推广此前未显示可靠收益的结构惩罚与选择性复核。若继续策略研究，应拆分规则做消融，并用新的独立手牌与自然比赛验证；本批作为回归集保留。',
'4. 继续工程优化时，优先剖析搜索续演中的状态构造与累计分配，再逐项做行为等价修改；不更换内部续演，不通过缩小预算混淆速度与策略变化。', '']
if e['Events']==0:
    lines += ['补丁自然触发频率仍不清楚。若目标是精确估计整体胜率收益，需要更多不筛选发牌的独立比赛；若目标是研究补丁为何有效，则用定向局面研究机制，但必须继续将条件收益与整体比赛收益分开报告。', '']
lines += ['## 数据与复核', '',
    f"共 {v['Matches']} 场实际比赛，{v['CompletedMatches']} 场正常结束、{v['TruncatedMatches']} 场截断；全部重新回放验证。{v['SubjectDecisionsMatchedToRecords']} 个测试席叫牌/弃牌/出牌动作逐条匹配实际引擎记录。冻结检查：{v['Frozen']}。", '',
'原始文件位于 `results/matches/block-NNN/`：每分支 `.game.gz` 为真实引擎牌谱，`.calls.gz` 为逐动作遥测，`.json` 为比赛指标。全部采用默认规则的真实引擎发牌，不使用定向测试夹具或预设合约。复核结果见 [validation.json](validation.json)。', '',
'复现需要 .NET 8、Python NumPy/SciPy。从仓库根目录执行：', '', '```sh',
'dotnet build experiments/natural-matches-v1-20261109 -c Release',
'# 新建目录只准备一次；不要覆盖本次事前计划：',
'dotnet experiments/natural-matches-v1-20261109/bin/Release/net8.0/Study.dll /tmp/natural-matches-new prepare',
'dotnet experiments/natural-matches-v1-20261109/bin/Release/net8.0/Study.dll /tmp/natural-matches-new run',
'dotnet experiments/natural-matches-v1-20261109/bin/Release/net8.0/Study.dll /tmp/natural-matches-new verify',
'# 以下脚本分析本目录保存的 results：',
'python3 experiments/natural-matches-v1-20261109/analyze.py',
'python3 experiments/natural-matches-v1-20261109/verify.py',
'python3 experiments/natural-matches-v1-20261109/report.py',
'```', '',
'输出目录记录运行程序集哈希，重建导致哈希变化时应使用新目录。支持比赛级断点续跑；缺失档案、非法动作、回放错误或不明首次行为差异均报错，不默默跳过。没有上传或发布仓库。', '']
(study/'README.md').write_text('\n'.join(lines))
print('REPORT written:',study/'README.md')
