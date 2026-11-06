from pathlib import Path
import json,itertools
from collections import Counter,defaultdict
import numpy as np
from scipy.stats import binomtest,beta
r=Path(__file__).parent;root=r/'results';meta=json.loads((r/'metadata.json').read_text());rng=np.random.default_rng(8326999);summary={};all_logs=[]
for spec in meta['Comparisons']:
 name=spec['Name'];g=[json.loads(p.read_text()) for p in sorted((root/name).glob('pair-*-leg-*.json'))] if (root/name).exists() else []
 pairs=[]
 for pair in sorted(set(x['Pair'] for x in g)):
  legs=[x for x in g if x['Pair']==pair]
  if len(legs)==2 and all(x['Winner'] is not None and not x['Truncated'] for x in legs):pairs.append((pair,sum(x['AWin'] for x in legs)/2))
 if not pairs:
  summary[name]={'completedPairs':0,'plannedPairs':spec['Pairs'],'savedGames':len(g)};continue
 scores=np.array([x[1] for x in pairs]);n=len(pairs);boot=scores[rng.integers(0,n,size=(20000,n))].mean(1);sweeps=int((scores==1).sum());losses=int((scores==0).sum());split=int((scores==.5).sum())
 def bounds(k):
  return (float(beta.ppf(.0125,k,n-k+1)) if k else 0.,float(beta.ppf(.9875,k+1,n-k)) if k<n else 1.)
 swlo,swhi=bounds(sweeps);lslo,lshi=bounds(losses)
 exact=[max(0.,.5+.5*(swlo-lshi)),min(1.,.5+.5*(swhi-lslo))]
 obj={'pairConservative95':exact,'A':spec['A'],'B':spec['B'],'mixed':spec['Mixed'],'completedPairs':n,'plannedPairs':spec['Pairs'],'savedGames':len(g),'analyzedGames':2*n,'wins':int(2*scores.sum()),'winRate':float(scores.mean()),'pairBootstrap95':np.quantile(boot,[.025,.975]).tolist(),'sweeps':sweeps,'splits':split,'swept':losses,'pairedSignP':float(binomtest(sweeps,sweeps+losses).pvalue) if sweeps+losses else 1.0,'truncated':sum(x['Truncated'] for x in g)}
 lat={};contracts=defaultdict(lambda:Counter())
 for side in ['A','B']:
  rows=[p for game in g for p in game['Players'] if p['Name']!='medium-rules-v1' and (p['Seat']%2==game['ATeam'])==(side=='A')]
  totals=defaultdict(lambda:Counter())
  for p in rows:
   for phase in p['Phases']:totals[phase['Phase']]['calls']+=phase['Calls'];totals[phase['Phase']]['milliseconds']+=phase['Milliseconds']
  lat[side]={k:dict(v,meanMs=v['milliseconds']/v['calls']) for k,v in totals.items()}
 for pair,score in pairs:
  for leg in [0,1]:
   path=root/name/f'pair-{pair:03}-leg-{leg}.json.gz'
   import gzip
   with gzip.open(path,'rt') as f:log=json.load(f)
   result=next(x for x in g if x['Pair']==pair and x['Leg']==leg)
   assert log['Winner']==result['Winner'] and log['Seed']==result['Seed']
   all_logs.append(log)
   for rd in log['Rounds']:
    if rd['Redeal']:continue
    key=log['Players'][rd['Declarer']]+':'+rd['Contract']['Kind'];contracts[key]['count']+=1;contracts[key]['made']+=rd['Made'];contracts[key]['calledTricks']+=rd['Contract']['Tricks'];contracts[key]['instantNegativeLoss']+=int(not rd['Made'] and rd['Scores'][rd['Declarer']%2]<=-500)
 obj['latency']=lat;obj['contracts']={k:dict(v) for k,v in contracts.items()};summary[name]=obj
# Preserve the full six-comparison family after user-requested stopping; do not select by significance.
complete=[k for k,v in summary.items() if v['completedPairs']>0]
last=0
for rank,k in enumerate(sorted(complete,key=lambda k:summary[k]['pairedSignP'])):
 last=max(last,min(1,(len(complete)-rank)*summary[k]['pairedSignP']));summary[k]['holmAcrossPlannedComparisons']=last
result={'groups':summary,'totalSavedGames':sum(x['savedGames'] for x in summary.values()),'analyzedGames':sum(x.get('analyzedGames',0) for x in summary.values())}
p=root/'audit.json'
if p.exists():
 audit=json.loads(p.read_text());by=defaultdict(dict)
 for x in audit:by[(x['Scenario'],x['Seed'])][x['Profile']]=x
 profiles=sorted(set(x['Profile'] for x in audit));audit_result={'trials':len(audit),'contexts':len(by),'profiles':{},'comparisons':{}}
 for profile in profiles:
  rows=[x for x in audit if x['Profile']==profile];phases={}
  for phase in ['bid','discard','play']:
   a=[x for x in rows if x['Phase']==phase];phases[phase]={'count':len(a),'totalMs':sum(x['Milliseconds'] for x in a),'medianMs':float(np.median([x['Milliseconds'] for x in a]))}
  audit_result['profiles'][profile]={'checksFailed':sum(not c['Pass'] for x in rows for c in x['Checks']),'failedByLevel':dict(Counter(c.get('Level','Unspecified') for x in rows for c in x['Checks'] if not c['Pass'])),'phases':phases}
 for a,b in [('base8','strong'),('all32','strong'),('wide32','all32'),('tuned32','wide32'),('plus','tuned32'),('bid32','all32'),('discard32','all32'),('play32','all32')]:
  diff=Counter()
  for context,actors in by.items():
   if actors[a]['Action']!=actors[b]['Action']:diff[actors[a]['Phase']]+=1
  audit_result['comparisons'][a+' vs '+b]=dict(diff)
 assert not audit_result['comparisons']['base8 vs strong'],'Baseline migration changed decisions'
 result['audit']=audit_result
(r/'summary.json').write_text(json.dumps(result,indent=2)+'\n')
if all_logs:
 with open('/tmp/ablation-replay.jsonl','w') as f:
  for g in all_logs:f.write(json.dumps(g)+'\n')
print('saved',result['totalSavedGames'],'analyzed',result['analyzedGames'])
for k,v in summary.items():print(k,v['completedPairs'],'/',v['plannedPairs'], 'wins',v.get('wins'), 'rate',round(v.get('winRate',0)*100,1))
if 'audit' in result:print('audit',result['audit']['comparisons'])
