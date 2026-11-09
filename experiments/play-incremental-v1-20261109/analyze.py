"""Paired descriptive performance by preselected root-state stratum, not win-rate inference."""
from collections import defaultdict, Counter
import json
from pathlib import Path
import numpy as np

study=Path(__file__).resolve().parent
rows=json.loads((study/'results/benchmark.json').read_text())
assert len(rows)==48
assert all(r['SameContinuationWork'] and r['Reference']['Work']==r['Optimized']['Work'] and r['SameActionAndSearch'] and r['Reference']['Action']==r['Optimized']['Action'] and r['Reference']['Search']==r['Optimized']['Search'] and r['Reference']['Rollouts']==r['Optimized']['Rollouts'] for r in rows)
summary={'Cases':24,'Repeats':2,'PairedMeasurements':48,'AllActionsSearchReportsAndRolloutsIdentical':True,'AllContinuationWorkIdentical':True,'Strata':{}}
for bucket in ['early','middle','late','forced']:
    selected=[r for r in rows if r['Bucket']==bucket]
    by_case=defaultdict(list)
    for r in selected:by_case[r['Id']].append(r)
    assert len(by_case)==6 and all(len(xs)==2 for xs in by_case.values())
    # One median per root state: repeats are not counted as independent states.
    cases=[{side:{field:float(np.median([r[side][field] for r in xs])) for field in ['Milliseconds','AllocatedBytes']} for side in ['Reference','Optimized']} for xs in by_case.values()]
    before=float(np.median([c['Reference']['Milliseconds'] for c in cases]));after=float(np.median([c['Optimized']['Milliseconds'] for c in cases]))
    item={'Cases':6,'PairedMeasurements':12,'ReferenceMedianMs':before,'OptimizedMedianMs':after,
          'ReferenceMedianAllocatedBytes':float(np.median([c['Reference']['AllocatedBytes'] for c in cases])),
          'OptimizedMedianAllocatedBytes':float(np.median([c['Optimized']['AllocatedBytes'] for c in cases])),
          'StateMedianOfPairedTimeRatios':float(np.median([c['Optimized']['Milliseconds']/c['Reference']['Milliseconds'] for c in cases])) if bucket!='forced' else None,
          'RootEvaluationsAcrossMeasuredRepeats':sum(r['Optimized']['Work']['Evaluations'] for r in selected),
          'ForcedMovesAcrossMeasuredRepeats':sum(r['Optimized']['Work']['ForcedMoves'] for r in selected),
          'PolicyMovesAcrossMeasuredRepeats':sum(r['Optimized']['Work']['PolicyMoves'] for r in selected)}
    total=item['ForcedMovesAcrossMeasuredRepeats']+item['PolicyMovesAcrossMeasuredRepeats']
    item['ForcedFraction']=item['ForcedMovesAcrossMeasuredRepeats']/total if total else None
    summary['Strata'][bucket]=item
summary['DataPreparation']={
    'RootContextsAcrossMeasuredRepeats':sum(r['Optimized']['Data']['RootContexts'] for r in rows if r['Optimized']['Data']),
    'OptionsCreatedAcrossMeasuredRepeats':sum(r['Optimized']['Data']['OptionsCreated'] for r in rows if r['Optimized']['Data']),
    'AdapterCallsAcrossMeasuredRepeats':sum(r['Optimized']['Data']['AdapterCalls'] for r in rows if r['Optimized']['Data']),
    'HistoryForks':sum(r['Optimized']['Data']['HistoryForks'] for r in rows if r['Optimized']['Data']),
    'HistoryAppends':sum(r['Optimized']['Data']['HistoryAppends'] for r in rows if r['Optimized']['Data']),
    'SeedHistoryCount':sum(r['Optimized']['Data']['SeedHistoryCount'] for r in rows if r['Optimized']['Data'])}
summary['Limits']=['6 root states per stratum,2 repeated measurements each; baseline is cached-play version; descriptive only',
    'Balanced strata do not have natural-match weights; no overall match speedup or win-rate claim; same previous regression states',
    'Normal contracts measured; Misere and configuration equivalence covered by tests, not timed strata',
    'Serial alternating pair order; decision-body allocations on current thread, excludes constructors']
(study/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print(json.dumps(summary,indent=2))
