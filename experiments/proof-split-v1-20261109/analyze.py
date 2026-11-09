"""Paired conditional contract results, never full-match win rates."""
import gzip
import json
from collections import Counter, defaultdict
from pathlib import Path
import numpy as np
from scipy.stats import beta, binomtest

study = Path(__file__).resolve().parent
root = study / 'results'
rng = np.random.default_rng(34119999)
plan = json.loads((root / 'plan.json').read_text())
groups = defaultdict(list)
differences = []
all_metrics = []
complete = 0
for spec in plan['Cases']:
    directory = root / 'deals' / spec['Id']
    arms = {}
    for name in ['baseline', 'declarer-proof', 'defense-proof']:
        path = directory / (name+'.json')
        if not path.exists(): continue
        metric = json.loads(path.read_text())
        assert metric['CompletedDeal'] and metric['Case'] == spec
        all_metrics.append(metric)
        with gzip.open(str(path)+'.gz', 'rt') as source:
            game = json.load(source)
        deal = game['Rounds'][0]
        metric['FirstBustTrick'] = next((i+1 for i, winner in enumerate(deal['TrickWinners']) if winner == spec['Declarer']), None) if spec['Contract']['Kind'] != 'Normal' else None
        assert len(game['Rounds']) == 1 and len(deal['TrickWinners']) == 10 and not deal['Redeal']
        assert deal['Contract'] == spec['Contract'] and deal['Declarer'] == spec['Declarer']
        arms[name] = (metric, deal)
    if len(arms) != 3: continue
    complete += 1
    baseline, base_deal = arms['baseline']
    for name, (metric, deal) in arms.items():
        assert deal['Hands'] == base_deal['Hands'] and deal['Kitty'] == base_deal['Kitty']
        key = lambda cards: sorted((c['Suit'], c['Rank']) for c in cards)
        assert key(metric['Discards']) == key(baseline['Discards']), 'Discard confound'
        if name == 'baseline': continue
        before = [d for d in base_deal['Decisions'] if d['Phase'] == 'play']
        after = [d for d in deal['Decisions'] if d['Phase'] == 'play']
        assert len(before) == len(after)
        changed = next((i for i, (a, b) in enumerate(zip(before, after)) if a != b), None)
        if changed is not None:
            event = next((t for actor in metric['Players'] for t in actor['Tactics'] if t['Turn'] == changed), None)
            differences.append({'case': spec['Id'], 'arm': name, 'turn': changed,
                                'before': before[changed], 'after': after[changed],
                                'reason': event['Reason'] if event and event['Reason'] else 'search-after-earlier-intervention',
                                'baselineMade': baseline['Made'], 'subjectMade': metric['Made']})
    groups[(spec['Contract']['Kind'], spec['Cohort'])].append(arms)

def summarize(cases):
    baseline = np.array([case['baseline'][0]['Made'] for case in cases], dtype=int)
    result = {'pairedSeeds': len(cases), 'baselineMade': int(baseline.sum()), 'baselineMakeRate': float(baseline.mean()), 'roles': {}}
    for arm in ['declarer-proof', 'defense-proof']:
        subject = np.array([case[arm][0]['Made'] for case in cases], dtype=int)
        benefit = subject-baseline if arm == 'declarer-proof' else baseline-subject
        n, better, worse = len(cases), int(sum(benefit == 1)), int(sum(benefit == -1))
        def cp(k): return (float(beta.ppf(.0125, k, n-k+1)) if k else 0., float(beta.ppf(.9875, k+1, n-k)) if k < n else 1.)
        bl, bu = cp(better)
        wl, wu = cp(worse)
        boot = benefit[rng.integers(0, n, (20000, n))].mean(1)
        team = lambda case: case[arm][0]['Case']['Declarer'] % 2 if arm == 'declarer-proof' else 1-case[arm][0]['Case']['Declarer'] % 2
        scores = [case[arm][0]['ScoreDelta'][team(case)]-case['baseline'][0]['ScoreDelta'][team(case)] for case in cases]
        busts = [(case['baseline'][0]['FirstBustTrick'], case[arm][0]['FirstBustTrick']) for case in cases]
        paired_busts = [(before, after) for before, after in busts if before is not None and after is not None]
        def timing(case, variant):
            declarer = case[arm][0]['Case']['Declarer']
            actors = [p for p in case[variant][0]['Players'] if (p['Seat'] == declarer if arm == 'declarer-proof' else p['Seat'] % 2 != declarer % 2)]
            return sum(p['PlayCalls'] for p in actors), sum(p['PlayMilliseconds'] for p in actors)
        base_timing = [timing(case, 'baseline') for case in cases]
        acting_timing = [timing(case, arm) for case in cases]
        result['roles'][arm] = {'made': int(subject.sum()), 'makeRate': float(subject.mean()),
            'betterOutcomes': better, 'worseOutcomes': worse, 'unchangedOutcomes': n-better-worse,
            'benefitRateDifference': float(benefit.mean()), 'benefitBootstrap95': np.quantile(boot, [.025, .975]).tolist(),
            'benefitConservative95': [max(-1., bl-wu), min(1., bu-wl)],
            'exactPairedP': float(binomtest(better, better+worse).pvalue) if better+worse else 1.,
            'meanActingTeamScoreDeltaChange': float(np.mean(scores)),
            'actingSeatLatency': {'calls': sum(n for n, ms in acting_timing),
                'baselineMeanMs': sum(ms for n, ms in base_timing)/sum(n for n, ms in base_timing),
                'subjectMeanMs': sum(ms for n, ms in acting_timing)/sum(n for n, ms in acting_timing)},
            'firstBustTiming': {'comparableFailedDeals': len(paired_busts),
                'earlier': sum(after < before for before, after in paired_busts),
                'same': sum(after == before for before, after in paired_busts),
                'later': sum(after > before for before, after in paired_busts),
                'meanTrickChange': float(np.mean([after-before for before, after in paired_busts])) if paired_busts else None}}
    return result

