"""Paired match effects and template-clustered conditional discard effects."""
import json,gzip,math,itertools
from collections import Counter,defaultdict
from pathlib import Path
import numpy as np
from scipy.stats import beta,binomtest

study=Path(__file__).resolve().parent;root=study/'results';rng=np.random.default_rng(51119999)
summary={'matches':{},'discard':{}}
for group_index,group in enumerate(['weak-opening','random']):
    rng=np.random.default_rng(51119999+group_index)
    games=[json.loads(p.read_text()) for p in sorted((root/'matches'/group).glob('pair-*.json'))]
    pairs=[];unchanged_pairs=0;initial=[]
    for index in range(64):
        legs=sorted([g for g in games if g['Index']==index],key=lambda g:g['Arm'])
        if len(legs)!=2 or any(g['Winner'] is None or g['Truncated'] for g in legs):continue
        assert legs[0]['Roster']==legs[1]['Roster'] and legs[0]['Seed']==legs[1]['Seed']
        records=[]
        for leg in legs:
            with gzip.open(root/'matches'/group/f"pair-{index:03}-arm-{leg['Arm']}.game.gz",'rt') as f:records.append(json.load(f))
        assert all(a['Hands']==b['Hands'] and a['Kitty']==b['Kitty'] and a['Dealer']==b['Dealer'] for a,b in zip(records[0]['Rounds'],records[1]['Rounds']))
        if not legs[1]['Events']:
            assert records[0]['Rounds']==records[1]['Rounds'], 'Unexpected behavior change without a guard event'
            unchanged_pairs+=1
        pairs.append((legs[0],legs[1]))
        first=[record['Rounds'][0] for record in records]
        initial.append({'index':index,'baselineRootWin':legs[0]['RootWin'],'subjectRootWin':legs[1]['RootWin'],
            'subjectEvents':legs[1]['Events'],
            'baselineFirstBid':legs[0]['FirstBid'],'subjectFirstBid':legs[1]['FirstBid'],
            'baselineRootDeclarer':first[0]['Declarer']==0,'subjectRootDeclarer':first[1]['Declarer']==0,
            'baselineContract':first[0]['Contract'],'subjectContract':first[1]['Contract'],
            'baselineFirstRelativeScore':first[0]['ScoreDelta'][0]-first[0]['ScoreDelta'][1] if not first[0]['Redeal'] else 0,
            'subjectFirstRelativeScore':first[1]['ScoreDelta'][0]-first[1]['ScoreDelta'][1] if not first[1]['Redeal'] else 0})
    result={'plannedPairs':64,'savedGames':len(games),'completedPairs':len(pairs),'truncated':sum(g['Truncated'] for g in games),
            'noInterventionIdenticalTrajectoryPairs':unchanged_pairs,
            'subjectEvents':dict(Counter(e['Phase'] for g in games if g['Arm']==1 for e in g['Events']))}
    if pairs:
        diff=np.array([int(b['RootWin'])-int(a['RootWin']) for a,b in pairs]);n=len(diff)
        better,worse=int(sum(diff==1)),int(sum(diff==-1))
        def cp(k):return (float(beta.ppf(.0125,k,n-k+1)) if k else 0.,float(beta.ppf(.9875,k+1,n-k)) if k<n else 1.)
        bl,bu=cp(better);wl,wu=cp(worse)
        boot=diff[rng.integers(0,n,(20000,n))].mean(1)
        result.update(baselineWins=sum(a['RootWin'] for a,b in pairs),subjectWins=sum(b['RootWin'] for a,b in pairs),
            gained=better,lost=worse,bothWin=sum(a['RootWin'] and b['RootWin'] for a,b in pairs),bothLose=sum(not a['RootWin'] and not b['RootWin'] for a,b in pairs),
            winRateDifference=float(diff.mean()),pairedBootstrap95=np.quantile(boot,[.025,.975]).tolist(),
            conservative95=[max(-1.,bl-wu),min(1.,bu-wl)],exactPairedP=float(binomtest(better,better+worse).pvalue) if better+worse else 1.,
            firstBidChanged=sum(a['FirstBid']!=b['FirstBid'] for a,b in pairs),firstBidBaselinePass=sum(a['FirstBid'] is None for a,b in pairs),
            baselineMeanRootDecisionMs=sum(a['Milliseconds'] for a,b in pairs)/sum(a['Calls'] for a,b in pairs),
            subjectMeanRootDecisionMs=sum(b['Milliseconds'] for a,b in pairs)/sum(b['Calls'] for a,b in pairs),commonDealPrefixesVerified=True)
        result['initialDeal']={'baselineRootDeclarer':sum(r['baselineRootDeclarer'] for r in initial),
            'subjectRootDeclarer':sum(r['subjectRootDeclarer'] for r in initial),
            'baselineRootMisere':sum(r['baselineRootDeclarer'] and r['baselineContract']['Kind'] in ['Misere','OpenMisere'] for r in initial),
            'subjectRootMisere':sum(r['subjectRootDeclarer'] and r['subjectContract']['Kind'] in ['Misere','OpenMisere'] for r in initial),
            'meanRelativeScoreChange':float(np.mean([r['subjectFirstRelativeScore']-r['baselineFirstRelativeScore'] for r in initial]))}
        result['pairedDetails']=initial
        result['firstBidStrata']={}
        for label,changed in [('changed',True),('same',False)]:
            rows=[r for r in initial if (r['baselineFirstBid']!=r['subjectFirstBid'])==changed]
            result['firstBidStrata'][label]={'pairs':len(rows),'gained':sum(not r['baselineRootWin'] and r['subjectRootWin'] for r in rows),
                'lost':sum(r['baselineRootWin'] and not r['subjectRootWin'] for r in rows)}
        rng_only=[r for r in initial if r['baselineFirstBid'] is None and r['subjectFirstBid'] is None and
                  len(r['subjectEvents'])==1 and r['subjectEvents'][0]['Phase']=='bid' and r['subjectEvents'][0]['Round']==0]
        result['sameOpeningPassOnlyInitialGuard']={'pairs':len(rng_only),
            'winOutcomeChanges':sum(r['baselineRootWin']!=r['subjectRootWin'] for r in rng_only),
            'interpretation':'Opening actions equal; skipping opening rollout advances future random streams differently; this is not a pure opening-action effect'}
    summary['matches'][group]=result
