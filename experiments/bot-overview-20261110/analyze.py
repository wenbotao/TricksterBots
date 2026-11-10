"""Reconcile the nine-bot engineering comparison and generate the repository overview."""
import hashlib
import json
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
DATA=HERE/'results'
read=lambda p:json.loads(p.read_text())
reg=read(DATA/'registration.json')
profiles=reg['Profiles']
labels=dict(zip(profiles,['Random','RandomPass','Weak','Medium','Fast','Strong','Partner','StrongPlus','StrongFlowCached']))
rows=read(DATA/'measurements.json')
assert len(rows)==432 and len(profiles)==9
assert hashlib.sha256((ROOT/reg['Source']).read_bytes()).hexdigest().upper()==reg['SourceSha256']
for source,digest in reg['SourceHashes'].items():
    assert hashlib.sha256((ROOT/source).read_bytes()).hexdigest().upper()==digest,source
states={}
for row in rows:
    assert set(row['Measurements'])==set(profiles)
    states.setdefault(row['Id'],[]).append(row)
assert len(states)==144
for cases in states.values():
    assert len(cases)==3 and {r['Repeat'] for r in cases}=={0,1,2}
    for profile in profiles:
        assert len({r['Measurements'][profile]['Action'] for r in cases})==1

FLOW='strong-flow-cached-v1'
old=read(ROOT/'experiments/flow-cached-v1-20261110/results/measurements.json')
old_actions={row['Id']:row['Measurements'][1]['Action'] for row in old}
assert all(row['Measurements'][FLOW]['Action']==old_actions[row['Id']] for row in rows)

def interval(grouped):
    logs=np.array([np.mean(values) for values in grouped.values()])
    rng=np.random.default_rng(454119001)
    boot=np.exp(logs[rng.integers(0,len(logs),(20000,len(logs)))].mean(axis=1))
    return {'GeometricRatio':float(np.exp(logs.mean())),'Bootstrap95':np.quantile(boot,[.025,.975]).tolist(),'SourceBlocks':len(logs)}

performance={};paired={}
for bucket in ['bid','discard','early','middle','late','forced']:
    selected=[rs for rs in states.values() if rs[0]['Bucket']==bucket]
    assert len(selected)==24
    performance[bucket]={};paired[bucket]={}
    for profile in profiles:
        costs=[np.median([row['Measurements'][profile]['Milliseconds'] for row in rs]) for rs in selected]
        alloc=[np.median([row['Measurements'][profile]['AllocatedBytes'] for row in rs]) for rs in selected]
        performance[bucket][profile]={'States':24,'MedianMilliseconds':float(np.median(costs)),
            'StateMedianP95Milliseconds':float(np.quantile(costs,.95)),'MedianAllocatedBytes':float(np.median(alloc))}
        if bucket=='forced' or profile not in ['fast-rules-v1','partner-rules-v1','strong-rules-v1','strong-plus-v1',FLOW]:continue
        paired[bucket][profile]={}
        for metric in ['Milliseconds','AllocatedBytes']:
            grouped={}
            for rs in selected:
                a=float(np.median([row['Measurements'][FLOW][metric] for row in rs]))
                b=float(np.median([row['Measurements'][profile][metric] for row in rs]))
                grouped.setdefault(rs[0]['SourceBlock'],[]).append(np.log(a/b))
            paired[bucket][profile][metric]=interval(grouped)

ux_report=DATA/'ux/REPORT.md'
ux_report.write_text(ux_report.read_text().rstrip()+'\n')
ux=read(DATA/'ux/ux-results.json')
assert ux['Profiles']==profiles and ux['TechnicalErrors']==0
ux_summary={}
for profile in profiles:
    ux_summary[profile]={}
    for level in ['Critical','Advisory','Control']:
        checks=[r for r in ux['Results'] if r['Profile']==profile and r['Level']==level]
        ux_summary[profile][level]={'Passed':sum(r['Passed'] for r in checks),'Trials':sum(r['Trials'] for r in checks)}
    ux_summary[profile]['FailedChecks']=[{'Scenario':r['Scenario'],'Check':r['Check'],'Level':r['Level'],'Passed':r['Passed'],'Trials':r['Trials']} for r in ux['Results'] if r['Profile']==profile and r['Passed']<r['Trials']]