summary = {'plannedDeals': len(plan['Jobs']), 'savedDeals': len(all_metrics), 'completePairedCases': complete,
           'discardOrDealConfounds': 0, 'groups': {}, 'byContract': {}, 'firstDivergences': differences}
for (kind, cohort), cases in groups.items(): summary['groups'][kind+':'+cohort] = summarize(cases)
for kind in sorted({key[0] for key in groups}):
    summary['byContract'][kind] = summarize([case for (k, cohort), cases in groups.items() if k == kind for case in cases])
events = [t for metric in all_metrics for actor in metric['Players'] for t in actor['Tactics']]
assert all(0 <= t['ProofNodes'] <= 8192 and 0 < t['RetainedCandidates'] <= t['OriginalCandidates'] for t in events)
summary['tactics'] = {'queries': len(events), 'interventions': sum(t['Reason'] is not None for t in events),
    'proofNodes': sum(t['ProofNodes'] for t in events), 'budgetReached': sum(t['ProofNodes'] == 8192 for t in events),
    'reasons': dict(Counter(t['Reason'] for t in events if t['Reason'])),
    'firstDivergenceReasons': dict(Counter(d['reason'] for d in differences))}
summary['latency'] = {arm: {'playCalls': sum(p['PlayCalls'] for m in all_metrics if m['Arm'] == arm for p in m['Players']),
    'playMilliseconds': sum(p['PlayMilliseconds'] for m in all_metrics if m['Arm'] == arm for p in m['Players'])} for arm in ['baseline', 'declarer-proof', 'defense-proof']}
if (root / 'audit.json').exists():
    audit = json.loads((root / 'audit.json').read_text())
    by = defaultdict(dict)
    for row in audit: by[(row['Scenario'], row['Seed'])][row['Profile']] = row
    summary['audit'] = {'queries': len(audit), 'allLegal': all(row['Legal'] for row in audit), 'profiles': {}, 'changesVsAll32': {}}
    for name in ['all32', 'tactical', 'misere-only', 'contract-only']:
        summary['audit']['profiles'][name] = {'failedChecks': sum(not c['Pass'] for row in audit if row['Profile'] == name for c in row['Checks']),
            'criticalFailures': sum(not c['Pass'] and c['Level'] == 'Critical' for row in audit if row['Profile'] == name for c in row['Checks'])}
        summary['audit']['changesVsAll32'][name] = {phase: sum(row[name]['Phase'] == phase and row[name]['Action'] != row['all32']['Action'] for row in by.values()) for phase in ['bid', 'discard', 'play']}
(study / 'summary.json').write_text(json.dumps(summary, indent=2)+'\n')
print('Progress', len(all_metrics), '/', len(plan['Jobs']), 'deals;', complete, 'complete three-arm cases')
for kind, result in summary['byContract'].items():
    print(kind, 'baseline', result['baselineMade'], '/', result['pairedSeeds'], {arm: {k: v[k] for k in ['made', 'betterOutcomes', 'worseOutcomes', 'exactPairedP']} for arm, v in result['roles'].items()})
print('TACTICS', summary['tactics'])
