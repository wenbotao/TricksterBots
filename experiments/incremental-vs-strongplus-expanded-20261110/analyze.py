"""Paired complete-match effects, mirrored direct matches, and serial matched-state costs."""
from collections import defaultdict, Counter
import gzip
import json
from pathlib import Path
import numpy as np
from scipy.stats import beta, binomtest

study=Path(__file__).resolve().parent;root=study/'results'
read=lambda p:json.loads(p.read_text())
def zipread(p):
    with gzip.open(p,'rt') as f:return json.load(f)
def cp(k,n,alpha=.05):
    return None if not n else [0. if k==0 else float(beta.ppf(alpha/2,k,n-k+1)),1. if k==n else float(beta.ppf(1-alpha/2,k+1,n-k))]
def bootstrap(values):
    if not values:return None
    a=np.array(values,dtype=float);rng=np.random.default_rng(233119001);samples=rng.integers(0,len(a),(20000,len(a)))
    return {'mean':float(a.mean()),'blockBootstrap95':np.quantile(a[samples].mean(1),[.025,.975]).tolist(),'blocks':len(a),'degenerate':bool(np.all(a==a[0]))}
def desc(values):
    return None if not values else {'count':len(values),'mean':float(np.mean(values)),'p50':float(np.median(values)),'p95':float(np.quantile(values,.95))}
plans=read(root/'plan.json');records=[];paired={};direct={}
for p in plans:
    directory=root/p['Group']/f"block-{p['Id']:03}";names=['incremental','plus'] if p['Group']=='replacement' else ['leg0','leg1']
    rows={a:read(directory/(a+'.json')) for a in names if (directory/(a+'.json')).exists()}
    records.extend(rows.values())
    if (directory/'complete.json').exists():
        (paired if p['Group']=='replacement' else direct)[p['Id']]=rows
summary={'PlannedMatches':320,'FinishedMatches':len(records),'ReplacementBlocks':len(paired),'DirectPairs':len(direct),'Complete':len(records)==320 and len(paired)==128 and len(direct)==32,'Replacement':{},'Direct':{},'Telemetry':{}}
for arm in ['incremental','plus']:
    rows=[r for r in records if r['Group']=='replacement' and r['Arm']==arm]
    completed=[r for r in rows if not r['Truncated'] and r['Winner'] is not None];wins=sum(r['Subjects'][arm]['Win'] for r in completed)
    summary['Replacement'][arm]={'Games':len(rows),'Completed':len(completed),'Wins':wins,'Losses':len(completed)-wins,'Truncated':sum(r['Truncated'] for r in rows),
        'WinRate':wins/len(completed) if completed else None,'WinRateExact95':cp(wins,len(completed)),
        'Rounds':sum(r['Rounds'] for r in rows),'PlayedDeals':sum(r['PlayedDeals'] for r in rows),'Redeals':sum(r['Redeals'] for r in rows),
        'SubjectContracts':sum(r['Subjects'][arm]['Contracts'] for r in rows),'SubjectMade':sum(r['Subjects'][arm]['Made'] for r in rows),
        'RepairEvents':sum(r['Subjects'][arm]['RepairEvents'] for r in rows),'MatchSeconds':desc([r['Seconds'] for r in rows]),
        'TerminalRelativeScore':desc([r['Subjects'][arm]['RelativeScore'] for r in completed])}
rows=[(b['incremental'],b['plus']) for b in paired.values() if not b['incremental']['Truncated'] and not b['plus']['Truncated']]
n=len(rows);improved=sum(a['Subjects']['incremental']['Win'] and not b['Subjects']['plus']['Win'] for a,b in rows);worsened=sum(b['Subjects']['plus']['Win'] and not a['Subjects']['incremental']['Win'] for a,b in rows)
u=cp(improved,n,.025);d=cp(worsened,n,.025)
summary['ReplacementPaired']={'Pairs':n,'Improved':improved,'Worsened':worsened,'BothWin':sum(a['Subjects']['incremental']['Win'] and b['Subjects']['plus']['Win'] for a,b in rows),
    'BothLose':sum(not a['Subjects']['incremental']['Win'] and not b['Subjects']['plus']['Win'] for a,b in rows),
    'WinDifference':(improved-worsened)/n if n else None,'ConservativeExact95Difference':[u[0]-d[1],u[1]-d[0]] if n else None,
    'ExactMcNemarP':float(binomtest(improved,improved+worsened,.5).pvalue) if improved+worsened else 1.,
    'PairedWinBootstrap':bootstrap([int(a['Subjects']['incremental']['Win'])-int(b['Subjects']['plus']['Win']) for a,b in rows]),
    'PairedTerminalRelativeScore':bootstrap([a['Subjects']['incremental']['RelativeScore']-b['Subjects']['plus']['RelativeScore'] for a,b in rows]),
    'PairedRounds':bootstrap([a['Rounds']-b['Rounds'] for a,b in rows])}
