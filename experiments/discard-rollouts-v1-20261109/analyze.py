"""Same-case continuation sensitivity, paired and clustered by original hand template."""
import json
from collections import Counter,defaultdict
from pathlib import Path
import numpy as np

study=Path(__file__).resolve().parent;root=study/'results'
prior=study.parent/'guard-benefits-v1-20261109/results'
cases=json.loads((root/'selected-discards.json').read_text())
expected=Counter(c['Template'] for c in cases)
summary={'cases':len(cases),'templates':len(expected),'policies':{},'effectDifferencesVsMedium':{}}
effects={}
for policy,policy_index in [('weak',1),('medium',0),('fast',2),('strong',3)]:
    directory=prior if policy in ['medium','weak'] else root
    paired=[];by_context=defaultdict(list)
    for case in cases:
        for layout in range(8):
            paths=[directory/'continuations'/f"case-{case['Index']:04}"/f'layout-{layout}-{policy}-arm-{arm}.json' for arm in [0,1]]
            if not all(p.exists() for p in paths):continue
            before,after=[json.loads(p.read_text()) for p in paths]
            assert before['Index']==after['Index']==case['Index'] and before['Layout']==after['Layout']==layout
            row={'score':after['RelativeScore']-before['RelativeScore'],'make':int(after['Made'])-int(before['Made']),
                 'tricks':sum(after['Tricks'][::2])-sum(before['Tricks'][::2]),'baselineMake':int(before['Made']),'repairedMake':int(after['Made'])}
            paired.append(row);by_context[(case['Template'],case['Index'])].append(row)
    by_template=defaultdict(list)
    for (template,index),rows in by_context.items():
        if len(rows)==8:by_template[template].append({k:float(np.mean([r[k] for r in rows])) for k in rows[0]})
    tpl={t:{k:float(np.mean([c[k] for c in contexts])) for k in contexts[0]} for t,contexts in by_template.items() if len(contexts)==expected[t]}
    effects[policy]=tpl
    result={'pairedLayouts':len(paired),'completeContexts':sum(len(v)==8 for v in by_context.values()),
            'completeTemplateClusters':len(tpl),'rawBaselineMade':sum(r['baselineMake'] for r in paired),'rawRepairedMade':sum(r['repairedMake'] for r in paired)}
    result['complete']=result['pairedLayouts']==1024 and result['completeContexts']==128 and len(tpl)==94
    if tpl:
        rng=np.random.default_rng(57119999+policy_index);ids=sorted(tpl);sample=rng.integers(0,len(ids),(20000,len(ids)))
        for key in ['score','make','tricks']:
            values=np.array([tpl[t][key] for t in ids]);result[key]={'mean':float(values.mean()),'clusterBootstrap95':np.quantile(values[sample].mean(1),[.025,.975]).tolist()}
            if policy in ['fast','strong'] and key in ['score','make']:
                result[key]['fourEndpointBonferroniInterval']=np.quantile(values[sample].mean(1),[.00625,.99375]).tolist()
        for key in ['baselineMake','repairedMake']:result[key]=float(np.mean([tpl[t][key] for t in ids]))
        result['families']={}
        for family_index,family in enumerate(['random-fillers','two-card-perturbations']):
            family_ids=[t for t in ids if (t<128)==(family_index==0)]
            if not family_ids:continue
            rng=np.random.default_rng(58119999+100*policy_index+family_index);sample=rng.integers(0,len(family_ids),(20000,len(family_ids)))
            item={'templates':len(family_ids)}
            for key in ['score','make']:
                values=np.array([tpl[t][key] for t in family_ids]);item[key]={'mean':float(values.mean()),'clusterBootstrap95':np.quantile(values[sample].mean(1),[.025,.975]).tolist()}
            result['families'][family]=item
    summary['policies'][policy]=result
for index,policy in enumerate(['fast','strong']):
    ids=sorted(set(effects[policy])&set(effects['medium']))
    if not ids:continue
    rng=np.random.default_rng(72119001+index);sample=rng.integers(0,len(ids),(20000,len(ids)))
    result={'pairedTemplateClusters':len(ids)}
    for key in ['score','make']:
        values=np.array([effects[policy][t][key]-effects['medium'][t][key] for t in ids])
        result[key]={'meanEffectDifference':float(values.mean()),'pairedClusterBootstrap95':np.quantile(values[sample].mean(1),[.025,.975]).tolist()}
    summary['effectDifferencesVsMedium'][policy]=result
summary['allPoliciesComplete']=all(r['complete'] for r in summary['policies'].values())
(study/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
(study/'template-effects.json').write_text(json.dumps(effects,indent=2)+'\n')
for policy,r in summary['policies'].items():print(policy,r['pairedLayouts'],'/1024 paired layouts',r['completeTemplateClusters'],'/94 templates',r.get('make'),r.get('score'))
print('EFFECT DIFFERENCES',json.dumps(summary['effectDifferencesVsMedium']))
