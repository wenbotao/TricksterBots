import json,math
from pathlib import Path
from collections import defaultdict,Counter
import numpy as np
from scipy.stats import binomtest,beta
r=Path(__file__).parent;root=r/'results';rng=np.random.default_rng(9419999);out={'stages':{}}
for planfile in sorted(root.glob('plan-*.json')):
 plan=json.loads(planfile.read_text());stage=plan['Stage'];groups={}
 for spec in plan['Comparisons']:
  name=spec['Name'];path=root/stage/name;games=[json.loads(p.read_text()) for p in sorted(path.glob('pair-*-leg-*.json'))] if path.exists() else []
  complete=[]
  for pair in range(spec['Pairs']):
   legs=[g for g in games if g['Pair']==pair]
   if len(legs)==2 and all(g['Winner'] is not None and not g['Truncated'] for g in legs):complete.append((pair,sum(g['AWin'] for g in legs)/2))
  v={'A':spec['A'],'B':spec['B'],'mixed':spec['Mixed'],'plannedPairs':spec['Pairs'],'completedPairs':len(complete),'savedGames':len(games),'truncated':sum(g['Truncated'] for g in games)}
  if complete:
   scores=np.array([x[1] for x in complete]);n=len(scores);wins=int(2*scores.sum());sw=int((scores==1).sum());ls=int((scores==0).sum())
   boot=scores[rng.integers(0,n,size=(20000,n))].mean(1)
   def cp(k):return (float(beta.ppf(.0125,k,n-k+1)) if k else 0.,float(beta.ppf(.9875,k+1,n-k)) if k<n else 1.)
   wl,wu=cp(sw);ll,lu=cp(ls)
   v.update(wins=wins,losses=2*n-wins,winRate=float(scores.mean()),sweeps=sw,splits=int((scores==.5).sum()),swept=ls,bootstrap95=np.quantile(boot,[.025,.975]).tolist(),conservative95=[max(0.,.5+.5*(wl-lu)),min(1.,.5+.5*(wu-ll))],pairedSignP=float(binomtest(sw,sw+ls).pvalue) if sw+ls else 1.)
  lat={}
  for side in ['A','B']:
   totals=defaultdict(lambda:Counter())
   for g in games:
    for p in g['Players']:
     if p['Name']=='medium-rules-v1' or (p['Seat']%2==g['ATeam'])!=(side=='A'):continue
     for ph in p['Phases']:totals[ph['Phase']]['calls']+=ph['Calls'];totals[ph['Phase']]['milliseconds']+=ph['Milliseconds']
   lat[side]={k:dict(x,meanMs=x['milliseconds']/x['calls']) for k,x in totals.items()}
  pools=[p for g in games for actor in g['Players'] for p in actor['DiscardPools']]
  for p in pools:assert p['CandidateCount']==p['OriginalCount']
  v['poolDiagnostics']={'queries':len(pools),'budgetViolations':0,'moreStructures':sum(p['Structures']>p['OriginalStructures'] for p in pools),'fewerStructures':sum(p['Structures']<p['OriginalStructures'] for p in pools),'oldMean':sum(p['OriginalStructures'] for p in pools)/len(pools) if pools else None,'newMean':sum(p['Structures'] for p in pools)/len(pools) if pools else None}
  v['latency']=lat;groups[name]=v
 out['stages'][stage]=groups
p=root/'audit.json'
if p.exists():
 audit=json.loads(p.read_text());by=defaultdict(dict)
 for x in audit:by[(x['Scenario'],x['Seed'])][x['Profile']]=x
 d={};phases={}
 for phase in ['bid','discard','play']:
  rows=[x for x in by.values() if x['structured']['Phase']==phase];phases[phase]={'contexts':len(rows),'structuredVsAll32Changed':sum(x['structured']['Action']!=x['all32']['Action'] for x in rows)}
 for name in ['strong','all32','bid32','plus','structured']:
  a=[x for x in audit if x['Profile']==name];d[name]={'queries':len(a),'checksFailed':sum(not c['Pass'] for x in a for c in x['Checks']),'failedByLevel':dict(Counter(c['Level'] for x in a for c in x['Checks'] if not c['Pass']))}
 for row in by.values():
  if row['structured']['Phase']=='discard':assert row['structured']['Pool']['CandidateCount']==row['all32']['Search']['Candidates']
 out['audit']={'queries':len(audit),'profiles':d,'phases':phases}
 out['audit']['freshActionChangesVsStrong']={name:{phase:{'queries':sum(x[name]['Phase']==phase for x in by.values()),'changed':sum(x[name]['Phase']==phase and x[name]['Action']!=x['strong']['Action'] for x in by.values())} for phase in ['bid','discard','play']} for name in ['all32','bid32','plus']}
# Screening rules were specified before observing any outcomes. Holdout data never affects this selection.
screen=out['stages'].get('screen',{})
if len(screen)==8 and all(v['completedPairs']==v['plannedPairs'] for v in screen.values()) and all(not v['truncated'] for v in screen.values()):
 preference={'bid32':0,'plus':1,'all32':2};ranks=[]
 for name in ['all32','bid32','plus']:
  g=[v for v in screen.values() if v['A']==name];wins=sum(x['wins'] for x in g);ranks.append({'profile':name,'wins':wins,'games':12})
 ranks.sort(key=lambda x:(-x['wins'],preference[x['profile']]))
 struct=[v for v in screen.values() if v['A']=='structured'];sw=sum(x['wins'] for x in struct)
 selection={'budget':ranks[0]['profile'] if ranks[0]['wins']>=6 else None,'budgetRanking':ranks,'structureAdvance':sw>=8,'structureWins':sw,'structureGames':16,'SelectionIsExploratory':True,'DoNotPoolWithHoldoutForConfirmation':True}
 (r/'selection.json').write_text(json.dumps(selection,indent=2)+'\n');out['selection']=selection
(r/'summary.json').write_text(json.dumps(out,indent=2)+'\n')
for stage,groups in out['stages'].items():
 print(stage,'saved',sum(x['savedGames'] for x in groups.values()))
 for name,v in groups.items():print(name,v['completedPairs'],'/',v['plannedPairs'],'wins',v.get('wins'), 'rate',round(v.get('winRate',0)*100,1))
if 'selection' in out:print('SELECTION',json.dumps(out['selection']))
