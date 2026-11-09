"""Check source immutability, genuine public observations, exact decisions and test results."""
from collections import Counter
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

study=Path(__file__).resolve().parent;repo=study.parent.parent;root=study/'results'
registration=json.loads((root/'registration.json').read_text())
for name,digest in registration['SourceHashes'].items():
    assert hashlib.sha256((repo/name).read_bytes()).hexdigest().upper()==digest
cases=json.loads((root/'cases.json').read_text());assert len(cases)==24
assert Counter(c['Bucket'] for c in cases)=={'early':6,'middle':6,'late':6,'forced':6}
for c in cases:
    path=repo/f"experiments/natural-matches-v1-20261109/results/matches/block-{c['SourceBlock']:03}/lean.calls.gz"
    with gzip.open(path,'rt') as f:recorded=json.load(f)
    digest=hashlib.sha256(json.dumps(c['View'],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
    assert any(x['Phase']=='play' and x['ContextHash']==digest for x in recorded)
rows=json.loads((root/'benchmark.json').read_text());assert len(rows)==72
for r in rows:
    a,b=r['Reference'],r['Optimized'];assert r['SameActionAndSearch']
    assert a['Action']==b['Action'] and a['Search']==b['Search'] and a['Rollouts']==b['Rollouts']==b['Work']['Evaluations']
    assert b['Work']['ForcedMoves']>=0 and b['Work']['PolicyMoves']>=0
summary=json.loads((study/'summary.json').read_text());assert summary['AllActionsSearchReportsAndRolloutsIdentical']
trx=Path(os.environ.get('FORCED_TRX','/tmp/play-forced-validation/full.trx'))
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};tree=ET.parse(trx)
counters=tree.find('.//t:Counters',ns).attrib
assert counters['total']==counters['passed']==counters['executed']=='555' and counters['failed']=='0'
names={'DefaultContinuationActionsAndUtilitiesMatchIncludingMisere','SingleJokerWithFourLeadDeclarationsStillCallsPolicy','ConfiguredContinuationUtilitiesMatchForEveryConfiguration','SequentialRootActionsReportsAndRandomStreamRemainIdentical','DefaultAndConfiguredGameRecordsAreIdenticalAndReplay'}
new_tests=[t.attrib for t in tree.findall('.//t:UnitTestResult',ns) if t.attrib.get('testName') in names]
assert len(new_tests)==5 and all(t['outcome']=='Passed' for t in new_tests)
frozen=subprocess.run([os.environ.get('DOTNET','dotnet'),str(repo/'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'),'verify-frozen','--root',str(repo)],check=True,text=True,capture_output=True)
report={'Passed':True,'ActualPublicObservationsVerified':24,'PairedMeasurements':72,'AllRootActionsSearchReportsRolloutsIdentical':True,
        'Tests':counters,'NewTests':new_tests,'SourceHashesVerified':True,'Frozen':frozen.stdout.strip(),
        'Scope':'Step1 play-only forced-action fast path; not a strength or full-match speedup test'}
(study/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