direct_rows=[r for r in records if r['Group']=='direct'];completed=[r for r in direct_rows if not r['Truncated']]
win=sum(r['Subjects']['incremental']['Win'] for r in completed);scores=[]
for pair in direct.values():
    if any(r['Truncated'] for r in pair.values()):continue
    scores.append(sum(r['Subjects']['incremental']['Win'] for r in pair.values())/2)
summary['Direct']={'Games':len(direct_rows),'Completed':len(completed),'Truncated':sum(r['Truncated'] for r in direct_rows),'IncrementalWins':win,'PlusWins':len(completed)-win,
    'IncrementalWinRate':win/len(completed) if completed else None,'CompleteSeedPairs':len(scores),'SeedPairWinFractions':scores,'ClusterWinRate':bootstrap(scores),
    'IncrementalSweeps':sum(x==1 for x in scores),'PlusSweeps':sum(x==0 for x in scores),'SplitPairs':sum(x==.5 for x in scores),
    'ByIncrementalSeat':{str(seat):{'Completed':sum(r['Subjects']['incremental']['Seat']==seat for r in completed),'Wins':sum(r['Subjects']['incremental']['Win'] for r in completed if r['Subjects']['incremental']['Seat']==seat)} for seat in [0,1]}}
# Descriptive telemetry separated by design; natural trajectories differ between strategies.
for group in ['replacement','direct']:
    for arm in ['incremental','plus']:
        phase_calls=defaultdict(list);contract_counts=defaultdict(lambda:{'Deals':0,'OwnTeamContracts':0,'OwnTeamMade':0,'OpponentContracts':0,'OpponentSet':0})
        for r in records:
            if r['Group']!=group or arm not in r['Subjects']:continue
            prefix=root/group/f"block-{r['Id']:03}"/r['Arm']
            telemetry=zipread(Path(str(prefix)+'.calls.gz'))[arm]
            for c in telemetry:phase_calls[c['Phase']].append(c)
            game=zipread(Path(str(prefix)+'.game.gz'));team=r['Subjects'][arm]['Seat']%2
            for deal in game['Rounds']:
                if deal['Redeal']:continue
                c=deal['Contract'];kind=c['Kind'] if c['Kind']!='Normal' else ('NoTrump' if c['Trump']=='Unknown' else 'Suit')
                item=contract_counts[kind];item['Deals']+=1
                if deal['Declarer']%2==team:item['OwnTeamContracts']+=1;item['OwnTeamMade']+=int(deal['Made'])
                else:item['OpponentContracts']+=1;item['OpponentSet']+=int(not deal['Made'])
        summary['Telemetry'][group+'/'+arm]={'Phases':{phase:{'Milliseconds':desc([c['Milliseconds'] for c in cs]),'AllocatedBytes':desc([c['AllocatedBytes'] for c in cs]),
            'TotalMilliseconds':sum(c['Milliseconds'] for c in cs),'Rollouts':sum(c['Rollouts'] for c in cs),'SearchChanged':sum(bool(c['Search'] and c['Search']['Changed']) for c in cs)} for phase,cs in phase_calls.items()},'Contracts':dict(contract_counts)}
comparisons=root/'comparisons.json'
if comparisons.exists():
    comps=read(comparisons);changes=[x for x in comps if x['FirstDifference'] is not None]
    summary['NaturalBehavior']={'Blocks':len(comps),'SameContextComparisons':sum(c['AlignedContexts'] for c in comps),'IdenticalActions':sum(c['IdenticalActions'] for c in comps),
        'SameContextDifferentActions':sum(len(c['Differences']) for c in comps),'FirstDifferencePhases':dict(Counter(c['FirstDifference']['Incremental']['Phase'] for c in changes)),
        'ChangedBlocks':len(changes),'Differences':[{'Block':c['Id'],**d} for c in comps for d in c['Differences']]}
