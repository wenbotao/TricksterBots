"""Verify frozen-policy behavior and summarize state-paired engineering costs."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import numpy as np

folder=Path(__file__).resolve().parent
repo=folder.parent.parent
results=folder/'results'
read=lambda name:json.loads((results/name).read_text())
rows=read('measurements.json');reg=read('registration.json');complete=read('complete.json')
assert len(rows)==432 and complete['ActionsAndSearchEqual']
assert hashlib.sha256((repo/reg['Source']).read_bytes()).hexdigest().upper()==reg['SourceSha256']
for name,digest in reg['SourceHashes'].items():
    assert hashlib.sha256((repo/'FiveHundredLab/Bots/FiveHundred'/name).read_bytes()).hexdigest().upper()==digest
states={}
for row in rows:states.setdefault(row['Id'],[]).append(row)
assert len(states)==144
for state, repeats in states.items():
    assert len(repeats)==3 and {r['Repeat'] for r in repeats}=={0,1,2}
    for profile in range(3):
        for field in ['Action','Search','Rollouts','Tactics','Guard']:
            assert len({json.dumps(r['Measurements'][profile][field],sort_keys=True) for r in repeats})==1,(state,profile,field)
    for r in repeats:
        for field in ['Action','Search','Rollouts','Tactics','Guard']:
            assert r['Measurements'][0][field]==r['Measurements'][1][field],(state,field)

def ratio_interval(values):
    # Equal source-block weight. Three repeats and correlated states are not independent samples.
    by_block={}
    for block,ratio in values:by_block.setdefault(block,[]).append(np.log(ratio))
    logs=np.array([np.mean(v) for v in by_block.values()])
    rng=np.random.default_rng(441119081)
    boots=np.exp(logs[rng.integers(0,len(logs),(20000,len(logs)))].mean(axis=1))
    return {'GeometricRatio':float(np.exp(logs.mean())),'Bootstrap95':np.quantile(boots,[.025,.975]).tolist(),'Blocks':len(logs)}

summary={}
for bucket in ['bid','discard','early','middle','late','forced']:
    cases=[repeats for repeats in states.values() if repeats[0]['Bucket']==bucket]
    assert len(cases)==24
    entry={'States':len(cases),'MillisecondsMedian':[],'AllocatedBytesMedian':[],'RolloutsMedian':[]}
    medians={}
    for key in ['Milliseconds','AllocatedBytes','Rollouts']:
        medians[key]=[[float(np.median([r['Measurements'][i][key] for r in repeats])) for i in range(3)] for repeats in cases]
        entry[{'Milliseconds':'MillisecondsMedian','AllocatedBytes':'AllocatedBytesMedian','Rollouts':'RolloutsMedian'}[key]]=np.median(medians[key],axis=0).tolist()
    for key in ['Milliseconds','AllocatedBytes']:
        for reference,label in [(0,'FlowVsIncremental'),(2,'FlowVsPlus')]:
            ratios=[(repeats[0]['SourceBlock'],values[1]/values[reference]) for repeats,values in zip(cases,medians[key])]
            if bucket!='forced':entry[label+key]=ratio_interval(ratios)
    work=[json.loads(repeats[0]['Measurements'][1]['DataWork']) for repeats in cases]
    entry['MedianStrategyInstances']=float(np.median([v['StrategyInstances'] for v in work if v])) if any(work) else 0
    entry['MedianDirectPlayCalls']=float(np.median([v['DirectPlayCalls'] for v in work if v])) if any(work) else 0
    summary[bucket]=entry

ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
trx=Path('/tmp/flow-cache-validation/full-final.trx')
if trx.exists():
    counters=ET.parse(trx).find('.//t:Counters',ns).attrib
    assert counters['failed']=='0' and counters['passed']==counters['total']
else:raise FileNotFoundError('Final-source full test report required')
frozen={}
for manifest in [repo/'FiveHundredLab/frozen-baselines-v1.json',*sorted((repo/'FiveHundredLab').glob('frozen-profiles-*.json'))]:
    for path,digest in json.loads(manifest.read_text()).items():
        # FrozenSourceHashV1 retains BOM; use decoded UTF-8 just like .NET UTF8.GetString.
        actual=hashlib.sha256((repo/path).read_bytes().decode('utf-8').replace('\r\n','\n').encode()).hexdigest()
        assert actual==digest,path
        frozen[path]=digest
validation={'Passed':True,'States':144,'PairedRepeats':432,'VersionsPerRepeat':3,'IdenticalIncrementalActions':432,
    'IdenticalIncrementalSearchReportsAndBudgets':432,'RepeatedDecisionsDeterministic':True,'FullTestCounters':counters,
    'FrozenUniqueFiles':len(frozen),'AllCostsMeasuredInSameSerialRun':True}
(results/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')
(folder/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
labels={'bid':'叫牌','discard':'弃牌','early':'前期出牌','middle':'中期出牌','late':'后期出牌','forced':'唯一合法出牌'}
lines=['# 全流程缓存优化 v1','',
'新增 `StrongFlowCachedBot`（`strong-flow-cached-v1`），基于 `strong-play-incremental-v1`。历史 bot 和策略支持文件保持冻结。','',
f"同局面动作和搜索结果全部保持一致。新缓存相对增量版的配对耗时几何比：叫牌 {summary['bid']['FlowVsIncrementalMilliseconds']['GeometricRatio']:.3f}，弃牌 {summary['discard']['FlowVsIncrementalMilliseconds']['GeometricRatio']:.3f}，前期 {summary['early']['FlowVsIncrementalMilliseconds']['GeometricRatio']:.3f}，中期 {summary['middle']['FlowVsIncrementalMilliseconds']['GeometricRatio']:.3f}。小于 1 表示加速；这是默认规则固定语料的阶段成本结果。",'',
'## 实现','',
'- 根决策内按将牌、Joker 领牌声明复用同一个 Medium.DefaultStrategy；规则由缓存生命周期绑定，复用 BaseBot 的牌堆、花色及最高牌派生缓存。每次根决策新建缓存并替换上一份，不跨 bot 或线程共享。',
'- 叫牌、弃牌的续演统一使用该缓存。唯一合法的完整动作直接执行，跳过无随机性、无学习状态的 Medium 调用；保留 Joker 声明动作。',
'- 出牌续演直接从分支状态构造策略输入，省去中间 Observation/PublicPlayer 和多层数组副本；分支拥有独立手牌、缺门、历史、得墩和明牌状态。',
'- 缺门按实际规则增量维护；已完成各墩的牌在每墩只构造一份只读快照，各分支不共享可写列表；叫牌历史每分支编码一次。',
'- 审查 DefaultStrategy 及其 BaseBot 出牌调用链后，省略未被消费的 PlayedCards 与 cardsPlayedInOrder 模型。保留已完成牌、缺门和所有参与决策的公开字段。此项依赖当前策略读取范围；未来若策略开始读取这些字段，须补回构造并更新对照测试。',
'- 采样、候选顺序、32 世界预算、弃牌修补、评分、置信门槛、Misère 证明及规则接口保持原样。显式 ConfiguredRules 路径目前不使用新缓存。','',
'## 验证','',
f"全量回归 {counters['passed']}/{counters['total']} 通过，{len(frozen)} 个既有源码/模型冻结文件校验通过。4 项专项测试覆盖逐步续演轨迹、收益、分支隔离、Joker、Misère/Open Misère、两组传统规则参数，以及默认/澳式/美式配置的引擎对照和权威回放。",'',
'固定成本语料中 144 个公开状态、每状态 3 次，共 432 组，新增版与增量基线的动作、搜索报告、续演预算及战术/弃牌修补诊断全部一致；每版重复运行确定。未新增胜率比赛，本轮结果支持工程行为保持，不作为一般意义的强度等价证明。','',
'## 成本','',
'沿用上一轮完整 144 状态语料，六阶段各 24 个。三个版本在同一次无并发对局的串行运行中重新测量；不比较历史绝对毫秒数。固定种子与状态打乱顺序、轮换版本执行顺序，实例创建在计量区外，根决策内缓存建立包含在计量内。各状态三次取中位数，再按来源发牌组等权汇总对数比并做 20,000 次组级 bootstrap。区间为探索性、未做多重比较校正。','',
'| 阶段 | 耗时中位数 ms：增量 / 新缓存 / Plus | 新缓存/增量耗时比（95%） | 新缓存/Plus 耗时比（95%） | 分配 MB：增量 / 新缓存 / Plus |',
'| --- | ---: | --- | --- | ---: |']
for bucket,entry in summary.items():
    def fmt(label):
        if bucket=='forced':return '不推断微秒比率'
        value=entry[label+'Milliseconds'];lo,hi=value['Bootstrap95']
        return f"{value['GeometricRatio']:.3f}（{lo:.3f}～{hi:.3f}）"
    ms=' / '.join(f'{x:.3f}' for x in entry['MillisecondsMedian'])
    mb=' / '.join(f'{x/1e6:.3f}' for x in entry['AllocatedBytesMedian'])
    lines.append(f"| {labels[bucket]} | {ms} | {fmt('FlowVsIncremental')} | {fmt('FlowVsPlus')} | {mb} |")
lines+=['',
'分配量表示累计对象分配，并非峰值内存。比值是配对且来源组等权的几何平均，不是表中两列中位数直接相除。分层等额语料不代表自然阶段频率，不汇总为整场加速。普通合约的成本不外推到 Misère；已复用语料不作为独立的新场景强度验证。','',
'## 尚未覆盖的优化','',
'本轮优化默认规则的全流程。显式 ConfiguredRules 的续演状态构造保留原样；其历史、Joker 声明和明牌状态具有规则相关的可变语义，需要独立设计缓存与失效条件。Medium 的传统叫牌评估内仍有逐次牌堆分组，默认可见手牌检查仍会解析手牌字符串；这些是后续可剖析的重复计算，本轮没有改动旧策略源码。','',
'## 源码依赖与使用','',
'`Profiles → StrongFlowCachedBot → FlowPolicyCache / FlowCachedRollout → MediumBot.DefaultStrategy → BaseBot`。隐藏世界仍由 BeliefSampler 产生，规则执行与计分仍由 Rules/ConfiguredRules 决定，真实游戏仍由 Engine 校验。','',
'```csharp',
'var bot = new StrongFlowCachedBot(seed: 7);',
'var configuredBot = StrongFlowCachedBot.FromConfiguration("FiveHundredRules/presets/au-reference-4p-v1.json", seed: 7);',
'// 或 Profiles.CreateForRules("strong-flow-cached-v1", 7, rules)',
'```','',
'## 复现与原始数据','',
'```sh',
'dotnet test TestBots -c Release --logger "trx;LogFileName=full-final.trx" --results-directory /tmp/flow-cache-validation',
'dotnet run --project experiments/flow-cached-v1-20261110 -c Release',
'python3 experiments/flow-cached-v1-20261110/analyze.py',
'```','',
'请串行运行测试与计量；运行前备份本目录 results。原始 [measurements.json](results/measurements.json)、[registration.json](results/registration.json)、[validation.json](results/validation.json) 和 [summary.json](summary.json) 保留逐状态数据及源码/输入哈希。来源语料见 [上一轮扩大测试](../incremental-vs-strongplus-expanded-20261110/README.md)。']
(folder/'README.md').write_text('\n'.join(lines)+'\n')
print(json.dumps(summary,ensure_ascii=False,indent=2))
