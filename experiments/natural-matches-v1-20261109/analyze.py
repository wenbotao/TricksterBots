"""Natural complete matches: independent seed-roster blocks, paired outcomes, descriptive telemetry."""
from collections import Counter, defaultdict
import gzip
import json
from pathlib import Path
import numpy as np
from scipy.stats import beta, binomtest

study = Path(__file__).resolve().parent
root = study / 'results'
plans = json.loads((root / 'plan.json').read_text())
read = lambda p: json.loads(p.read_text())
def zipped(p):
    with gzip.open(p, 'rt') as f:
        return json.load(f)
def cp(k, n, alpha=.05):
    if not n:
        return None
    return [0. if k == 0 else float(beta.ppf(alpha/2, k, n-k+1)),
            1. if k == n else float(beta.ppf(1-alpha/2, k+1, n-k))]
def paired(values):
    if not values:
        return None
    a = np.array(values, dtype=float)
    rng = np.random.default_rng(124119001)
    samples = rng.integers(0, len(a), (20000, len(a)))
    return {'mean': float(a.mean()), 'blockBootstrap95': np.quantile(a[samples].mean(1), [.025,.975]).tolist(),
            'degenerateBootstrap': bool(np.all(a == a[0]))}
def describe(values):
    if not values:
        return None
    return {'count': len(values), 'mean': float(np.mean(values)), 'p50': float(np.median(values)), 'p95': float(np.quantile(values,.95))}

metrics = []
blocks = {}
for plan in plans:
    directory = root / 'matches' / f"block-{plan['Id']:03}"
    for arm in ['unpatched','guard','lean']:
        path = directory / (arm + '.json')
        if path.exists():
            metrics.append(read(path))
    if (directory / 'complete.json').exists():
        blocks[plan['Id']] = {arm:read(directory / (arm+'.json')) for arm in ['unpatched','guard','lean']}
summary = {'PlannedBlocks':len(plans), 'PlannedMatches':3*len(plans), 'FinishedMatches':len(metrics),
           'CompletedBlocks':len(blocks), 'Complete':len(blocks)==len(plans), 'Arms':{}, 'Comparisons':{}}
for arm in ['unpatched','guard','lean']:
    rows = [m for m in metrics if m['Arm']==arm]
    finished = [m for m in rows if not m['Truncated'] and m['Winner'] is not None]
    n = len(finished);wins = sum(m['SubjectWin'] for m in finished)
    phases = defaultdict(list)
    for m in rows:
        path = root / 'matches' / f"block-{m['Id']:03}" / (arm+'.calls.gz')
        for call in zipped(path):
            phases[call['Phase']].append(call)
    contract_types = defaultdict(lambda: {'Deals':0,'SubjectTeamContracts':0,'SubjectTeamMade':0,'OpponentContracts':0,'OpponentSet':0})
    for m in rows:
        game = zipped(root / 'matches' / f"block-{m['Id']:03}" / (arm+'.game.gz'))
        for r in game['Rounds']:
            if r['Redeal']:
                continue
            c = r['Contract'];kind = c['Kind'] if c['Kind'] != 'Normal' else ('NoTrump' if c['Trump']=='Unknown' else 'Suit')
            item = contract_types[kind];item['Deals'] += 1
            if r['Declarer'] % 2 == m['SubjectSeat'] % 2:
                item['SubjectTeamContracts'] += 1;item['SubjectTeamMade'] += int(r['Made'])
            else:
                item['OpponentContracts'] += 1;item['OpponentSet'] += int(not r['Made'])
    phase_summary = {}
    for phase,calls in phases.items():
        phase_summary[phase] = {'Milliseconds':describe([c['Milliseconds'] for c in calls]),
            'AllocatedBytes':describe([c['AllocatedBytes'] for c in calls]),
            'KnownRollouts':sum(c['Rollouts'] for c in calls if c['Rollouts'] is not None),
            'RolloutCounterCoverage':sum(c['Rollouts'] is not None for c in calls),
            'TotalMilliseconds':sum(c['Milliseconds'] for c in calls)}
    summary['Arms'][arm] = {'Matches':len(rows),'Completed':n,'Truncated':sum(m['Truncated'] for m in rows),
        'Wins':wins,'Losses':n-wins,'WinRate':wins/n if n else None,'WinRateExact95':cp(wins,n),
        'Rounds':sum(m['Rounds'] for m in rows),'PlayedDeals':sum(m['PlayedDeals'] for m in rows),
        'Redeals':sum(m['Redeals'] for m in rows),'SubjectContracts':sum(m['SubjectContracts'] for m in rows),
        'SubjectMade':sum(m['SubjectMade'] for m in rows),'SubjectNormalNoTrump':sum(m['SubjectNormalNoTrump'] for m in rows),
        'RepairEvents':sum(m['RepairEvents'] for m in rows),'MatchesWithRepair':sum(m['RepairEvents']>0 for m in rows),
        'RelativeTerminalScore':describe([m['RelativeScore'] for m in finished]),'MatchSeconds':describe([m['Seconds'] for m in rows]),
        'SubjectPhaseTelemetry':phase_summary,'ContractTypes':dict(contract_types)}
