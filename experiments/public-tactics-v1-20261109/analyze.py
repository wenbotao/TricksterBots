"""Summarize paired outcomes; keep primary All32 comparison separate from Strong reference."""
import json
import gzip
from collections import Counter, defaultdict
from pathlib import Path
import numpy as np
from scipy.stats import beta, binomtest

study = Path(__file__).resolve().parent
root = study / 'results'
rng = np.random.default_rng(19119999)
summary = {'groups': {}, 'comparisons': {}}
plan = json.loads((root / 'plan-validation.json').read_text())
strata = defaultdict(list)
for spec in plan['Comparisons']:
    games = [json.loads(p.read_text()) for p in sorted((root / 'validation' / spec['Name']).glob('*.json'))]
    pairs = []
    for pair in range(spec['Pairs']):
        legs = [g for g in games if g['Pair'] == pair]
        if len(legs) == 2 and all(not g['Truncated'] and g['Winner'] is not None for g in legs):
            pairs.append(sum(g['AWin'] for g in legs) / 2)
    scores = np.array(pairs)
    group = {'opponent': spec['B'], 'mixed': spec['Mixed'], 'savedGames': len(games),
             'completedPairs': len(pairs), 'plannedPairs': spec['Pairs'], 'truncated': sum(g['Truncated'] for g in games)}
    if pairs:
        n = len(pairs)
        sweeps, swept = sum(scores == 1), sum(scores == 0)
        def cp(k):
            return (float(beta.ppf(.0125, k, n-k+1)) if k else 0., float(beta.ppf(.9875, k+1, n-k)) if k < n else 1.)
        wl, wu = cp(sweeps)
        ll, lu = cp(swept)
        boot = scores[rng.integers(0, n, (20000, n))].mean(1)
        group.update(wins=int(2*scores.sum()), losses=int(2*n-2*scores.sum()), winRate=float(scores.mean()),
                     sweeps=int(sweeps), splits=int(sum(scores == .5)), swept=int(swept),
                     bootstrap95=np.quantile(boot, [.025, .975]).tolist(),
                     conservative95=[max(0., .5+.5*(wl-lu)), min(1., .5+.5*(wu-ll))],
                     pairedSignP=float(binomtest(sweeps, sweeps+swept).pvalue) if sweeps+swept else 1.)
        strata[spec['B']].append(scores)
    reports = [t for g in games for actor in g['Players'] for t in actor['Tactics']]
    assert all(0 <= t['ProofNodes'] <= 8192 and 0 < t['RetainedCandidates'] <= t['OriginalCandidates'] for t in reports)
    group['tactics'] = {'queries': len(reports), 'nodes': sum(t['ProofNodes'] for t in reports),
                        'budgetReached': sum(t['ProofNodes'] == 8192 for t in reports),
                        'interventions': sum(t['Reason'] is not None for t in reports),
                        'removedCandidates': sum(t['OriginalCandidates']-t['RetainedCandidates'] for t in reports),
                        'reasons': dict(Counter(t['Reason'] for t in reports if t['Reason']))}
    contracts = Counter()
    for path in sorted((root / 'validation' / spec['Name']).glob('*.json')):
        with gzip.open(str(path)+'.gz', 'rt') as source:
            record = json.load(source)
        contracts.update('redeal' if deal['Redeal'] else deal['Contract']['Kind'] for deal in record['Rounds'])
    group['dealKinds'] = dict(contracts)
    timing = defaultdict(lambda: Counter())
    for g in games:
        for actor in g['Players']:
            if actor['Name'] == 'medium-rules-v1': continue
            side = 'A' if actor['Seat'] % 2 == g['ATeam'] else 'B'
            for phase in actor['Phases']:
                timing[side+':'+phase['Phase']]['calls'] += phase['Calls']
                timing[side+':'+phase['Phase']]['milliseconds'] += phase['Milliseconds']
    group['latency'] = {key: dict(v, meanMs=v['milliseconds']/v['calls']) for key, v in timing.items()}
    summary['groups'][spec['Name']] = group
for opponent, groups in strata.items():
    n = sum(len(g) for g in groups)
    boots = sum(g[rng.integers(0, len(g), (20000, len(g)))].sum(1) for g in groups) / n
    wins = int(2*sum(g.sum() for g in groups))
    summary['comparisons'][opponent] = {'wins': wins, 'losses': 2*n-wins, 'winRate': wins/(2*n),
                                       'completedPairs': n, 'stratifiedPairBootstrap95': np.quantile(boots, [.025, .975]).tolist()}
if (root / 'intervention-review.json').exists():
    review = json.loads((root / 'intervention-review.json').read_text())
    summary['interventionReview'] = {'gamesReplayed': review['GamesReplayed'], 'queries': review['Queries'],
        'freshPairedActionChanges': sum(row['Changed'] for row in review['Rows']),
        'byReason': {reason: {'queries': sum(row['Reason'] == reason for row in review['Rows']),
                             'changed': sum(row['Reason'] == reason and row['Changed'] for row in review['Rows'])}
                     for reason in sorted(set(row['Reason'] for row in review['Rows']))}}
if (root / 'audit.json').exists():
    audit = json.loads((root / 'audit.json').read_text())
    contexts = defaultdict(dict)
    for row in audit: contexts[(row['Scenario'], row['Seed'])][row['Profile']] = row
    summary['audit'] = {'queries': len(audit), 'profiles': {}, 'changesVsAll32': {}}
    for name in ['strong', 'all32', 'plus', 'tactical']:
        rows = [row for row in audit if row['Profile'] == name]
        summary['audit']['profiles'][name] = {'queries': len(rows), 'failedChecks': sum(not c['Pass'] for row in rows for c in row['Checks']),
            'failures': dict(Counter(row['Scenario']+':'+c['Id']+':'+c['Level'] for row in rows for c in row['Checks'] if not c['Pass']))}
    for phase in ['bid', 'discard', 'play']:
        rows = [v for v in contexts.values() if v['tactical']['Phase'] == phase]
        summary['audit']['changesVsAll32'][phase] = {'queries': len(rows), 'changed': sum(v['tactical']['Action'] != v['all32']['Action'] for v in rows),
            'details': [{'scenario': v['tactical']['Scenario'], 'seed': v['tactical']['Seed'], 'before': v['all32']['Action'], 'after': v['tactical']['Action'], 'tactics': v['tactical']['Tactics']} for v in rows if v['tactical']['Action'] != v['all32']['Action']]}
(study / 'summary.json').write_text(json.dumps(summary, indent=2)+'\n')
for name, group in summary['groups'].items():
    print(name, group['completedPairs'], '/', group['plannedPairs'], 'pairs', group.get('wins'), '-', group.get('losses'), group['tactics'])
print('COMPARISONS', json.dumps(summary['comparisons']))
