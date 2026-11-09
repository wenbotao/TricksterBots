"""Fresh-hand paired effects, clustered across levels, scores and layouts."""
import json
from pathlib import Path
from collections import Counter,defaultdict
import numpy as np

study=Path(__file__).resolve().parent;root=study/'results';hands=json.loads((root/'selected-hands.json').read_text())
proposals=[json.loads(p.read_text()) for p in sorted((root/'decisions').glob('*.json'))]
summary={'freshHands':len(hands),'decisionStates':len(proposals),'proposalDiagnostics':{},'policies':{}}
key=lambda d:tuple((c['Suit'],c['Rank']) for c in d)
summary['proposalDiagnostics']={'shapeDiffersV1':sum(key(p['Choices']['shape'])!=key(p['Choices']['v1']) for p in proposals),
    'auditReviewed':sum(p['Audit']['Reviewed'] for p in proposals),'auditVetoed':sum(p['Audit']['Vetoed'] for p in proposals),
    'extraAuditRollouts':sum(2*p['Audit']['Samples'] for p in proposals),'shapeEdits':dict(Counter(p['ShapeReport']['Edits'] for p in proposals))}
records=[];physical=0
for directory in sorted((root/'outcomes').glob('*')):
    if not (directory/'complete.json').exists():continue
    values=[json.loads(p.read_text()) for p in directory.glob('choice-*.json')];physical+=len(values)
    by_arm={arm:row for row in values for arm in row['Arms']};assert set(by_arm)=={'original','v1','shape','audit'}
    first=values[0];records.append((first,by_arm))
summary.update(completedGroups=len(records),physicalDeals=physical,logicalDeals=4*len(records),plannedGroups=len(hands)*8*2*2)
for policy_index,policy in enumerate(['medium','mixed']):
    rows=[(m,a) for m,a in records if m['Policy']==policy];result={'groups':len(rows),'comparisons':{},'conditions':{}}
    for comparison_index,(new,old) in enumerate([('v1','original'),('shape','v1'),('audit','shape')]):
        name=new+'-'+old;by_hand=defaultdict(list)
        for m,a in rows:by_hand[m['Hand']].append({'make':int(a[new]['Made'])-int(a[old]['Made']),
            'score':a[new]['RelativeScore']-a[old]['RelativeScore'],'utility':a[new]['Utility']-a[old]['Utility']})
        clusters=[{k:np.mean([x[k] for x in xs]) for k in ['make','score','utility']} for xs in by_hand.values() if len(xs)==16]
        item={'completeHandClusters':len(clusters)}
        if clusters:
            rng=np.random.default_rng(98119001+policy_index*100+comparison_index);sample=rng.integers(0,len(clusters),(20000,len(clusters)))
            for k in ['make','score','utility']:
                v=np.array([x[k] for x in clusters]);item[k]={'mean':float(v.mean()),'handClusterBootstrap95':np.quantile(v[sample].mean(1),[.025,.975]).tolist()}
        result['comparisons'][name]=item
    for condition in range(8):
        selected=[(m,a) for m,a in rows if m['Condition']==condition];by_hand=defaultdict(list)
        for m,a in selected:by_hand[m['Hand']].append((m,a))
        completed=[v for v in by_hand.values() if len(v)==2];item={'hands':len(completed)}
        for new,old in [('shape','v1'),('audit','shape')]:
            if not completed:continue
            item[new+'-'+old]={k:float(np.mean([np.mean([(int(a[new]['Made'])-int(a[old]['Made'])) if k=='make' else a[new]['RelativeScore']-a[old]['RelativeScore'] if k=='score' else a[new]['Utility']-a[old]['Utility'] for m,a in xs]) for xs in completed])) for k in ['make','score','utility']}
        result['conditions'][condition]=item
    summary['policies'][policy]=result
bench=root/'engineering-benchmark.json'
if bench.exists():
    b=json.loads(bench.read_text());summary['engineering']={'cases':len(b),'allDecisionsAndSearchesEqual':all(x['SameDecision'] for x in b),
        'referenceStaticEvaluations':sum(x['Reference']['StaticEvaluations'] for x in b),
        'cachedStaticEvaluations':sum(x['Cached']['StaticEvaluations'] for x in b),
        'referenceMedianMs':float(np.median([x['ReferenceMilliseconds'] for x in b])),
        'cachedMedianMs':float(np.median([x['CachedMilliseconds'] for x in b])),
        'timingLimitation':'Concurrent workload,8 alternating-order cases; no isolated speedup claim'}
summary['complete']=summary['completedGroups']==summary['plannedGroups']
(study/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print('GROUPS',summary['completedGroups'],'/',summary['plannedGroups'],'physical',physical)
print('PROPOSALS',summary['proposalDiagnostics'])
for p,r in summary['policies'].items():print(p,json.dumps(r['comparisons']))