if (root/'match-plan.json').exists():
    plan=json.loads((root/'match-plan.json').read_text())
    eligible=sum(math.prod(math.comb(size,k) for size,k in zip([6,6,7,7],counts)) for counts in itertools.product(range(5),repeat=4) if sum(counts)==10)
    total=math.comb(43,10)
    summary['openingEligibility']={'screened':plan['Screened'],'qualifying':plan['Qualifying'],'rate':plan['Qualifying']/plan['Screened'],
        'uniformDealExactEligibleHands':eligible,'uniformDealExactTotalHands':total,'uniformDealExactRate':eligible/total,
        'scope':'Default 43-card deck, first opening at zero score; not the per-match event rate'}
proposals=[json.loads(p.read_text()) for p in sorted((root/'proposals').glob('case-*.json'))]
summary['discard']['proposals']={'completed':len(proposals),'planned':1024,'changed':sum(p['Changed'] for p in proposals),
    'changedTemplates':len({p['Template'] for p in proposals if p['Changed']}),'calibrationChanged':sum(p['Changed'] and p['Template']==0 for p in proposals)}
if (root/'selected-discards.json').exists():
    selected=json.loads((root/'selected-discards.json').read_text());summary['discard']['selectedContexts']=len(selected);summary['discard']['selectedTemplates']=len({c['Template'] for c in selected})
    summary['discard']['policyEffects']={}
    for policy_index,policy in enumerate(['medium','weak']):
        rng=np.random.default_rng(57119999+policy_index)
        outcomes=[]
        for case in selected:
            for layout in range(8):
                paths=[root/'continuations'/f"case-{case['Index']:04}"/f"layout-{layout}-{policy}-arm-{arm}.json" for arm in [0,1]]
                if not all(p.exists() for p in paths):continue
                before,after=[json.loads(p.read_text()) for p in paths]
                outcomes.append({'template':case['Template'],'index':case['Index'],'layout':layout,
                    'score':after['RelativeScore']-before['RelativeScore'],'make':int(after['Made'])-int(before['Made']),
                    'tricks':sum(after['Tricks'][::2])-sum(before['Tricks'][::2]),'beforeMade':before['Made'],'afterMade':after['Made']})
        by_context=defaultdict(list)
        for row in outcomes:by_context[(row['template'],row['index'])].append(row)
        # Only completed eight-layout contexts enter the cluster statistic.
        by_template=defaultdict(list)
        for (template,index),rows in by_context.items():
            if len(rows)==8:by_template[template].append({k:np.mean([r[k] for r in rows]) for k in ['score','make','tricks']})
        clusters=[{k:float(np.mean([c[k] for c in contexts])) for k in ['score','make','tricks']} for contexts in by_template.values()]
        result={'completedPairedLayouts':len(outcomes),'completedContexts':sum(len(v) for v in by_template.values()),'templateClusters':len(clusters),
            'baselineMade':sum(o['beforeMade'] for o in outcomes),'subjectMade':sum(o['afterMade'] for o in outcomes)}
        if clusters:
            n=len(clusters);resample=rng.integers(0,n,(20000,n))
            for key in ['score','make','tricks']:
                values=np.array([c[key] for c in clusters]);result[key]={'templateWeightedMeanDifference':float(values.mean()),'templateClusterBootstrap95':np.quantile(values[resample].mean(1),[.025,.975]).tolist()}
        result['families']={}
        for family_index,family in enumerate(['random-fillers','two-card-perturbations']):
            ids=[t for t in by_template if (t<128)==(family_index==0)]
            if not ids:continue
            family_rng=np.random.default_rng(58119999+100*policy_index+family_index)
            sampled=family_rng.integers(0,len(ids),(20000,len(ids)))
            entry={'templateClusters':len(ids)}
            for key in ['score','make']:
                values=np.array([np.mean([c[key] for c in by_template[t]]) for t in ids])
                entry[key]={'templateWeightedMeanDifference':float(values.mean()),'templateClusterBootstrap95':np.quantile(values[sampled].mean(1),[.025,.975]).tolist()}
            result['families'][family]=entry
        summary['discard']['policyEffects'][policy]=result
(study/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
for name,result in summary['matches'].items():
    print('MATCHES',name,{k:result.get(k) for k in ['savedGames','completedPairs','baselineWins','subjectWins','gained','lost','winRateDifference','exactPairedP','subjectEvents']})
print('DISCARD',json.dumps(summary['discard']))
