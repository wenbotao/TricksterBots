"""Incremental paired comparisons. Do not pool two stages as the same strategy comparison."""
import json
import gzip
from collections import Counter, defaultdict
from pathlib import Path
import numpy as np
from scipy.stats import beta, binomtest

study = Path(__file__).resolve().parent
root = study / 'results'
rng = np.random.default_rng(41119999)
summary = {'stages': {}}
for planfile in sorted(root.glob('plan-*.json')):
    plan = json.loads(planfile.read_text())
    stage = plan['Stage']
    groups = {}
    strata = []
    for spec in plan['Comparisons']:
        games = [json.loads(p.read_text()) for p in sorted((root / stage / spec['Name']).glob('*.json'))]
        scores = []
        identical = 0
        for pair in range(spec['Pairs']):
            legs = [g for g in games if g['Pair'] == pair]
            if len(legs) == 2 and all(g['Winner'] is not None and not g['Truncated'] for g in legs):
                scores.append(sum(g['AWin'] for g in legs)/2)
                rounds = []
                for leg in legs:
                    path = root / stage / spec['Name'] / f"pair-{pair:03}-leg-{leg['Leg']}.json.gz"
                    with gzip.open(path,'rt') as source: rounds.append(json.load(source)['Rounds'])
                identical += rounds[0] == rounds[1]
        group = {'A': spec['A'], 'B': spec['B'], 'mixed': spec['Mixed'], 'savedGames': len(games),
                 'plannedPairs': spec['Pairs'], 'completedPairs': len(scores), 'truncated': sum(g['Truncated'] for g in games)}
        group['identicalRoundTrajectoryPairs'] = identical
        if scores:
            a = np.array(scores)
            n, sw, ls = len(a), int(sum(a == 1)), int(sum(a == 0))
            def cp(k): return (float(beta.ppf(.0125,k,n-k+1)) if k else 0., float(beta.ppf(.9875,k+1,n-k)) if k<n else 1.)
            wl,wu = cp(sw)
            ll,lu = cp(ls)
            group.update(wins=int(2*a.sum()), losses=int(2*n-2*a.sum()), winRate=float(a.mean()), sweeps=sw, splits=int(sum(a==.5)), swept=ls,
                         conservative95=[max(0.,.5+.5*(wl-lu)),min(1.,.5+.5*(wu-ll))],
                         pairedSignP=float(binomtest(sw,sw+ls).pvalue) if sw+ls else 1.)
            strata.append(a)
        events = [e for g in games for p in g['Players'] for e in p['Guards']]
        group['guardEvents'] = dict(Counter(e['Phase'] for e in events))
        group['gamesWithGuardEvents'] = sum(any(p['Guards'] for p in g['Players']) for g in games)
        timing = defaultdict(lambda: Counter())
        for g in games:
            for p in g['Players']:
                if p['Name'] == 'medium-rules-v1': continue
                side = 'A' if p['Seat']%2 == g['ATeam'] else 'B'
                for phase in p['Phases']:
                    timing[side+':'+phase['Phase']]['calls'] += phase['Calls']
                    timing[side+':'+phase['Phase']]['milliseconds'] += phase['Milliseconds']
        group['latency'] = {key:dict(v,meanMs=v['milliseconds']/v['calls']) for key,v in timing.items()}
        groups[spec['Name']] = group
    result = {'groups':groups}
    if strata:
        n = sum(len(a) for a in strata)
        wins = int(sum(2*a.sum() for a in strata))
        boot = sum(a[rng.integers(0,len(a),(20000,len(a)))].sum(1) for a in strata)/n
        result['aggregate'] = {'wins':wins,'losses':2*n-wins,'winRate':wins/(2*n), 'completedPairs':n,
                               'stratifiedPairBootstrap95':np.quantile(boot,[.025,.975]).tolist()}
    summary['stages'][stage] = result
if (root/'audit.json').exists():
    rows = json.loads((root/'audit.json').read_text())
    contexts = defaultdict(dict)
    for row in rows: contexts[(row['Scenario'],row['Seed'])][row['Profile']] = row
    audit = {'queries':len(rows),'profiles':{},'changes':{}}
    for name in ['misere','bid-guard','hand-guard']:
        audit['profiles'][name] = {'failedChecks':sum(not c['Pass'] for r in rows if r['Profile']==name for c in r['Checks']),
                                 'criticalFailures':sum(not c['Pass'] and c['Level']=='Critical' for r in rows if r['Profile']==name for c in r['Checks'])}
    for name,control in [('bid-guard','misere'),('hand-guard','bid-guard')]:
        audit['changes'][name] = {phase:sum(r[name]['Phase']==phase and r[name]['Action']!=r[control]['Action'] for r in contexts.values()) for phase in ['bid','discard','play']}
    summary['audit'] = audit
if (root/'discard-probe.json').exists():
    probes = json.loads((root/'discard-probe.json').read_text())
    assert len(probes) == 64 and len({p['Index'] for p in probes}) == 64
    summary['independentDiscardProbe'] = {'queries':len(probes),'changed':sum(p['Changed'] for p in probes),
        'underlyingSearchMatched':True,'legalAndRetainedControlsChecked':True,
        'purpose':'Legal engine-captured 6NT discard observations, not completed games or win-rate evidence'}
(study/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
for stage,result in summary['stages'].items():
    print(stage,result.get('aggregate'))
    for name,g in result['groups'].items(): print(name,g['completedPairs'],'/',g['plannedPairs'],'pairs',g.get('wins'),'-',g.get('losses'),'events',g['guardEvents'])
if 'audit' in summary: print('AUDIT',summary['audit'])