rules=read(DATA/'rule-validation.json')
assert len(rules)==7 and all(r['Replayed'] and r['MismatchRejected'] for r in rules)
rule_coverage=[]
for file in sorted(DATA.glob('rules-*.game.json')):
    game=read(file)
    decisions=[d for r in game['Rounds'] for d in r['Decisions']]
    rule_coverage.append({'File':file.name,'BidDecisions':sum(d['Phase']=='bid' for d in decisions),
        'DiscardDecisions':sum(d['Phase']=='discard' for d in decisions),'PlayDecisions':sum(d['Phase']=='play' for d in decisions)})
frozen={}
for manifest in [ROOT/'FiveHundredLab/frozen-baselines-v1.json',*sorted((ROOT/'FiveHundredLab').glob('frozen-profiles-*.json'))]:
    for file,digest in read(manifest).items():
        assert hashlib.sha256((ROOT/file).read_bytes().decode().replace('\r\n','\n').encode()).hexdigest()==digest,file
        frozen[file]=digest
for check in rules:
    stem='default' if check['Configuration']=='default' else Path(check['Configuration']).stem
    covered=[c for c in rule_coverage if c['File'].startswith('rules-'+stem) ]
    assert sum(c['DiscardDecisions'] for c in covered)>=1 and sum(c['PlayDecisions'] for c in covered)>=30,stem
previous_validation=read(ROOT/'experiments/flow-cached-v1-20261110/results/validation.json')
assert previous_validation['FullTestCounters']['passed']=='569'
for file,digest in read(ROOT/'experiments/flow-cached-v1-20261110/results/registration.json')['SourceHashes'].items():
    assert hashlib.sha256((ROOT/'FiveHundredLab/Bots/FiveHundred'/file).read_bytes()).hexdigest().upper()==digest