micro=root/'micro-benchmark.json'
if micro.exists():
    data=read(micro);groups=defaultdict(list)
    for r in data:groups[r['Bucket']].append(r)
    result={'PairedMeasurements':len(data),'UniqueStates':len({r['Id'] for r in data}),'Groups':{}}
    for name,rs in groups.items():
        by_state=defaultdict(list)
        for r in rs:by_state[r['Id']].append(r)
        assert len(by_state)==24 and all(len(xs)==3 for xs in by_state.values())
        states=[]
        for xs in by_state.values():
            for side in ['Incremental','Plus']:
                assert len({x[side]['Action'] for x in xs})==1 and len({json.dumps(x[side]['Search'],sort_keys=True) for x in xs})==1
            states.append({'Id':xs[0]['Id'],'SourceBlock':xs[0]['SourceBlock'],'SourceArm':xs[0]['SourceArm'],
                'SameAction':xs[0]['SameAction'],**{side:{field:float(np.median([r[side][field] for r in xs])) for field in ['Milliseconds','AllocatedBytes','Rollouts']} for side in ['Incremental','Plus']}})
        item={'States':len(states),'IndependentSourceBlocks':len({x['SourceBlock'] for x in states}),
            'SourceArms':dict(Counter(x['SourceArm'] for x in states)),'DifferentActions':sum(not x['SameAction'] for x in states),
            **{side:{field:float(np.median([x[side][field] for x in states])) for field in ['Milliseconds','AllocatedBytes','Rollouts']} for side in ['Incremental','Plus']}}
        if name!='forced':
            for metric,label in [('Milliseconds','TimeRatio'),('AllocatedBytes','AllocationRatio')]:
                clusters=defaultdict(list)
                for x in states:clusters[x['SourceBlock']].append(np.log(x['Incremental'][metric]/x['Plus'][metric]))
                blockmeans=np.array([np.mean(xs) for xs in clusters.values()]);rng=np.random.default_rng(333119001)
                samples=rng.integers(0,len(blockmeans),(20000,len(blockmeans)))
                item[label]={'GeometricMeanBySourceBlock':float(np.exp(blockmeans.mean())),
                    'SourceBlockBootstrap95':np.exp(np.quantile(blockmeans[samples].mean(1),[.025,.975])).tolist(),
                    'Clusters':len(blockmeans),'StateMedianRatio':float(np.median([x['Incremental'][metric]/x['Plus'][metric] for x in states]))}
        item['BySourceArm']={arm:{'States':sum(x['SourceArm']==arm for x in states),
            'MedianStateTimeRatio':float(np.median([x['Incremental']['Milliseconds']/x['Plus']['Milliseconds'] for x in states if x['SourceArm']==arm])) if name!='forced' else None} for arm in ['incremental','plus']}
        result['Groups'][name]=item
    result['Differences']=[{k:r[k] for k in ['Id','SourceBlock','SourceArm','Phase','Bucket']}|{'Incremental':r['Incremental']['Action'],'Plus':r['Plus']['Action']} for r in data if r['Repeat']==0 and not r['SameAction']]
    summary['MatchedMicro']=result
summary['Limits']=['128 new replacement blocks and32 new direct seed pairs are distinct designs, do not pool their win rates',
    'Default rules only; no parameter retuning or hand screening; direct matchup uses Medium partners',
    'Full variants differ in strategy/visibility/threshold/candidates and engineering; no pure engineering causal attribution',
    'Natural cost data concurrent across four processes and different trajectories; serial matched-state data is primary cost evidence',
    'StrongPlus default rollout count derived from fresh search reports, not raw0 wrapper counter']
# Old data is auxiliary; source hashes match, but cost numbers are never pooled across runtimes.
old=read(study.parent/'incremental-vs-strongplus-20261109/summary.json')
a=summary['ReplacementPaired'];o=old['ReplacementPaired'];total=a['Pairs']+o['Pairs'];good=a['Improved']+o['Improved'];bad=a['Worsened']+o['Worsened']
u=cp(good,total,.025);d=cp(bad,total,.025)
pooled={'Replacement':{'PairedWinBootstrap':bootstrap([1]*good+[-1]*bad+[0]*(total-good-bad)),'Pairs':total,'Improved':good,'Worsened':bad,'WinDifference':(good-bad)/total,
    'ConservativeExact95Difference':[u[0]-d[1],u[1]-d[0]],'ExactMcNemarP':float(binomtest(good,good+bad,.5).pvalue) if good+bad else 1.}}
for arm in ['incremental','plus']:
    n=summary['Replacement'][arm]['Completed']+old['Replacement'][arm]['Completed'];w=summary['Replacement'][arm]['Wins']+old['Replacement'][arm]['Wins']
    pooled['Replacement'][arm]={'Completed':n,'Wins':w,'Losses':n-w,'WinRate':w/n,'WinRateExact95':cp(w,n)}
ss=scores+old['Direct']['SeedPairWinFractions']
pooled['Direct']={'Games':summary['Direct']['Completed']+old['Direct']['Completed'],
    'IncrementalWins':summary['Direct']['IncrementalWins']+old['Direct']['IncrementalWins'],
    'PlusWins':summary['Direct']['PlusWins']+old['Direct']['PlusWins'],'CompleteSeedPairs':len(ss),'ClusterWinRate':bootstrap(ss)}
summary['PooledAuxiliary']=pooled
(study/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print('MATCHES',len(records),'/320 REPLACEMENT',len(paired),'/128 DIRECT',len(direct),'/32')
for arm,a in summary['Replacement'].items():print(arm,'W/L/truncated',a['Wins'],a['Losses'],a['Truncated'])
print('PAIRED',json.dumps(summary['ReplacementPaired']));print('DIRECT',json.dumps(summary['Direct']))
