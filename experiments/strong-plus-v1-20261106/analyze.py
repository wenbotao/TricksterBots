import json,gzip,itertools
from pathlib import Path
from collections import Counter,defaultdict
import numpy as np
from scipy.stats import binomtest
r=Path(__file__).parent;results=r/'results';rng=np.random.default_rng(7116999)
games=[json.loads(p.read_text()) for p in sorted(results.glob('pair-*-leg-*.json'))]
summary={}
for label,mixed in [('pure',False),('mixed',True)]:
 subset=[g for g in games if g['Mixed']==mixed];pairs=[]
 for i in sorted(set(g['Pair'] for g in subset)):
  legs=[g for g in subset if g['Pair']==i]
  if len(legs)==2 and all(g['Winner'] is not None and not g['Truncated'] for g in legs):pairs.append((i,sum(g['PlusWin'] for g in legs)/2))
 scores=np.array([s for i,s in pairs]);n=len(scores)
 if not n:continue
 boot=scores[rng.integers(0,n,size=(20000,n))].mean(1);sweeps=int((scores==1).sum());losses=int((scores==0).sum());split=int((scores==.5).sum())
 summary[label]={'pairs':n,'games':2*n,'wins':int(2*scores.sum()),'winRate':float(scores.mean()),'pairBootstrap95':np.quantile(boot,[.025,.975]).tolist(),'sweeps':sweeps,'splits':split,'swept':losses,'pairedSignP':float(binomtest(sweeps,sweeps+losses).pvalue) if sweeps+losses else 1.0}
# Real-match comparison: first deal only; identical prefix means identical public/own context.
first=[];bid=[];replays=[];contracts={};decisions=Counter()
for pair in range(45):
 files=[results/f'pair-{pair:03}-leg-{leg}.json.gz' for leg in range(2)]
 if not all(p.exists() for p in files):continue
 logs=[]
 for p in files:
  with gzip.open(p,'rt') as f:log=json.load(f)
  logs.append(log);replays.append(log)
  for rd in log['Rounds']:
   for decision in rd['Decisions']:decisions[decision['Phase']]+=1
   if rd['Redeal']:continue
   who=log['Players'][rd['Declarer']];kind=rd['Contract']['Kind'];key=who+':'+kind
   c=contracts.setdefault(key,{'count':0,'made':0,'calledTricks':0});c['count']+=1;c['made']+=rd['Made'];c['calledTricks']+=rd['Contract']['Tricks']
 a,b=logs[0]['Rounds'][0],logs[1]['Rounds'][0];assert a['Hands']==b['Hands'] and a['Kitty']==b['Kitty']
 if pair<30:
  seat=a['Decisions'][0]['Seat'];old=a['Decisions'][0] if seat%2 else b['Decisions'][0];plus=b['Decisions'][0] if seat%2 else a['Decisions'][0]
  bid.append({'pair':pair,'changed':old['Bid']!=plus['Bid'],'Strong':old['Bid'],'Plus':plus['Bid']})
 for da,db in zip(a['Decisions'],b['Decisions']):
  if da!=db:
   first.append({'pair':pair,'phase':da['Phase'],'Leg0':da,'Leg1':db});break
summary['firstDealDifferences']=dict(Counter(x['phase'] for x in first));summary['firstBidComparisons']={'count':len(bid),'changed':sum(x['changed'] for x in bid)}
summary['contracts']=contracts;summary['decisionCounts']=dict(decisions)
summary['truncated']=sum(g['Truncated'] for g in games)
# Audits use fresh actors; these are designed situations, not randomly drawn live errors.
auditfile=results/'audit.json'
if auditfile.exists():
 audit=json.loads(auditfile.read_text());phases={}
 for phase in ['bid','discard','play']:
  a=[x for x in audit if x['Phase']==phase];phases[phase]={'count':len(a),'changed':sum(x['Changed'] for x in a),'strongMedianMs':float(np.median([x['StrongMs'] for x in a])),'plusMedianMs':float(np.median([x['PlusMs'] for x in a])),'strongTotalMs':sum(x['StrongMs'] for x in a),'plusTotalMs':sum(x['PlusMs'] for x in a),'budget32Changed':sum(x.get('PlusDiffersFromBudget',False) for x in a)}
 summary['audit']={'count':len(audit),'changed':sum(x['Changed'] for x in audit),'checksStrongFailed':sum(not c['Strong'] for x in audit for c in x['Checks']),'checksPlusFailed':sum(not c['Plus'] for x in audit for c in x['Checks']),'phases':phases}
summary['measuredLatency']={}
for name in ['strong-plus-v1','strong-rules-v1']:
 p=[p for g in games for p in g['Players'] if p['Name']==name];summary['measuredLatency'][name]={'calls':sum(x['Calls'] for x in p),'milliseconds':sum(x['Milliseconds'] for x in p),'meanMs':sum(x['Milliseconds'] for x in p)/sum(x['Calls'] for x in p)}
(r/'summary.json').write_text(json.dumps(summary,indent=2)+'\n');(r/'first-differences.json').write_text(json.dumps({'firstBid':bid,'firstPrefixDifference':first},indent=2)+'\n')
if len(games)==90:
 with open('/tmp/strongplus-replay.jsonl','w') as f:
  for game in replays:f.write(json.dumps(game)+'\n')
print(json.dumps(summary,ensure_ascii=False,indent=2))