summary={'Profiles':labels,'Performance':performance,'FlowRelativeTo':paired,'Ux':ux_summary,'RuleCoverage':rule_coverage}
(HERE/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
validation={'Passed':True,'Profiles':9,'PublicStates':144,'Repeats':3,'MeasuredDecisionCalls':3888,
    'AllMeasuredActionsLegal':True,'AllRepeatedActionsDeterministic':True,'FlowActionsMatchPreviousEngineeringRun':432,
    'UxScenarios':len(ux['Scenarios']),'UxDecisions':len(ux['Scenarios'])*9*4,'UxErrors':0,
    'FlowConfigurationsReplayed':7,'RuleSmokeRecords':len(rule_coverage),'EveryConfigurationHasDiscardAndPlayCoverage':True,'FrozenSourceModelFiles':len(frozen),'PriorUnchangedSourceFullRegressionPassed':569}
(DATA/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')

pct=lambda n:f'{n*100:.2f}%'
strategies=[
('Random','RandomBot','random-rules-v1','所有阶段在合法动作内随机；无牌力或合作偏好','随机对照、压力与合法性基线'),
('RandomPass','RandomPassBot','random-pass-v1','永远 Pass；其他动作复用 Random，叫牌不消耗随机数','隔离叫牌影响的消极基线'),
('Weak','WeakBot','weak-rules-v1','局部牌力、有限普通合约、即时争墩；不采样','入门难度与低成本基线'),
('Medium','MediumBot','medium-rules-v1','完整迁入原 FiveHundredBot 的确定性规则判断；配置路径独立启发式','原策略基准、最低搜索成本的可解释对手'),
('Fast','FastBot','fast-rules-v1','Medium 可见手牌变体基准，候选代表裁剪、少量采样','速度优先的自动对手'),
('Strong','StrongBot','strong-rules-v1','Medium 基准+完整续演竞叫、较宽候选、8 个隐藏世界','已有充分随机混战证据的强度基准'),
('Partner','PartnerBot','partner-rules-v1','Fast+公开必破、搭档保牌/回引信号、弃牌修补','以真人搭档体验为优先目标'),
('StrongPlus','StrongPlusBot','strong-plus-v1','32 世界、24 弃牌候选、6+1.0SE 门槛、公开 Misère 证明','既有独立的强度优先版本'),
('StrongFlowCached','StrongFlowCachedBot','strong-flow-cached-v1','32 世界、12 弃牌候选、8+0.75SE、公开 Misère 证明与弃牌修补；全流程默认续演缓存','完整新版本，兼顾较宽搜索与工程成本')]
lines=['# 已完成 Five Hundred Bot 整体比较','',
'日期：2026-11-10（北京时间）。StrongFlowCachedBot 作为完整独立版本，与既有八个完成版本一同列入正式比较。源码策略没有因本次比较而改变；新增正式目录说明、源码冻结及统一评估数据。','',
'## 1. 结论与版本选择','',
'- StrongFlowCached 是新的完整搜索版本，支持相同的构造、规则配置、合法交互与工厂入口；不自动替换 Strong/StrongPlus。它保留增量版的决策，在默认规则下优化计算成本。',
f"- 成本以本轮九版、同环境、同公开局面的串行测量为准：Flow 相对 StrongPlus 的叫牌、弃牌、前/中/后期耗时比依次为 {paired['bid']['strong-plus-v1']['Milliseconds']['GeometricRatio']:.3f}、{paired['discard']['strong-plus-v1']['Milliseconds']['GeometricRatio']:.3f}、{paired['early']['strong-plus-v1']['Milliseconds']['GeometricRatio']:.3f}、{paired['middle']['strong-plus-v1']['Milliseconds']['GeometricRatio']:.3f}、{paired['late']['strong-plus-v1']['Milliseconds']['GeometricRatio']:.3f}；同时成本仍高于 Fast/Partner，除后期外也高于 8 世界的 Strong。不同历史实验的毫秒数不拼接排名。",
'- StrongFlowCached 没有独立完整比赛胜率样本。它与增量版的行为对照是工程证据，不能把增量版的比赛胜率直接改名为 StrongFlowCached 的实测成绩。',
'- 本轮体验探针中 Partner 为 28/28 明确约束、40/40 战术提示；Flow 为 28/28、34/40，未通过项集中于弱牌开叫及支持人类叫过的花色。探针不代表自然失误频率。',
'- 选择应区分目标：Random/RandomPass 作对照，Weak 作入门，Medium 作原算法基准，Fast 作速度方案，Partner 作真人搭档候选，Strong 作历史强度参照，StrongPlus 与 StrongFlowCached 保留为不同完整搜索方案。','',
'## 2. 正式版本及策略','',
'所有入口位于 [FiveHundredLab/Bots/FiveHundred](FiveHundredLab/Bots/FiveHundred/README.md)。参数均为 `(int seed = 1, Rules? rules = null)`，支持 `FromConfiguration(path, seed)`、IPlayer 与 IJokerDeclarationPlayer。表中 profile 通过 `Profiles.CreateForRules` 取得正式类，避免使用指向归档旧类的历史别名。','',
'| 版本 | 正式类 / profile | 主要策略 | 适用定位 |','| --- | --- | --- | --- |']
for name,cls,profile,strategy,use in strategies:
    lines.append(f'| {name} | [{cls}.cs](FiveHundredLab/Bots/FiveHundred/{cls}.cs) / `{profile}` | {strategy} | {use} |')
lines+=['',
'默认路径的预算差异：Fast 叫牌直接用基准；弃牌比较基准加前三个静态候选，4 世界；出牌经等价牌/末位裁剪后使用 2、4 或 8 世界。Strong 为 8 世界、12 弃牌候选、8+0.75SE；StrongPlus 为 32/24/6+1.0SE；StrongFlowCached 为 32/12/8+0.75SE。弃牌基准插入后去重，因此实际候选数可能比静态候选上限多一个。门槛以同样本配对收益差及标准误衡量，不是固定胜率阈值。',
'8+0.75SE 和 6+1.0SE 不存在对所有 SE 都成立的“更保守”次序；在 SE=8 时相等，较小/较大 SE 下顺序不同。',
'九版均没有预训练或在线对手学习。Adapt、Structured、Guard、Lean、Forced、Cached、Incremental 及消融参数保留为实验/回归参照，不混入正式九版排名。','',
'### StrongFlowCached 的完整性与独立性','',
'其 Bid/Discard/Play 及策略判断位于 StrongFlowCachedBot.cs；FlowPolicyCache 按一次根决策绑定规则并按将牌/Joker 声明复用 Medium.DefaultStrategy，FlowCachedRollout 维护独立隐藏世界与候选分支。该结构不是对 StrongPlus 的包装或动态参数别名。',
'默认路径还保留公开 Misère 必破/避险证明及无将 K 支撑弃牌修补；不启用此前已放弃的 Strong 内部续演、普通合约证明或预训练。固定样本的弃牌修补自然触发很少，不能把总体收益归因于单个补丁。','',
'## 3. 规则与信息边界','',
'省略配置得到传统 Rules；显式新格式 JSON 得到 ConfiguredRules。即使 legacy-au 配置表达的规则接近默认规则，bot 仍走不同策略路径。九版均绑定规则并拒绝引擎/玩家配置不一致，真实动作仍由 Engine 验证。',
'| 范围 | 默认 Rules 路径 | 显式 ConfiguredRules 路径 |','| --- | --- | --- |',
'| 规则读取 | 保留原澳式四人规则 | 四预设、自定义模板及混搭示例；以加载器支持边界为准 |',
'| Strong 系叫牌 | Strong/Plus/Flow 使用搜索续演 | 保留原计分启发式，并非同一叫牌搜索 |',
'| 续演 | 固定 Medium 策略；Flow 增加根决策缓存与直接输入 | Rules 驱动的配置续演；Flow 保留原实现 |',
'| 零墩参与者/Joker/公开时点 | 传统三人零墩流程 | 按配置执行，不能直接套用传统证明 |',
'| 本轮成本证据 | 144 普通合约公开状态 | 不作成本/胜率外推 |',
'| 本轮 Flow 功能核验 | 默认路径一副牌、四个独立 Flow 实例 | 六份交付 JSON，四个独立 Flow 实例；全 Pass 样本另以合法竞叫脚本触发弃/出牌，全部回放 |','',
'首轮 Canadian、自定义模板和显式 legacy-au 三个样本全 Pass，故另存带 `forced-auction` 标记的三副脚本竞叫牌谱，确保每配置都覆盖弃牌与完整打牌；这些脚本竞叫不是 Flow 的实测决策。规则功能回放是接口/执行检查，不是变体下的强度或全局无死局证明。本人手牌、自己的弃牌、公开竞叫/出牌与合法明牌按规则可见；不向 bot 提供真实隐藏手牌或引擎发牌种子。随机种子只用于自身选择/采样。Random 及 RandomPass 可随机作 Joker 预声明，其余版本保留拒绝可选预声明的行为。','',
'## 4. 同环境性能比较','',
'本轮覆盖完整 144 个公开局面，六阶段各 24 个、每阶段各 12 个来自增量版/StrongPlus 的自然轨迹，每状态 3 次；九版共 **3,888 次决策**。新建实例、同观察/同种子、分阶段预热、固定打乱状态顺序、轮换九版执行顺序；无比赛或测试并发。实例构造不计时，决策内缓存建立计入。',
'语料沿用既有工程集而非自然场景均匀抽样：各阶段等额不代表一局中的频率。RandomPass 不会成为定约者，其弃牌成本仅为接口假设输入，不是自然触发开销。',
'下表先对每个状态三次取中位数，再对 24 状态取中位数。唯一合法出牌单独以微秒显示，极短测量只作量级描述。','',
'| Bot | 叫牌 ms | 弃牌 ms | 前期 ms | 中期 ms | 后期 ms | 唯一合法 μs |','| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
for profile in profiles:
    vals=[performance[b][profile]['MedianMilliseconds'] for b in ['bid','discard','early','middle','late']]
    cells=' | '.join(f'{v:.4f}' if v<.1 else f'{v:.2f}' for v in vals)
    forced=performance['forced'][profile]['MedianMilliseconds']*1000
    lines.append(f'| {labels[profile]} | {cells} | {forced:.2f} |')
lines+=['',
'![九版同局面决策耗时，颜色使用对数刻度](experiments/bot-overview-20261110/performance.png)','',
'| Bot | 叫牌分配 MB | 弃牌分配 MB | 前期分配 MB | 中期分配 MB | 后期分配 MB |','| --- | ---: | ---: | ---: | ---: | ---: |']
for profile in profiles:
    cells=' | '.join(f"{performance[b][profile]['MedianAllocatedBytes']/1e6:.3f}" for b in ['bid','discard','early','middle','late'])
    lines.append(f'| {labels[profile]} | {cells} |')
lines+=['','累计对象分配不是峰值内存；P95 状态中位数及所有配对值见 summary.json，不将自然比赛并发耗时混用。','',
'### StrongFlowCached 相对其他搜索版本','',
'以下为每状态先配对，再按来源发牌组等权汇总对数比的几何平均及组级 bootstrap 95% 区间（20,000 次）。<1 表示 Flow 更快；不同于上表两列中位数相除。多项区间为探索性结果，未作多重比较校正。','',
'| 阶段 | Flow / Fast | Flow / Partner | Flow / Strong | Flow / StrongPlus（95%） |','| --- | ---: | ---: | ---: | --- |']
for bucket in ['bid','discard','early','middle','late']:
    def ratio(profile):return paired[bucket][profile]['Milliseconds']['GeometricRatio']
    r=paired[bucket]['strong-plus-v1']['Milliseconds'];lo,hi=r['Bootstrap95']
    stage={'bid':'叫牌','discard':'弃牌','early':'前期','middle':'中期','late':'后期'}[bucket]
    lines.append(f"| {stage} | {ratio('fast-rules-v1'):.2f} | {ratio('partner-rules-v1'):.2f} | {ratio('strong-rules-v1'):.2f} | {r['GeometricRatio']:.3f}（{lo:.3f}～{hi:.3f}） |")
lines+=['',
'Flow/StrongPlus 弃牌差异同时包含 12 对 24 个静态候选和工程差异，不能全部解释为缓存收益。普通合约前中期两版通常相同世界与合法候选预算；与增量基线的独立行为对照显示工程改动没有更换搜索决策。后期几何比受两张手牌、后续唯一动作的局面影响，不直接计算整场总体加速。',
'Fast/Partner 通过更少的世界和候选换取低延迟；裁剪会在部分状态直接采用基准，因此配对几何比可能明显高于两列中位数相除。即使 Flow 单次更快于 StrongPlus，也不意味着能达到 Fast/Partner 的成本。随机/启发式版本更便宜，但成本低不代表合作质量或强度更高。','',
'## 5. 真人搭档体验代理检查','',
f"复用 {len(ux['Scenarios'])} 个可达脚本场景，每版本每场景 4 次（两种种子、正/逆手牌顺序），共 {validation['UxDecisions']} 次决策，非法动作/异常为 0。Critical 是明确体验约束，Advisory 是局面战术/约定提示，Control 是控制场景，不把三层合成总分。","",
'| Bot | 明确约束通过 | 战术/约定提示通过 | 控制场景通过 |','| --- | ---: | ---: | ---: |']
for profile in profiles:
    def score(level):
        v=ux_summary[profile][level];return f"{v['Passed']}/{v['Trials']}"
    lines.append(f"| {labels[profile]} | {score('Critical')} | {score('Advisory')} | {score('Control')} |")
lines+=['',
'这是固定探针，不是实战失误频率或真人满意度；两种种子不足以稳定估计随机策略/搜索策略的通过概率。探针曾参与策略开发，属于回归与问题定位。','',
'### StrongFlowCached 本次未全通过的检查','',
'| 场景 / 检查 | 层级 | 通过次数 |','| --- | --- | ---: |']
for fail in ux_summary[FLOW]['FailedChecks']:
    lines.append(f"| {fail['Scenario']} / {fail['Check']} | {fail['Level']} | {fail['Passed']}/{fail['Trials']} |")
if not ux_summary[FLOW]['FailedChecks']:lines.append('| 无 | — | 全部通过 |')
lines+=['',
'Partner 的显式保搭档赢家、约定信号与回引处理，是和 Strong 系搜索目标不同的设计取向。StrongFlowCached 继承的搜索和局部修补不等于包含 Partner 的全部合作规则；可证必破也不等于识别所有现实零墩攻防。具体动作、检查定义和每个种子见 [完整体验矩阵](experiments/bot-overview-20261110/results/ux/REPORT.md)。','',
'## 6. 已有实战证据：按实验分别解读','',
'### 同一随机生态的 1500 场历史对战','',
'默认规则、四席从五种 bot 独立等概率抽取、允许重复。统计座位参赛人次，区间按整场比赛重采样，不能把同队重复 bot 当独立胜负。','',
'| Bot | 胜 / 参赛人次 | 原始胜率 | 整场 bootstrap 95% |','| --- | ---: | ---: | --- |']
history=read(ROOT/'experiments/fivehundred-random-mix-20261105/analysis/analysis.json')
for row in history['rows']:
    lo,hi=row['ci'];lines.append(f"| {row['bot']} | {row['wins']}/{row['appearances']} | {pct(row['rate'])} | {pct(lo)}～{pct(hi)} |")
lines+=['',
'Strong 在该生态有明确优势，Weak 较低；控制阵容并作多重校正后，Fast/Medium/Partner 仍未形成可确认的完整强弱顺序。这些是历史成绩，不能作为九版同生态的统一排名。详见 [1500 场分析](experiments/fivehundred-random-mix-20261105/analysis/WIN_RATE_REPORT_ZH.md)。','',
'### StrongPlus 对 Strong','',
'既有镜像配对：纯队 StrongPlus×2 对 Strong×2，StrongPlus 43/60 胜（71.67%，配对区间 60.00%～81.67%）；双方搭档 Medium，StrongPlus 17/30 胜（56.67%，43.33%～70.00%）。前者有优势证据，后者不足以确认差异；搭档结构影响显著。详见 [StrongPlus 实验](experiments/strong-plus-v1-20261106/README.md)。','',
'### 增量版对 StrongPlus，及与 Flow 的关系','',
'新增 320 场为主要独立批次：128 个相同发牌/阵容替换配对，增量版 90/128、Plus 87/128，差 +2.34pp，配对区间 −4.69～+9.38pp；64 场直接镜像对战为增量版 35 胜、Plus 29 胜，增量版区间 45.31%～64.06%。未确认胜率差异。旧 80 场只作辅助合并：直接对战累计 43∶37。详见 [扩大测试](experiments/incremental-vs-strongplus-expanded-20261110/README.md)。',
'Flow 的 432 组工程对照、逐步续演和规则回放与增量基线相同；本次又验证相同 144 语料的动作重现。以上支持它保持已测行为，不证明任意输入下完全等价，也不把增量版比赛重标为 Flow 比赛。Flow 尚无独立完整比赛胜率估计。','',
'### Random 与 RandomPass','',
'Random 在上述 1500 场未参赛。RandomPass/Weak 的另一组 50 场随机阵容中，RandomPass 24/88（27.27%），Weak 74/108（68.52%）；1 场四个 RandomPass 全 Pass 截断不计胜负。该结果不能与五种 bot 的 1500 场原始百分比直接比较。详见 [50 场报告](experiments/randompass-weak-50-20261105/REPORT.md)。','',
'## 7. 综合判断及使用建议','',
'| 使用场景 | 优先考虑 | 依据与边界 |','| --- | --- | --- |',
'| 压力、合法性与叫牌消融 | Random / RandomPass | 随机基线；RandomPass 同场全员时可持续重发 |',
'| 入门难度、低延迟对手 | Weak / Medium | 即时启发式成本低；Medium 保留原算法 |',
'| 大量自动对战、速度优先 | Fast | 少世界/代表候选；与 Strong 系计算量目标不同 |',
'| 一位真人的队友 | Partner 作为初选 | 目标直接覆盖搭档体验，仍需真人试玩与约定说明；不以胜率一项定夺 |',
'| 稳定历史强度参照 | Strong | 有 1500 场同生态证据；8 世界使其仍有成本优势 |',
'| 扩大弃牌覆盖的搜索方案 | StrongPlus | 24 弃牌候选及已有对 Strong 证据，较高成本 |',
'| 默认规则的完整新搜索入口 | StrongFlowCached | 较宽搜索+显著工程改善；尚未独立证明胜率更高，不替代全部旧版本 |','',
'下一步若继续工程优化，可剖析 Medium 手段内的重复牌堆分组与可见手牌解析，再分别处理 ConfiguredRules 的缓存失效与 mutable history。若关注真人体验，应从本次未通过的具体局面提出明确策略目标，另命名版本验证；若关注强度，另设九版统一生态或定向配对赛，不从成本指标推断胜率。','',
'## 8. 源码关系、验证及复现','',
'```mermaid','flowchart TD',
'  P[Profiles.CreateForRules] --> R[Random / Weak / Medium / Fast / Strong / Partner / StrongPlus / StrongFlowCached]',
'  RP[RandomPass] --> RA[RandomBot]',
'  PA[PartnerBot] --> F[FastBot]',
'  FS[Fast / Strong / StrongPlus] --> M[MediumBot 中的 DefaultStrategy 或 VisibilityStrategy]',
'  FC[StrongFlowCachedBot] --> C[FlowPolicyCache]',
'  FC --> L[FlowCachedRollout]',
'  C --> M',
'  L --> C',
'  M --> B[BaseBot / 平台卡牌模型]',
'  R --> Q[Rules / ConfiguredRules / 公共采样与估值]',
'  E[Engine] --> Q','```','',
'正式 Medium 不再调用旧 Trickster.Bots.FiveHundredBot；旧类保留给原 API 与历史兼容。策略逻辑仍在各 bot 文件，Flow 支持文件只处理数据适配和分支执行。共享 Medium 或规则设施变化会影响调用者，因此应保留源码冻结与逐动作回归。','',
f"本轮统一评估验证：{validation['MeasuredDecisionCalls']} 次合法成本决策、{validation['UxDecisions']} 次体验探针零异常、7 个 Flow 规则配置（含三副脚本竞叫补充）回放、{len(frozen)} 个源码/模型冻结校验通过。上一轮未变更的核心源码已通过 569/569 全量回归；本轮仅新增正式登记、评估程序、冻结清单和文档。",'',
'数据、成本区间、逐状态 P95、规则覆盖及校验见 [summary.json](experiments/bot-overview-20261110/summary.json)、[validation.json](experiments/bot-overview-20261110/results/validation.json)、[registration.json](experiments/bot-overview-20261110/results/registration.json)。原始测量保存于同目录 measurements.json；完整规则 smoke 牌谱用于审计，不是胜率测试样本。','',
'```sh',
'dotnet build experiments/bot-overview-20261110 -c Release',
'dotnet experiments/bot-overview-20261110/bin/Release/net8.0/Study.dll bench',
'dotnet experiments/bot-overview-20261110/bin/Release/net8.0/Study.dll ux',
'dotnet experiments/bot-overview-20261110/bin/Release/net8.0/Study.dll rules',
'python3 experiments/bot-overview-20261110/analyze.py',
'python3 experiments/bot-overview-20261110/plot.py',
'```','',
'从仓库根目录运行，使用 .NET 8 与 Python NumPy（绘图另需 Matplotlib）。请先备份结果目录；成本串行运行，不与其他对局、测试混跑。']
text='\n'.join(lines)+'\n'
# Separate prose from tables and code blocks for consistent Markdown rendering.
text=text.replace('路径。\n| 范围','路径。\n\n| 范围')
(ROOT/'FIVEHUNDRED_BOT_COMPARISON.md').write_text(text)
(HERE/'README.md').write_text('# 九个正式 Bot 的统一比较\n\n[整体报告](../../FIVEHUNDRED_BOT_COMPARISON.md) · [统计](summary.json) · [验证](results/validation.json)\n\n本轮无新胜率比赛。固定 144 公开状态、三次重复、九版串行成本计量；17 场景×4 次×9 版体验探针；新正式 StrongFlowCached 的默认及六份配置 smoke/replay。完整方法、限制及复现步骤见整体报告。\n')
print(json.dumps(validation,ensure_ascii=False,indent=2))
print('Generated FIVEHUNDRED_BOT_COMPARISON.md')
