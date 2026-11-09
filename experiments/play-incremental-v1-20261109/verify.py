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
assert hashlib.sha256((repo/registration['Input']).read_bytes()).hexdigest().upper()==registration['InputSha256']
assert (root/'cases.json').read_bytes()==(repo/registration['Input']).read_bytes()
assert Counter(c['Bucket'] for c in cases)=={'early':6,'middle':6,'late':6,'forced':6}
for c in cases:
    path=repo/f"experiments/natural-matches-v1-20261109/results/matches/block-{c['SourceBlock']:03}/lean.calls.gz"
    with gzip.open(path,'rt') as f:recorded=json.load(f)
    digest=hashlib.sha256(json.dumps(c['View'],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
    assert any(x['Phase']=='play' and x['ContextHash']==digest for x in recorded)
rows=json.loads((root/'benchmark.json').read_text());assert len(rows)==48
for r in rows:
    a,b=r['Reference'],r['Optimized'];assert r['SameActionAndSearch']
    assert a['Action']==b['Action'] and a['Search']==b['Search'] and a['Rollouts']==b['Rollouts']==b['Work']['Evaluations']
    assert a['Work']==b['Work']
    if b['Data']:
        d=b['Data'];w=b['Work']
        assert d['RootContexts']==1 and d['EncodedCards']==43 and d['OptionsCreated']<=5 and d['AdapterCalls']==w['PolicyMoves']
        assert d['HistoryForks']==w['Evaluations'] and d['HistoryAppends']==w['Evaluations']+w['ForcedMoves']+w['PolicyMoves']
    assert b['Work']['ForcedMoves']>=0 and b['Work']['PolicyMoves']>=0
summary=json.loads((study/'summary.json').read_text());assert summary['AllActionsSearchReportsAndRolloutsIdentical']
trx=Path(os.environ.get('HISTORY_TRX','/tmp/play-incremental-validation/targeted.trx'))
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};tree=ET.parse(trx)
counters=tree.find('.//t:Counters',ns).attrib
assert counters['total']==counters['passed']==counters['executed']=='4' and counters['failed']=='0'
ids={t.attrib['id'] for t in tree.findall('.//t:TestDefinitions/t:UnitTest',ns) if t.find('t:TestMethod',ns).attrib.get('className')=='TestBots.TestPlayIncremental'}
new_tests=[t.attrib for t in tree.findall('.//t:UnitTestResult',ns) if t.attrib.get('testId') in ids]
assert len(new_tests)==4 and all(t['outcome']=='Passed' for t in new_tests)
frozen=subprocess.run([os.environ.get('DOTNET','dotnet'),str(repo/'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'),'verify-frozen','--root',str(repo)],check=True,text=True,capture_output=True)
report={'Passed':True,'ActualPublicObservationsVerified':24,'PairedMeasurements':48,'AllRootActionsSearchReportsRolloutsIdentical':True,
        'Tests':counters,'NewTests':new_tests,'SourceHashesVerified':True,'Frozen':frozen.stdout.strip(),
        'Scope':'Step3 incremental public history;4 targeted tests only; full integration deferred by user; not a strength or full-match speedup test'}
(study/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