for new,old in [('lean','unpatched'),('guard','unpatched'),('lean','guard')]:
    rows = [(b[new],b[old]) for b in blocks.values() if not b[new]['Truncated'] and not b[old]['Truncated'] and b[new]['Winner'] is not None and b[old]['Winner'] is not None]
    n = len(rows);improved = sum(a['SubjectWin'] and not b['SubjectWin'] for a,b in rows);worsened = sum(b['SubjectWin'] and not a['SubjectWin'] for a,b in rows)
    upper = cp(improved,n,.025);lower=cp(worsened,n,.025)
    summary['Comparisons'][new+'-'+old] = {'CompletedPairs':n,'Improved':improved,'Worsened':worsened,
        'BothWin':sum(a['SubjectWin'] and b['SubjectWin'] for a,b in rows),'BothLose':sum(not a['SubjectWin'] and not b['SubjectWin'] for a,b in rows),
        'PairedWinRateDifference':(improved-worsened)/n if n else None,
        'ConservativeExact95Difference':[upper[0]-lower[1],upper[1]-lower[0]] if n else None,
        'ExactMcNemarP':float(binomtest(improved,improved+worsened,.5).pvalue) if improved+worsened else 1.,
        'PairedRelativeTerminalScore':paired([a['RelativeScore']-b['RelativeScore'] for a,b in rows]),
        'PairedRounds':paired([a['Rounds']-b['Rounds'] for a,b in rows])}
# No pooling of guard+lean: they are the same strategy on the same paired deals.
lean_rows = [m for m in metrics if m['Arm']=='lean']
events = sum(m['RepairEvents'] for m in lean_rows)
exposures = sum(m['SubjectNormalNoTrump'] for m in lean_rows)
summary['NaturalRepairExposure'] = {'Events':events,'SubjectNormalNoTrump':exposures,
    'FractionOfSubjectNormalNoTrump':events/exposures if exposures else None,
    'MatchesWithRepair':sum(m['RepairEvents']>0 for m in lean_rows),'ObservedMatches':len(lean_rows),
    'MatchRepairProbabilityExact95':cp(sum(m['RepairEvents']>0 for m in lean_rows),len(lean_rows)),
    'Note':'Only lean counted; repeated guard is not an independent exposure; contract-level event frequency descriptive, not independent binomial inference'}
strata = defaultdict(list)
for m in lean_rows:
    strata[m['Roster'][(m['SubjectSeat']+2)%4]].append(m)
summary['LeanPartnerStrata'] = {k:{'Matches':len(v),'Completed':sum(not x['Truncated'] for x in v),'Wins':sum(x['SubjectWin'] for x in v if not x['Truncated'])} for k,v in strata.items()}
comparison_path = root / 'comparisons.json'
if comparison_path.exists():
    comparisons=read(comparison_path)
    summary['Behavior']={'VerifiedBlocks':len(comparisons),'IdenticalUnpatchedActions':sum(x['SameActions'] for x in comparisons),
        'ChangedUnpatchedActions':sum(not x['SameActions'] for x in comparisons),'Differences':[x for x in comparisons if not x['SameActions']]}
summary['Limits']=['32 independent seed-roster blocks; arms share deals, do not count96 as independent comparative samples',
    'Default rules only; no screened hands or parameter retuning; no statement about all variants',
    'Terminal score subject to game stopping and variable rounds; descriptive alongside win outcome',
    'Two concurrent blocks; natural timings include warmup and GC; no speedup conclusion',
    'Unpatched rollout diagnostics unavailable rather than zero; subject telemetry only, excludes other players']
(study/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print('MATCHES',len(metrics),'/',3*len(plans),'BLOCKS',len(blocks),'/',len(plans))
for arm,data in summary['Arms'].items():
    print(arm,'W/L/truncated',data['Wins'],data['Losses'],data['Truncated'],'rounds',data['Rounds'],'repairs',data['RepairEvents'])
print('PAIRED',json.dumps(summary['Comparisons']))
